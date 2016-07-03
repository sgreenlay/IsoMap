
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

        private IntVector2 HighlightedTile { get; set; }
        private Index<Units> SelectedUnit { get; set; }

        private Vector2 ScreenOffset { get; set; }
        private Vector2 TileOffset { get; set; }

        private CanvasBitmap TreeTallBitmap;
        private CanvasBitmap TreeShortBitmap;
        private CanvasBitmap RockBitmap;
        private CanvasBitmap HeartBitmap;

        private CanvasBitmap NoVisionBitmap;
        private CanvasBitmap NoMovementBitmap;

        private Task LoadingAssetsTask;

        private Vector2 TerrainTopLeft = new Vector2(4.0f, -3.0f);

        public GameData gamedata;
        public BitArray MovableOverlay;
        public BitArray CanMovableOverlay;
        private CanvasBitmap[] ForegroundImages;
        private Vector2[] ForegroundOffsets;
        private PathFinder pathfinder;
        struct TeamGraphics
        {
            public TeamGraphics(CanvasBitmap b, int o)
            {
                bitmap = b;
                offset = o;
            }
            public CanvasBitmap bitmap;
            public int offset;

            public Vector2 offset2d
            {
                get
                {
                    Vector2 pos = -bitmap.Size.ToVector2() / 2.0f;
                    pos.Y += offset;
                    return pos;
                }
            }
        }
        private TeamGraphics[] teamgraphics = new TeamGraphics[2];

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

        public void Invalidate()
        {
            MapCanvas.Invalidate();
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

            gamedata = new GameData();
            MovableOverlay = new BitArray(gamedata.TerrainSize.Area());
            CanMovableOverlay = new BitArray(gamedata.TerrainSize.Area());
            ForegroundImages = new CanvasBitmap[gamedata.TerrainSize.Area()];
            ForegroundOffsets = new Vector2[gamedata.TerrainSize.Area()];
            pathfinder = new PathFinder(gamedata.TerrainSize);
            SelectedUnit = Index<Units>.Invalid;
        }

        private async Task LoadAssets()
        {
            teamgraphics[0] = new TeamGraphics(
                await CanvasBitmap.LoadAsync(MapCanvas, "Assets/Game/Character Boy.png"),
                -35);

            teamgraphics[1] = new TeamGraphics(
                await CanvasBitmap.LoadAsync(MapCanvas, "Assets/Game/Enemy Bug.png"),
                -35);

            TreeTallBitmap = await CanvasBitmap.LoadAsync(MapCanvas, "Assets/Game/Tree Tall.png");
            TreeShortBitmap = await CanvasBitmap.LoadAsync(MapCanvas, "Assets/Game/Tree Short.png");
            RockBitmap = await CanvasBitmap.LoadAsync(MapCanvas, "Assets/Game/Rock.png");
            HeartBitmap = await CanvasBitmap.LoadAsync(MapCanvas, "Assets/Game/Heart.png");

            NoVisionBitmap = await CanvasBitmap.LoadAsync(MapCanvas, "Assets/Game/no_vision.png");
            NoMovementBitmap = await CanvasBitmap.LoadAsync(MapCanvas, "Assets/Game/no_movement.png");

            Invalidate();
        }

        private void OnPointerMoved(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var currentPoint = e.GetCurrentPoint(MapCanvas);

            var old = HighlightedTile;
            HighlightedTile = WorldToTerrainXY(ScreenToMap(
                new Vector2((float)currentPoint.Position.X,
                            (float)currentPoint.Position.Y)));

            if (HighlightedTile != old)
            {
                pathfinder.Clear();
                var idx = gamedata.units.IndexOfPosition(HighlightedTile);
                if (idx != -1)
                {
                    gamedata.pathfindMove(idx, pathfinder);
                }
                pathfinder.CopyPathDataOut(CanMovableOverlay);

                Invalidate();
            }

            e.Handled = true;
        }

        private void ClearSelection()
        {
            SelectedUnit = new Index<Units>(-1);
            MovableOverlay.SetAll(false);
        }

        private void SetSelection(Index<Units> idx)
        {
            SelectedUnit = idx;
            Debug.Assert(idx != -1);
            if (gamedata.ActivePhase == GameData.Phase.Move)
            {
                pathfinder.Clear();
                gamedata.pathfindMove(idx, pathfinder);
                pathfinder.CopyPathDataOut(MovableOverlay);
            }
            else if (gamedata.ActivePhase == GameData.Phase.Shoot)
            {
                var Terrain = gamedata.Terrain;
                var TerrainSize = gamedata.TerrainSize;

                MovableOverlay.SetAll(false);
                gamedata.pathfindShoot(idx, MovableOverlay);
            }
        }

        private void OnPointerPressed(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var currentPoint = e.GetCurrentPoint(MapCanvas);

            var selectedTile = ScreenToMap(
                new Vector2((float)currentPoint.Position.X,
                            (float)currentPoint.Position.Y));
            var selectedXY = WorldToTerrainXY(selectedTile);

            if (currentPoint.Properties.IsLeftButtonPressed || currentPoint.Properties.IsRightButtonPressed)
            {
                if (gamedata.ActivePhase == GameData.Phase.Move)
                {
                    if (SelectedUnit != -1 && gamedata.TerrainSize.ValidXY(selectedXY) && MovableOverlay.Get(gamedata.TerrainSize.XYToIndex(selectedXY)))
                    {
                        var tgt = gamedata.units.IndexOfPosition(selectedXY);
                        if (tgt != -1)
                            gamedata.Kill(tgt);

                        gamedata.Move(SelectedUnit, selectedXY);

                        gamedata.ActivePhase = GameData.Phase.Shoot;

                        SetSelection(SelectedUnit);
                    }
                    else
                    {
                        ClearSelection();

                        var idx = gamedata.units.IndexOfPosition(selectedXY);
                        if (idx != -1 && gamedata.units.Allegiance[idx] == gamedata.CurrentTeam())
                        {
                            SetSelection(idx);
                        }
                    }
                }
                else if (gamedata.ActivePhase == GameData.Phase.Shoot)
                {
                    Debug.Assert(SelectedUnit != -1);
                    if (gamedata.TerrainSize.ValidXY(selectedXY) && MovableOverlay.Get(gamedata.TerrainSize.XYToIndex(selectedXY)))
                    {
                        ClearSelection();

                        gamedata.PlayerShoot(selectedXY);
                    }
                }
            }

            Invalidate();

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

            Invalidate();

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

            for (int i = 0; i < ForegroundImages.Length; ++i)
            {
                ForegroundImages[i] = null;
            }

            for (var i = new Index<Units>(0); i < gamedata.units.Count; ++i)
            {
                if (gamedata.units.Allegiance[i] == null)
                    continue;

                var idx = gamedata.TerrainSize.XYToIndex(gamedata.units.Positions[i]);
                Debug.Assert(ForegroundImages[idx] == null);
                if (gamedata.units.Allegiance[i] == gamedata.TeamA)
                {
                    ForegroundImages[idx] = teamgraphics[0].bitmap;
                    ForegroundOffsets[idx] = teamgraphics[0].offset2d;
                }
                else if (gamedata.units.Allegiance[i] == gamedata.TeamB)
                {
                    ForegroundImages[idx] = teamgraphics[1].bitmap;
                    ForegroundOffsets[idx] = teamgraphics[1].offset2d;
                }
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
                    var terrainxyidx = gamedata.TerrainSize.XYToIndex(terrainxy);
                    var SelectedTile = SelectedUnit != -1 ? new IntVector2?(gamedata.units.Positions[SelectedUnit]) : null;

                    if (terrainxy == HighlightedTile && HighlightedTile == SelectedTile)
                    {
                        args.DrawingSession.FillGeometry(geometry, Colors.Red);
                        args.DrawingSession.DrawGeometry(geometry, Color.FromArgb(255, 40, 40, 40));
                    }
                    else if (WorldToTerrainXY(tile) == SelectedTile)
                    {
                        args.DrawingSession.FillGeometry(geometry, Colors.DarkRed);
                        args.DrawingSession.DrawGeometry(geometry, Color.FromArgb(255, 40, 40, 40));
                    }
                    else if (terrainxy == HighlightedTile)
                    {
                        args.DrawingSession.FillGeometry(geometry, Colors.CornflowerBlue);
                        args.DrawingSession.DrawGeometry(geometry, Color.FromArgb(255, 40, 40, 40));
                    }
                    else if (!gamedata.TerrainSize.ValidXY(terrainxy))
                    {
                        args.DrawingSession.FillGeometry(geometry, Colors.DarkSlateGray);
                    }
                    else if (SelectedTile != null && MovableOverlay.Get(terrainxyidx))
                    {
                        args.DrawingSession.FillGeometry(geometry, Colors.GreenYellow);
                    }
                    else if (CanMovableOverlay.Get(terrainxyidx))
                    {
                        args.DrawingSession.FillGeometry(geometry, Colors.LightSeaGreen);
                    }
                    else
                    {
                        args.DrawingSession.FillGeometry(geometry, Colors.LightGreen);
                    }

                    args.DrawingSession.DrawGeometry(geometry, Colors.Black);

                    if (gamedata.TerrainSize.ValidXY(terrainxy))
                    {
                        var pos = MapToScreen(onscreenTile + new Vector2(0.5f, 0.5f));
                        switch (gamedata.Terrain[terrainxyidx])
                        {
                            case GameData.TerrainType.Empty:
                                break;
                            case GameData.TerrainType.Soft:
                                args.DrawingSession.DrawImage(TreeShortBitmap, pos + new Vector2(-50f, -140f));
                                break;
                            case GameData.TerrainType.Solid:
                                args.DrawingSession.DrawImage(RockBitmap, pos + new Vector2(-50f, -140f));
                                break;
                            case GameData.TerrainType.Transparent:
                                args.DrawingSession.DrawImage(TreeTallBitmap, pos + new Vector2(-50f, -140f));
                                break;
                        }
                        if (ForegroundImages[terrainxyidx] != null)
                        {
                            args.DrawingSession.DrawImage(ForegroundImages[terrainxyidx], pos + ForegroundOffsets[terrainxyidx]);
                        }
                    }
                }
            }

            for (var i = new Index<Units>(0); i < gamedata.units.Count; ++i)
            {
                if (!gamedata.units.IsValid(i))
                    continue;
                var unit = TerrainXYToWorld(gamedata.units.Positions[i]);
                var onscreenUnit = unit - TileOffset;

                var pos = MapToScreen(onscreenUnit + new Vector2(0.5f, 0.5f));
                pos += new Vector2(-20, -80);
                var hppos = pos + new Vector2(0, 30);

                var format = new CanvasTextFormat();
                var textLayout = new CanvasTextLayout(args.DrawingSession, gamedata.units.Names[i], format, 100, 16);
                var textgeo = CanvasGeometry.CreateText(textLayout);
                //args.DrawingSession.DrawText(team.Names[i], pos, Colors.Black);
                args.DrawingSession.DrawGeometry(textgeo, pos, Colors.Black);
                args.DrawingSession.FillGeometry(textgeo, pos, Colors.LightBlue);
                float pct = gamedata.units.Healths[i] / (float)gamedata.units.MaxHealths[i];
                args.DrawingSession.FillRectangle(new Rect(hppos.X, hppos.Y, 100, 5), Colors.DarkRed);
                args.DrawingSession.FillRectangle(new Rect(hppos.X, hppos.Y, pct * 100, 5), Colors.Green);
            }

            if (gamedata.TerrainSize.ValidXY(HighlightedTile))
            {
                var pos = MapToScreen(TerrainXYToWorld(HighlightedTile) - TileOffset + new Vector2(0.5f, 0.5f));
                pos += new Vector2(-50f, -140f);
                var terrtype = gamedata.Terrain[gamedata.TerrainSize.XYToIndex(HighlightedTile)];

                switch (terrtype)
                {
                    case GameData.TerrainType.Empty:
                        break;
                    case GameData.TerrainType.Soft:
                        args.DrawingSession.DrawImage(NoVisionBitmap, pos + new Vector2(40f, 30f));
                        break;
                    case GameData.TerrainType.Solid:
                        args.DrawingSession.DrawImage(NoVisionBitmap, pos + new Vector2(40f, 30f));
                        args.DrawingSession.DrawImage(NoMovementBitmap, pos + new Vector2(40f, 62f));
                        break;
                    case GameData.TerrainType.Transparent:
                        args.DrawingSession.DrawImage(NoMovementBitmap, pos + new Vector2(40f, 30f));
                        break;
                }
            }
        }
    }
}
