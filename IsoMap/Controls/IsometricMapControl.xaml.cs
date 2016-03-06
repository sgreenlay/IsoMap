
//#define HORIZONTAL_HEXAGONS

using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Numerics;
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

        private enum Phase
        {
            Move,
            Shoot
        };

        private Phase ActivePhase;

        public IsometricMapControl()
        {
            InitializeComponent();

#if true
            TileShape = MapTileShape.Square;
            TileSize = new Size(150, 85);
#else
            TileShape = MapTileShape.Hexagon;
            TileSize = new Size(150, 125);
#endif

            TileOffset = new Vector2(0.0f, 0.0f);
            ScreenOffset = new Vector2(0.0f, 0.0f);
            
            MapCanvas.PointerMoved += OnPointerMoved;
            MapCanvas.PointerPressed += OnPointerPressed;

            CoreWindow.GetForCurrentThread().KeyDown += OnKeyDown;

            TeamA = new List<Vector2>();
            TeamA.Add(new Vector2(5.0f, 0.0f));
            TeamA.Add(new Vector2(6.0f, 0.0f));
            TeamA.Add(new Vector2(5.0f, 1.0f));
            TeamA.Add(new Vector2(5.0f, 2.0f));
            TeamA.Add(new Vector2(6.0f, 3.0f));

            TeamB = new List<Vector2>();
            TeamB.Add(new Vector2(9.0f, -1.0f));
            TeamB.Add(new Vector2(9.0f, 0.0f));
            TeamB.Add(new Vector2(10.0f, 1.0f));
            TeamB.Add(new Vector2(10.0f, -1.0f));

            ActiveTeam = Team.TeamA;
            ActivePhase = Phase.Move;
        }

        private async void LoadAssets()
        {
            TeamABitmap = await CanvasBitmap.LoadAsync(MapCanvas, "Rock.png");
            TeamBBitmap = await CanvasBitmap.LoadAsync(MapCanvas, "Heart.png");
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

        private void OnPointerPressed(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var currentPoint = e.GetCurrentPoint(MapCanvas);

            var selectedTile = ScreenToMap(
                new Vector2((float)currentPoint.Position.X,
                            (float)currentPoint.Position.Y));

            if (currentPoint.Properties.IsLeftButtonPressed)
            {
                SelectedTile = null;

                if (ActiveTeam == Team.TeamA && TeamA.Contains(selectedTile))
                {
                    SelectedTile = selectedTile;
                }
                else if (ActiveTeam == Team.TeamB && TeamB.Contains(selectedTile))
                {
                    SelectedTile = selectedTile;
                }
            }
            else if (currentPoint.Properties.IsRightButtonPressed)
            {
                if (ActivePhase == Phase.Move)
                {
                    if (SelectedTile != null && Vector2.Distance(SelectedTile.Value, selectedTile) <= 1.44f)
                    {
                        if (TeamA.Contains(SelectedTile.Value))
                        {
                            if (TeamB.Contains(selectedTile))
                            {
                                TeamB.Remove(selectedTile);
                            }

                            TeamA.Remove(SelectedTile.Value);
                            TeamA.Add(selectedTile);
                        }
                        else if (TeamB.Contains(SelectedTile.Value))
                        {
                            if (TeamA.Contains(selectedTile))
                            {
                                TeamA.Remove(selectedTile);
                            }

                            TeamB.Remove(SelectedTile.Value);
                            TeamB.Add(selectedTile);
                        }

                        ActivePhase = Phase.Shoot;

                        SelectedTile = selectedTile;
                    }
                }
                else if (ActivePhase == Phase.Shoot)
                {
                    if ((selectedTile.X == SelectedTile.Value.X) ||
                        (selectedTile.Y == SelectedTile.Value.Y))
                    {
                        if (TeamA.Contains(SelectedTile.Value))
                        {
                            if (TeamB.Contains(selectedTile))
                            {
                                TeamB.Remove(selectedTile);
                            }

                            ActiveTeam = Team.TeamB;
                        }
                        else if (TeamB.Contains(SelectedTile.Value))
                        {
                            if (TeamA.Contains(selectedTile))
                            {
                                TeamA.Remove(selectedTile);
                            }

                            ActiveTeam = Team.TeamA;
                        }

                        ActivePhase = Phase.Move;

                        SelectedTile = null;
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
                ScrollMap(new Vector2(0.0f, -1.0f));
            }
            else if (e.VirtualKey == Windows.System.VirtualKey.Down)
            {
                ScrollMap(new Vector2(0.0f, +1.0f));
            }
            else if (e.VirtualKey == Windows.System.VirtualKey.Left)
            {
                ScrollMap(new Vector2(-1.0f, 0.0f));
            }
            else if (e.VirtualKey == Windows.System.VirtualKey.Right)
            {
                ScrollMap(new Vector2(+1.0f, 0.0f));
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
#if HORIZONTAL_HEXAGONS
                if (Math.Floor(mapCoordinates.Y) % 2.0f == 0)
                {
                    screenCoordinates.X = (float)(mapCoordinates.X * 1.5f * TileSize.Width);
                }
                else
                {
                    screenCoordinates.X = (float)((mapCoordinates.X + 0.5f) * 1.5f * TileSize.Width);
                }
                
                screenCoordinates.Y = (float)(mapCoordinates.Y * TileSize.Height / 2.0f);
#else
                if (Math.Floor(mapCoordinates.Y) % 2.0f == 0)
                {
                    screenCoordinates.X = (float)(mapCoordinates.X * TileSize.Width);
                }
                else
                {
                    screenCoordinates.X = (float)((mapCoordinates.X + 0.5f) * TileSize.Width);
                }

                screenCoordinates.Y = (float)(mapCoordinates.Y * TileSize.Height * 0.75);
#endif
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
#if HORIZONTAL_HEXAGONS
                // TODO
#else
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
#endif
            }

            return onscreenTile + TileOffset;
        }

        void ScrollMap(Vector2 relativeScreenOffset)
        {
            Vector2 screenOffset = relativeScreenOffset + ScreenOffset;

            if (TileShape == MapTileShape.Square)
            {
                // TODO
            }
            else if (TileShape == MapTileShape.Hexagon)
            {
#if HORIZONTAL_HEXAGONS
                // TODO
#else
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
#endif
            }
        }

        void ScrollMapToCenterTile(Vector2 absoluteTile)
        {
            // TODO
        }

        void Redraw(CanvasControl canvas, CanvasDrawEventArgs args)
        {
            if (TeamABitmap == null)
            {
                LoadAssets();
            }

            for (int y = -2; y <= (int)Math.Ceiling(canvas.ActualHeight / TileSize.Height * 2) + 1; ++y)
            {
                for (int x = -2; x <= (int)Math.Ceiling(canvas.ActualWidth / TileSize.Width) + 1; ++x)
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

#if HORIZONTAL_HEXAGONS
                        path.BeginFigure(new Vector2(
                            topLeft.X + 0.25f * (float)TileSize.Width,
                            topLeft.Y));

                        path.AddLine(new Vector2(
                            bottomRight.X - 0.25f * (float)TileSize.Width,
                            topLeft.Y));
                        path.AddLine(new Vector2(
                            bottomRight.X,
                            topLeft.Y + 0.5f * (float)TileSize.Height));
                        path.AddLine(new Vector2(
                            bottomRight.X - 0.25f * (float)TileSize.Width,
                            bottomRight.Y));
                        path.AddLine(new Vector2(
                            topLeft.X + 0.25f * (float)TileSize.Width,
                            bottomRight.Y));
                        path.AddLine(new Vector2(
                            topLeft.X,
                            topLeft.Y + 0.5f * (float)TileSize.Height));
#else
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
#endif

                        path.EndFigure(CanvasFigureLoop.Closed);
                    }
                    
                    var tile = onscreenTile + TileOffset;
                    var geometry = CanvasGeometry.CreatePath(path);

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
                    else
                    {
                        args.DrawingSession.FillGeometry(geometry, Colors.White);
                        args.DrawingSession.DrawGeometry(geometry, Colors.Black);
                    }
                }
            }
            
            foreach (var unit in TeamA)
            {
                var pos = MapToScreen(unit + new Vector2(0.5f, 0.5f));
                pos = pos - TeamABitmap.Size.ToVector2() / 2.0f;

                pos.Y -= 40;

                args.DrawingSession.DrawImage(TeamABitmap, pos);
            }

            foreach (var unit in TeamB)
            {
                var pos = MapToScreen(unit + new Vector2(0.5f, 0.5f));
                pos = pos - TeamBBitmap.Size.ToVector2() / 2.0f;

                pos.Y -= 25;

                args.DrawingSession.DrawImage(TeamBBitmap, pos);
            }
        }
    }
}
