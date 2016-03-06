
//#define USE_HEXAGONAL_TILES

using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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

        private List<Vector2> TeamA;
        private CanvasBitmap TeamABitmap { get; set; }

        private List<Vector2> TeamB;
        private CanvasBitmap TeamBBitmap { get; set; }

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
        private List<TerrainType> Terrain;
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
                }
            }

            TeamA = new List<Vector2>();
            for (var x = 0; x < 5; ++x)
            {
                var tpos = new IntVector2(Rand.Next(TerrainSize.X), Rand.Next(TerrainSize.Y));
                if (Terrain[TerrainXYToIndex(tpos)] != TerrainType.Empty)
                    continue;
                var pos = TerrainXYToWorld(tpos);
                if (TeamA.Contains(pos))
                    continue;
                TeamA.Add(pos);
            }

            TeamB = new List<Vector2>();
            for (var x = 0; x < 5; ++x)
            {
                var tpos = new IntVector2(Rand.Next(TerrainSize.X), Rand.Next(TerrainSize.Y));
                if (Terrain[TerrainXYToIndex(tpos)] != TerrainType.Empty)
                    continue;
                var pos = TerrainXYToWorld(tpos);
                if (TeamA.Contains(pos))
                    continue;
                if (TeamB.Contains(pos))
                    continue;
                TeamB.Add(pos);
            }

            ActiveTeam = Team.TeamA;
            ActivePhase = Phase.Move;
        }

        private async Task LoadAssets()
        {
            TeamABitmap = await CanvasBitmap.LoadAsync(MapCanvas, "Assets/Game/Rock.png");
            Debug.Assert(TeamABitmap != null);

            TeamBBitmap = await CanvasBitmap.LoadAsync(MapCanvas, "Assets/Game/Heart.png");
            Debug.Assert(TeamBBitmap != null);
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
        private List<Vector2> CurrentTeam()
        {
            if (ActiveTeam == Team.TeamA) return TeamA;
            else return TeamB;
        }
        private List<Vector2> EnemyTeam()
        {
            if (ActiveTeam == Team.TeamA) return TeamB;
            else return TeamA;
        }

        private void SetSelection(Vector2 sel)
        {
            SelectedTile = sel;
            MovableOverlay.SetAll(false);
            if (ActivePhase == Phase.Move)
            {
                var tpos = WorldToTerrainXY(sel);
                for (var y = -1; y < 2; ++y)
                {
                    for (var x = -1; x < 2; ++x)
                    {
                        var tpos2 = tpos + new IntVector2(x, y);
                        if (!ValidTerrainXY(tpos2))
                            continue;
                        var ttype = Terrain[TerrainXYToIndex(tpos2)];
                        if (ttype == TerrainType.Solid || ttype == TerrainType.Transparent)
                            continue;
                        var wpos2 = TerrainXYToWorld(tpos2);
                        if (CurrentTeam().Contains(wpos2))
                            continue;
                        MovableOverlay.Set(TerrainXYToIndex(tpos2), true);
                    }
                }
            }
            else if (ActivePhase == Phase.Shoot)
            {
                var tpos = WorldToTerrainXY(sel);
                for (var x = tpos.X + 1; x < TerrainSize.X; ++x)
                {
                    var idx = TerrainXYToIndex(x, tpos.Y);
                    var ttype = Terrain[idx];
                    if (ttype == TerrainType.Solid)
                        break;
                    if (CurrentTeam().Contains(TerrainXYToWorld(x, tpos.Y)))
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
                    if (CurrentTeam().Contains(TerrainXYToWorld(x, tpos.Y)))
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
                    if (CurrentTeam().Contains(TerrainXYToWorld(tpos.X, y)))
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
                    if (CurrentTeam().Contains(TerrainXYToWorld(tpos.X, y)))
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

            if (currentPoint.Properties.IsLeftButtonPressed)
            {
                if (ActivePhase == Phase.Move)
                {
                    ClearSelection();

                    if (CurrentTeam().Contains(selectedTile))
                    {
                        SetSelection(selectedTile);
                    }
                }
            }
            else if (currentPoint.Properties.IsRightButtonPressed)
            {
                if (ActivePhase == Phase.Move)
                {
                    var terxy = WorldToTerrainXY(selectedTile);
                    if (SelectedTile != null && ValidTerrainXY(terxy) && MovableOverlay.Get(TerrainXYToIndex(terxy)))
                    {
                        if (EnemyTeam().Contains(selectedTile))
                            EnemyTeam().Remove(selectedTile);

                        CurrentTeam().Remove(SelectedTile.Value);
                        CurrentTeam().Add(selectedTile);

                        ActivePhase = Phase.Shoot;

                        SetSelection(selectedTile);
                    }
                }
                else if (ActivePhase == Phase.Shoot)
                {
                    Debug.Assert(SelectedTile != null);
                    var terxy = WorldToTerrainXY(selectedTile);
                    if (ValidTerrainXY(terxy) && MovableOverlay.Get(TerrainXYToIndex(terxy)))
                    {
                        if (EnemyTeam().Contains(selectedTile))
                            EnemyTeam().Remove(selectedTile);

                        if (ActiveTeam == Team.TeamA)
                            ActiveTeam = Team.TeamB;
                        else
                            ActiveTeam = Team.TeamA;

                        ActivePhase = Phase.Move;

                        ClearSelection();
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

        void Redraw(CanvasControl canvas, CanvasDrawEventArgs args)
        {
            if (LoadingAssetsTask == null) LoadingAssetsTask = LoadAssets();

            if (!LoadingAssetsTask.IsCompleted) return;
            if (LoadingAssetsTask.IsFaulted)
            {
                throw LoadingAssetsTask.Exception;
            }

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
                        switch (Terrain[TerrainXYToIndex(terrainxy)])
                        {
                            case TerrainType.Empty:
                                args.DrawingSession.FillGeometry(geometry, Colors.Khaki);
                                break;
                            case TerrainType.Soft:
                                args.DrawingSession.FillGeometry(geometry, Colors.WhiteSmoke);
                                break;
                            case TerrainType.Solid:
                                args.DrawingSession.FillGeometry(geometry, Colors.Gray);
                                break;
                            case TerrainType.Transparent:
                                args.DrawingSession.FillGeometry(geometry, Colors.Blue);
                                break;
                        }
                        args.DrawingSession.DrawGeometry(geometry, Colors.Black);
                    }
                }
            }
            
            foreach (var unit in TeamA)
            {
                var onscreenUnit = unit - TileOffset;

                var pos = MapToScreen(onscreenUnit + new Vector2(0.5f, 0.5f));
                pos = pos - TeamABitmap.Size.ToVector2() / 2.0f;

                pos.Y -= 40;

                args.DrawingSession.DrawImage(TeamABitmap, pos);
            }

            foreach (var unit in TeamB)
            {
                var onscreenUnit = unit - TileOffset;

                var pos = MapToScreen(onscreenUnit + new Vector2(0.5f, 0.5f));
                pos = pos - TeamBBitmap.Size.ToVector2() / 2.0f;

                pos.Y -= 25;

                args.DrawingSession.DrawImage(TeamBBitmap, pos);
            }
        }
    }
}
