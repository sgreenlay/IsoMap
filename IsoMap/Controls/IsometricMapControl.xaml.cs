
//#define USE_HEXAGONAL_TILES

using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace IsoMap.Controls
{
    public sealed partial class IsometricMapControl : UserControl
    {
        public enum MapTileShape
        {
            Square,
            Hexagon
        };

        public MapTileShape TileShape { get; set; }
        public Size TileSize { get; set; }

        private Vector2 HighlightedTile { get; set; }
        private Vector2? SelectedTile { get; set; }

        private Vector2 ScreenOffset { get; set; }
        private Vector2 TileOffset { get; set; }

        private enum Team
        {
            TeamA,
            TeamB
        };

        private Team ActiveTeam;

        private CanvasBitmap TreeTallBitmap;
        private CanvasBitmap TreeShortBitmap;
        private CanvasBitmap RockBitmap;
        private CanvasBitmap HeartBitmap;

        private class Units
        {
            public List<IntVector2> Positions = new List<IntVector2>();
            public List<string> Names = new List<string>();
            public List<int> Healths = new List<int>();
            public List<int> MaxHealths = new List<int>();

            private static List<string> Syllables = new List<string>(){
                "ga","ka","sa","ta","na","ha","ma","ya","ra","wa",
                "ge","ke","se","te","ne","he","me",/* */"re",/* */
                "gi","ki","si","chi","ni","hi","mi",/* */"ri",/* */
                "go","ko","so","to","no","ho","mo","yo","ro","wo",
                "gu","ku","su","tsu","nu","hu","mu","yu","ru",
            };
            private static Random Rand = new Random();
            private static string randSyl()
            {
                return Syllables[Rand.Next(Syllables.Count)];
            }

            internal bool Contains(IntVector2 pos)
            {
                return Positions.Contains(pos);
            }
            internal int IndexOf(IntVector2 pos)
            {
                return Positions.IndexOf(pos);
            }

            internal static string randName()
            {
                string name = "";
                for (var x = 0; x < 3; ++x)
                {
                    name += randSyl();
                }
                name = char.ToUpper(name[0]) + name.Substring(1);
                return name;
            }

            internal void Add(IntVector2 pos)
            {
                Positions.Add(pos);
                Names.Add(randName());
                Healths.Add(3);
                MaxHealths.Add(3);
            }

            internal void Remove(IntVector2 selectedTile)
            {
                var idx = Positions.IndexOf(selectedTile);
                Remove(idx);
            }
            internal CanvasBitmap Bitmap { get; set; }
            internal int offset;

            internal int Count { get { return Positions.Count; } }

            internal void Move(IntVector2 source, IntVector2 destination)
            {
                var idx = Positions.IndexOf(source);
                if (idx == -1)
                    throw new ArgumentException();
                Positions[idx] = destination;
            }

            internal void Damage(int idx, int v)
            {
                Healths[idx] -= 1;
                if (Healths[idx] == 0)
                    Remove(idx);
            }
            private void Remove(int idx)
            {
                Positions.RemoveAt(idx);
                Names.RemoveAt(idx);
                Healths.RemoveAt(idx);
                MaxHealths.RemoveAt(idx);
            }
        };

        private Units TeamA = new Units();
        private Units TeamB = new Units();

        private Task LoadingAssetsTask;
        private Random Rand = new Random();

        private enum Phase
        {
            Move,
            Shoot
        };

        private Phase ActivePhase;

        private struct IntVector2
        {
            public IntVector2(int x, int y) { X = x; Y = y; }
            public int X, Y;

            public static IntVector2 operator +(IntVector2 a, IntVector2 b)
            {
                return new IntVector2(a.X + b.X, a.Y + b.Y);
            }
        }
        private IntVector2 TerrainSize = new IntVector2(9, 7);
        private Vector2 TerrainTopLeft = new Vector2(4.0f, -3.0f);

        private enum TerrainType
        {
            Empty,
            Solid,
            Transparent,
            Soft
        };
        private struct ImageAndOffset
        {
            IntVector2 offset;
            CanvasBitmap image;
        };
        private List<TerrainType> Terrain;
        private List<CanvasBitmap> ForegroundImages;
        private Vector2[] ForegroundOffsets;

        private int[] PathFindData;
        private BitArray MovableOverlay;
        private int TerrainXYToIndex(IntVector2 v)
        {
            return TerrainXYToIndex(v.X, v.Y);
        }
        private int TerrainXYToIndex(int x, int y)
        {
            Debug.Assert(ValidTerrainXY(x, y));
            return x + y * TerrainSize.X;
        }
        private IntVector2 IndexToTerrainXY(int idx)
        {
            return new IntVector2(idx % TerrainSize.X, idx / TerrainSize.X);
        }
        private IntVector2 WorldToTerrainXY(Vector2 pos)
        {
            var pos2 = pos - TerrainTopLeft;
            return new IntVector2((int)pos2.X, (int)pos2.Y);
        }
        private Vector2 TerrainXYToWorld(IntVector2 pos)
        {
            return TerrainXYToWorld(pos.X, pos.Y);
        }
        private Vector2 TerrainXYToWorld(int x, int y)
        {
            return new Vector2(x, y) + TerrainTopLeft;
        }
        private bool ValidTerrainXY(IntVector2 v) { return ValidTerrainXY(v.X, v.Y); }
        private bool ValidTerrainXY(int x, int y) { return (x >= 0 && x < TerrainSize.X) && (y >= 0 && y < TerrainSize.Y); }

        void ClearPathData()
        {
            for (int x = 0; x < PathFindData.Length; ++x) PathFindData[x] = 0;
        }
        void FindAllPaths(IntVector2 src, int stepsToRecurse)
        {
            Debug.Assert(stepsToRecurse > 0);
            if (!ValidTerrainXY(src))
                return;
            var idx = TerrainXYToIndex(src);

            var ttype = Terrain[idx];
            if (ttype == TerrainType.Solid || ttype == TerrainType.Transparent)
                return;
            if (CurrentTeam().Contains(src))
                return;

            if (PathFindData[idx] >= stepsToRecurse)
                return;
            PathFindData[idx] = stepsToRecurse;

            if (EnemyTeam().Contains(src))
                return;

            if (stepsToRecurse == 1)
                return;

            FindAllPaths(src + new IntVector2(1, 0), stepsToRecurse - 1);
            FindAllPaths(src + new IntVector2(-1, 0), stepsToRecurse - 1);
            FindAllPaths(src + new IntVector2(0, 1), stepsToRecurse - 1);
            FindAllPaths(src + new IntVector2(0, -1), stepsToRecurse - 1);
        }
        void CopyPathDataToMovableOverlay()
        {
            for (int i = 0; i < TerrainSize.X * TerrainSize.Y; ++i)
            {
                MovableOverlay[i] = PathFindData[i] > 0;
            }
        }

        public IsometricMapControl()
        {
            InitializeComponent();

#if USE_HEXAGONAL_TILES
            TileShape = MapTileShape.Hexagon;
            TileSize = new Size(150, 125);
#else
            TileShape = MapTileShape.Square;
            TileSize = new Size(150, 85);
#endif

            TileOffset = new Vector2(0.0f, 0.0f);
            ScreenOffset = new Vector2(0.0f, 0.0f);

            MapCanvas.PointerMoved += OnPointerMoved;
            MapCanvas.PointerPressed += OnPointerPressed;

            CoreWindow.GetForCurrentThread().KeyDown += OnKeyDown;

            Terrain = new List<TerrainType>();
            MovableOverlay = new BitArray(TerrainSize.X * TerrainSize.Y);
            PathFindData = new int[TerrainSize.X * TerrainSize.Y];
            ForegroundImages = new List<CanvasBitmap>();
            ForegroundOffsets = new Vector2[TerrainSize.X * TerrainSize.Y];
            ClearPathData();
            for (var y = 0; y < TerrainSize.Y; ++y)
            {
                for (var x = 0; x < TerrainSize.X; ++x)
                {
                    var r = Rand.NextDouble();
                    if (r < 0.85)
                        Terrain.Add(TerrainType.Empty);
                    else if (r < 0.90)
                        Terrain.Add(TerrainType.Soft);
                    else if (r < 0.95)
                        Terrain.Add(TerrainType.Solid);
                    else
                        Terrain.Add(TerrainType.Transparent);

                    ForegroundImages.Add(null);
                }
            }

            while (TeamA.Count < 4)
            {
                var tpos = new IntVector2(Rand.Next(TerrainSize.X), Rand.Next(TerrainSize.Y));
                if (Terrain[TerrainXYToIndex(tpos)] != TerrainType.Empty)
                    continue;
                if (TeamA.Contains(tpos))
                    continue;
                TeamA.Add(tpos);
            }

            while (TeamB.Count < 4)
            {
                var tpos = new IntVector2(Rand.Next(TerrainSize.X), Rand.Next(TerrainSize.Y));
                if (Terrain[TerrainXYToIndex(tpos)] != TerrainType.Empty)
                    continue;
                if (TeamA.Contains(tpos))
                    continue;
                if (TeamB.Contains(tpos))
                    continue;
                TeamB.Add(tpos);
            }

            ActiveTeam = Team.TeamA;
            ActivePhase = Phase.Move;
        }

        private async Task LoadAssets()
        {
            TeamA.Bitmap = await CanvasBitmap.LoadAsync(MapCanvas, "Assets/Game/Character Boy.png");
            TeamA.offset = -35;
            Debug.Assert(TeamA.Bitmap != null);

            TeamB.Bitmap = await CanvasBitmap.LoadAsync(MapCanvas, "Assets/Game/Enemy Bug.png");
            TeamB.offset = -35;
            Debug.Assert(TeamB.Bitmap != null);

            TreeTallBitmap = await CanvasBitmap.LoadAsync(MapCanvas, "Assets/Game/Tree Tall.png");
            TreeShortBitmap = await CanvasBitmap.LoadAsync(MapCanvas, "Assets/Game/Tree Short.png");
            RockBitmap = await CanvasBitmap.LoadAsync(MapCanvas, "Assets/Game/Rock.png");
            HeartBitmap = await CanvasBitmap.LoadAsync(MapCanvas, "Assets/Game/Heart.png");

            MapCanvas.Invalidate();
        }

        private void OnPointerMoved(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var currentPoint = e.GetCurrentPoint(MapCanvas);

            HighlightedTile = ScreenToMap(
                new Vector2((float)currentPoint.Position.X,
                            (float)currentPoint.Position.Y));

            MapCanvas.Invalidate();

            e.Handled = true;
        }

        private void ClearSelection()
        {
            SelectedTile = null;
            MovableOverlay.SetAll(false);
        }
        private Units CurrentTeam()
        {
            if (ActiveTeam == Team.TeamA) return TeamA;
            else return TeamB;
        }
        private Units EnemyTeam()
        {
            if (ActiveTeam == Team.TeamA) return TeamB;
            else return TeamA;
        }

        private void SetSelection(Vector2 sel)
        {
            SelectedTile = sel;
            if (ActivePhase == Phase.Move)
            {
                ClearPathData();
                var tpos = WorldToTerrainXY(sel);
                FindAllPaths(tpos + new IntVector2(1, 0), 3);
                FindAllPaths(tpos + new IntVector2(-1, 0), 3);
                FindAllPaths(tpos + new IntVector2(0, 1), 3);
                FindAllPaths(tpos + new IntVector2(0, -1), 3);
                CopyPathDataToMovableOverlay();
            }
            else if (ActivePhase == Phase.Shoot)
            {
                MovableOverlay.SetAll(false);
                var tpos = WorldToTerrainXY(sel);
                for (var x = tpos.X + 1; x < TerrainSize.X; ++x)
                {
                    var idx = TerrainXYToIndex(x, tpos.Y);
                    var ttype = Terrain[idx];
                    if (ttype == TerrainType.Solid)
                        break;
                    if (CurrentTeam().Contains(new IntVector2(x, tpos.Y)))
                        continue;
                    MovableOverlay.Set(idx, true);
                    if (ttype == TerrainType.Soft)
                        break;
                }
                for (var x = tpos.X - 1; x >= 0; --x)
                {
                    var idx = TerrainXYToIndex(x, tpos.Y);
                    var ttype = Terrain[idx];
                    if (ttype == TerrainType.Solid)
                        break;
                    if (CurrentTeam().Contains(new IntVector2(x, tpos.Y)))
                        continue;
                    MovableOverlay.Set(idx, true);
                    if (ttype == TerrainType.Soft)
                        break;
                }
                for (var y = tpos.Y + 1; y < TerrainSize.Y; ++y)
                {
                    var idx = TerrainXYToIndex(tpos.X, y);
                    var ttype = Terrain[idx];
                    if (ttype == TerrainType.Solid)
                        break;
                    if (CurrentTeam().Contains(new IntVector2(tpos.X, y)))
                        continue;
                    MovableOverlay.Set(idx, true);
                    if (ttype == TerrainType.Soft)
                        break;
                }
                for (var y = tpos.Y - 1; y >= 0; --y)
                {
                    var idx = TerrainXYToIndex(tpos.X, y);
                    var ttype = Terrain[idx];
                    if (ttype == TerrainType.Solid)
                        break;
                    if (CurrentTeam().Contains(new IntVector2(tpos.X, y)))
                        continue;
                    MovableOverlay.Set(idx, true);
                    if (ttype == TerrainType.Soft)
                        break;
                }
            }
        }

        private void OnPointerPressed(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var currentPoint = e.GetCurrentPoint(MapCanvas);

            var selectedTile = ScreenToMap(
                new Vector2((float)currentPoint.Position.X,
                            (float)currentPoint.Position.Y));
            var selectedXY = WorldToTerrainXY(selectedTile);

            if (currentPoint.Properties.IsLeftButtonPressed)
            {
                if (ActivePhase == Phase.Move)
                {
                    ClearSelection();

                    if (CurrentTeam().Contains(selectedXY))
                    {
                        SetSelection(selectedTile);
                    }
                }
            }
            else if (currentPoint.Properties.IsRightButtonPressed)
            {
                if (ActivePhase == Phase.Move)
                {
                    if (SelectedTile != null && ValidTerrainXY(selectedXY) && MovableOverlay.Get(TerrainXYToIndex(selectedXY)))
                    {
                        if (EnemyTeam().Contains(selectedXY))
                            EnemyTeam().Remove(selectedXY);

                        CurrentTeam().Move(WorldToTerrainXY(SelectedTile.Value), selectedXY);

                        ActivePhase = Phase.Shoot;

                        SetSelection(selectedTile);
                    }
                }
                else if (ActivePhase == Phase.Shoot)
                {
                    Debug.Assert(SelectedTile != null);
                    if (ValidTerrainXY(selectedXY) && MovableOverlay.Get(TerrainXYToIndex(selectedXY)))
                    {
                        var idx = EnemyTeam().IndexOf(selectedXY);
                        if (idx != -1)
                        {
                            EnemyTeam().Damage(idx, 1);
                        }

                        ClearSelection();
                        ActivePhase = Phase.Move;
                        ActiveTeam = Team.TeamB;
                        AITurn();
                        ActiveTeam = Team.TeamA;
                    }
                }
            }

            MapCanvas.Invalidate();

            e.Handled = true;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.VirtualKey == Windows.System.VirtualKey.Up)
            {
                ScrollMap(new Vector2(0.0f, +5.0f));
            }
            else if (e.VirtualKey == Windows.System.VirtualKey.Down)
            {
                ScrollMap(new Vector2(0.0f, -5.0f));
            }
            else if (e.VirtualKey == Windows.System.VirtualKey.Left)
            {
                ScrollMap(new Vector2(+5.0f, 0.0f));
            }
            else if (e.VirtualKey == Windows.System.VirtualKey.Right)
            {
                ScrollMap(new Vector2(-5.0f, 0.0f));
            }

            MapCanvas.Invalidate();

            e.Handled = true;
        }

        Vector2 MapToScreen(Vector2 mapCoordinates)
        {
            Vector2 screenCoordinates = new Vector2(0.0f, 0.0f);

            if (TileShape == MapTileShape.Square)
            {
                screenCoordinates.X = (float)((mapCoordinates.X - mapCoordinates.Y) *
                    (TileSize.Width / 2.0f));

                screenCoordinates.Y = (float)((mapCoordinates.X + mapCoordinates.Y) *
                    (TileSize.Height / 2.0f));
            }
            else if (TileShape == MapTileShape.Hexagon)
            {
                if (Math.Floor(mapCoordinates.Y) % 2.0f == 0)
                {
                    screenCoordinates.X = (float)(mapCoordinates.X * TileSize.Width);
                }
                else
                {
                    screenCoordinates.X = (float)((mapCoordinates.X + 0.5f) * TileSize.Width);
                }

                screenCoordinates.Y = (float)(mapCoordinates.Y * TileSize.Height * 0.75);
            }

            return screenCoordinates + ScreenOffset;
        }

        Vector2 ScreenToMap(Vector2 screenCoordinates)
        {
            Vector2 onscreenTile = new Vector2(0.0f, 0.0f);

            screenCoordinates -= ScreenOffset;

            if (TileShape == MapTileShape.Square)
            {
                onscreenTile.X = (float)Math.Floor(
                    (screenCoordinates.X / ((float)TileSize.Width / 2.0f) +
                    screenCoordinates.Y / ((float)TileSize.Height / 2.0f)) / 2.0f);

                onscreenTile.Y = (float)Math.Floor(
                    (screenCoordinates.Y / ((float)TileSize.Height / 2.0f) -
                    (screenCoordinates.X / ((float)TileSize.Width / 2.0f))) / 2.0f);
            }

            else if (TileShape == MapTileShape.Hexagon)
            {
                onscreenTile.Y = (float)Math.Floor(
                    (screenCoordinates.Y / (float)(TileSize.Height * 0.75)));

                float yOffset = screenCoordinates.Y - (onscreenTile.Y * (float)TileSize.Height * 0.75f);

                float w = (float)(TileSize.Width);
                float h = (float)(TileSize.Height) * 0.25f;

                if (yOffset < h)
                {
                    float xMod;

                    if (onscreenTile.Y % 2.0f != 0)
                    {
                        xMod = (screenCoordinates.X + (0.5f * w)) % w;
                    }
                    else
                    {
                        xMod = screenCoordinates.X % w;
                    }

                    float t1 = yOffset / h + 2.0f * xMod / w;
                    if (t1 < 1.0f)
                    {
                        onscreenTile.Y -= 1; // dark
                    }
                    else
                    {
                        float t2 = -yOffset / h + 2.0f * xMod / w;
                        if (t2 > 1.0f)
                        {
                            onscreenTile.Y -= 1; // dark
                        }
                    }
                }

                if (onscreenTile.Y % 2 == 0)
                {
                    onscreenTile.X = (float)Math.Floor(
                        (screenCoordinates.X / (float)(TileSize.Width)));
                }
                else
                {
                    onscreenTile.X = (float)Math.Floor(
                        (screenCoordinates.X / (float)(TileSize.Width)) - 0.5f);
                }
            }

            return onscreenTile + TileOffset;
        }

        void ScrollMap(Vector2 relativeScreenOffset)
        {
            Vector2 screenOffset = relativeScreenOffset + ScreenOffset;

            if (TileShape == MapTileShape.Square)
            {
                if (screenOffset.X / TileSize.Width >= 1.0f)
                {
                    TileOffset = new Vector2(TileOffset.X - 1.0f, TileOffset.Y + 1.0f);
                }
                else if (screenOffset.X / TileSize.Width <= -1.0f)
                {
                    TileOffset = new Vector2(TileOffset.X + 1.0f, TileOffset.Y - 1.0f);
                }

                if (screenOffset.Y / TileSize.Height >= 1.0f)
                {
                    TileOffset = new Vector2(TileOffset.X - 1.0f, TileOffset.Y - 1.0f);
                }
                else if (screenOffset.Y / TileSize.Height <= -1.0f)
                {
                    TileOffset = new Vector2(TileOffset.X + 1.0f, TileOffset.Y + 1.0f);
                }

                ScreenOffset = new Vector2(
                    (float)(screenOffset.X % TileSize.Width),
                    (float)(screenOffset.Y % TileSize.Height));
            }
            else if (TileShape == MapTileShape.Hexagon)
            {
                if (screenOffset.X / TileSize.Width >= 1.0f)
                {
                    TileOffset = new Vector2(TileOffset.X - 1.0f, TileOffset.Y);
                }
                else if (screenOffset.X / TileSize.Width <= -1.0f)
                {
                    TileOffset = new Vector2(TileOffset.X + 1.0f, TileOffset.Y);
                }

                if (screenOffset.Y / (TileSize.Height * 1.5f) >= 1.0f)
                {
                    TileOffset = new Vector2(TileOffset.X, TileOffset.Y - 2.0f);
                }
                else if (screenOffset.Y / (TileSize.Height * 1.5f) <= -1.0f)
                {
                    TileOffset = new Vector2(TileOffset.X, TileOffset.Y + 2.0f);
                }

                ScreenOffset = new Vector2(
                    (float)(screenOffset.X % TileSize.Width),
                    (float)(screenOffset.Y % (TileSize.Height * 1.5f)));
            }
        }

        void ScrollMapToCenterOnTile(Vector2 absoluteTile)
        {
            // TODO
        }

        void AITurn()
        {
            Debug.Assert(ActivePhase == Phase.Move);

            if (CurrentTeam().Count == 0)
                return;

            var idx = Rand.Next(CurrentTeam().Count);
            ClearPathData();
            var tpos = CurrentTeam().Positions[idx];
            FindAllPaths(tpos + new IntVector2(1, 0), 4);
            FindAllPaths(tpos + new IntVector2(-1, 0), 4);
            FindAllPaths(tpos + new IntVector2(0, 1), 4);
            FindAllPaths(tpos + new IntVector2(0, -1), 4);
            CopyPathDataToMovableOverlay();

            for (var i = 0; i < EnemyTeam().Count; ++i)
            {
                var epos = EnemyTeam().Positions[i];
                var eidx = TerrainXYToIndex(epos);
                if (MovableOverlay.Get(eidx))
                {
                    EnemyTeam().Remove(epos);
                    CurrentTeam().Move(tpos, epos);
                    return;
                }
            }

            // No enemies in range. Move randomly. Accumulate a list of all indexes.
            List<int> indexes = new List<int>();
            for (var i = 0; i < MovableOverlay.Length; ++i)
            {
                if (MovableOverlay[i])
                    indexes.Add(i);
            }
            if (indexes.Count == 0)
            {
                // no valid squares for movement
                return;
            }
            // Select randomly from the list
            var tgt_idx = indexes[Rand.Next(indexes.Count)];
            var tgt_xy = IndexToTerrainXY(tgt_idx);
            CurrentTeam().Move(CurrentTeam().Positions[idx], tgt_xy);
        }

        void Redraw(CanvasControl canvas, CanvasDrawEventArgs args)
        {
            if (LoadingAssetsTask == null) LoadingAssetsTask = LoadAssets();

            if (!LoadingAssetsTask.IsCompleted) return;
            if (LoadingAssetsTask.IsFaulted)
            {
                throw LoadingAssetsTask.Exception;
            }

            for (int i = 0; i < ForegroundImages.Count; ++i)
            {
                ForegroundImages[i] = null;
            }

            Action<Units> prepare_foreground_images = (Units team) =>
            {
                Vector2 pos = -team.Bitmap.Size.ToVector2() / 2.0f;
                pos.Y += team.offset;
                for (var i = 0; i < team.Count; ++i)
                {
                    var idx = TerrainXYToIndex(team.Positions[i]);
                    Debug.Assert(ForegroundImages[idx] == null);
                    ForegroundImages[idx] = team.Bitmap;
                    ForegroundOffsets[idx] = pos;
                }
            };
            prepare_foreground_images(TeamA);
            prepare_foreground_images(TeamB);

            for (int y = -3; y <= (int)Math.Ceiling(canvas.ActualHeight / TileSize.Height * 2) + 1; ++y)
            {
                for (int x = -3; x <= (int)Math.Ceiling(canvas.ActualWidth / TileSize.Width) + 1; ++x)
                {
                    Vector2 onscreenTile = new Vector2(0.0f, 0.0f);

                    var path = new CanvasPathBuilder(canvas);

                    if (TileShape == MapTileShape.Square)
                    {
                        onscreenTile.X = (float)Math.Floor((double)(x + (y / 2)));
                        onscreenTile.Y = (float)Math.Floor((double)(-x + (y / 2) + (y % 2)));

                        var tileRect = new Rect(onscreenTile.X, onscreenTile.Y, 1, 1);

                        path.BeginFigure(
                            MapToScreen(
                                new Vector2((float)tileRect.Left, (float)tileRect.Top)));

                        path.AddLine(
                            MapToScreen(
                                new Vector2((float)tileRect.Right, (float)tileRect.Top)));
                        path.AddLine(
                            MapToScreen(
                                new Vector2((float)tileRect.Right, (float)tileRect.Bottom)));
                        path.AddLine(
                            MapToScreen(
                                new Vector2((float)tileRect.Left, (float)tileRect.Bottom)));

                        path.EndFigure(CanvasFigureLoop.Closed);
                    }
                    else if (TileShape == MapTileShape.Hexagon)
                    {
                        onscreenTile.X = (float)x;
                        onscreenTile.Y = (float)y;

                        var topLeft = MapToScreen(onscreenTile);

                        var bottomRight = new Vector2(
                            (float)(topLeft.X + TileSize.Width),
                            (float)(topLeft.Y + TileSize.Height));

                        path.BeginFigure(new Vector2(
                            topLeft.X + 0.5f * (float)TileSize.Width,
                            topLeft.Y));

                        path.AddLine(new Vector2(
                            bottomRight.X,
                            topLeft.Y + 0.25f * (float)TileSize.Height));
                        path.AddLine(new Vector2(
                            bottomRight.X,
                            bottomRight.Y - 0.25f * (float)TileSize.Height));
                        path.AddLine(new Vector2(
                            bottomRight.X - 0.5f * (float)TileSize.Width,
                            bottomRight.Y));
                        path.AddLine(new Vector2(
                            topLeft.X,
                            bottomRight.Y - 0.25f * (float)TileSize.Height));
                        path.AddLine(new Vector2(
                            topLeft.X,
                            topLeft.Y + 0.25f * (float)TileSize.Height));

                        path.EndFigure(CanvasFigureLoop.Closed);
                    }

                    var tile = onscreenTile + TileOffset;
                    var geometry = CanvasGeometry.CreatePath(path);

                    var terrainxy = WorldToTerrainXY(tile);

                    if (tile == HighlightedTile && HighlightedTile == SelectedTile)
                    {
                        args.DrawingSession.FillGeometry(geometry, Colors.Red);
                        args.DrawingSession.DrawGeometry(geometry, Color.FromArgb(255, 40, 40, 40));
                    }
                    else if (tile == SelectedTile)
                    {
                        args.DrawingSession.FillGeometry(geometry, Colors.DarkRed);
                        args.DrawingSession.DrawGeometry(geometry, Color.FromArgb(255, 40, 40, 40));
                    }
                    else if (tile == HighlightedTile)
                    {
                        args.DrawingSession.FillGeometry(geometry, Colors.CornflowerBlue);
                        args.DrawingSession.DrawGeometry(geometry, Color.FromArgb(255, 40, 40, 40));
                    }
                    else if (!ValidTerrainXY(terrainxy))
                    {
                        args.DrawingSession.FillGeometry(geometry, Colors.DarkSlateGray);
                        args.DrawingSession.DrawGeometry(geometry, Colors.Black);
                    }
                    else if (SelectedTile != null && MovableOverlay.Get(TerrainXYToIndex(terrainxy)))
                    {
                        args.DrawingSession.FillGeometry(geometry, Colors.GreenYellow);
                        args.DrawingSession.DrawGeometry(geometry, Colors.Black);
                    }
                    else
                    {
                        args.DrawingSession.FillGeometry(geometry, Colors.LightGreen);
                    }

                    if (ValidTerrainXY(terrainxy))
                    {
                        var idx = TerrainXYToIndex(terrainxy);
                        args.DrawingSession.DrawGeometry(geometry, Colors.Black);

                        var tpos = MapToScreen(onscreenTile + new Vector2(0.5f, 0.5f));
                        var pos = tpos + new Vector2(-50f, -140f);
                        switch (Terrain[idx])
                        {
                            case TerrainType.Empty:
                                break;
                            case TerrainType.Soft:
                                args.DrawingSession.DrawImage(TreeShortBitmap, pos);
                                break;
                            case TerrainType.Solid:
                                args.DrawingSession.DrawImage(RockBitmap, pos);
                                break;
                            case TerrainType.Transparent:
                                args.DrawingSession.DrawImage(TreeTallBitmap, pos);
                                break;
                        }

                        if (ForegroundImages[idx] != null)
                        {
                            args.DrawingSession.DrawImage(ForegroundImages[idx], tpos + ForegroundOffsets[idx]);
                        }
                    }
                }
            }

            Action<Units> func = (Units team) =>
            {
                for (var i = 0; i < team.Count; ++i)
                {
                    var unit = TerrainXYToWorld(team.Positions[i]);
                    var onscreenUnit = unit - TileOffset;

                    var pos = MapToScreen(onscreenUnit + new Vector2(0.5f, 0.5f));
                    pos += new Vector2(-20, -80);
                    var hppos = pos + new Vector2(0, 30);

                    var format = new CanvasTextFormat();
                    var textLayout = new CanvasTextLayout(args.DrawingSession, team.Names[i], format, 100, 16);
                    var textgeo = CanvasGeometry.CreateText(textLayout);
                    //args.DrawingSession.DrawText(team.Names[i], pos, Colors.Black);
                    args.DrawingSession.DrawGeometry(textgeo, pos, Colors.Black);
                    args.DrawingSession.FillGeometry(textgeo, pos, Colors.LightBlue);
                    float pct = team.Healths[i] / (float)team.MaxHealths[i];
                    args.DrawingSession.FillRectangle(new Rect(hppos.X, hppos.Y, 100, 5), Colors.DarkRed);
                    args.DrawingSession.FillRectangle(new Rect(hppos.X, hppos.Y, pct * 100, 5), Colors.Green);
                }
            };
            func(TeamA);
            func(TeamB);
        }
    }
}
