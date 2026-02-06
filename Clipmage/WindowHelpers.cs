using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Clipmage
{
    [Flags]
    public enum RoundedCorners
    {
        None = 0x00,
        TopLeft = 0x02,
        TopRight = 0x04,
        BottomLeft = 0x08,
        BottomRight = 0x10,
        All = 0x1F
    }

    internal class WindowHelpers
    {
        private static Dictionary<Control, PaintEventHandler> _paintHandlers = new Dictionary<Control, PaintEventHandler>();

        public static void ApplyRoundedRegion(Control control, int radius, int borderWidth = 0, Color borderColor = default)
        {
            if (control == null || control.Width <= 0 || control.Height <= 0) return;

            // 1. Remove existing handler
            if (_paintHandlers.ContainsKey(control))
            {
                control.Paint -= _paintHandlers[control];
                _paintHandlers.Remove(control);
            }

            // 2. Apply Region (Clipping)
            // We use the full bounds for the region
            Rectangle bounds = new Rectangle(0, 0, control.Width, control.Height);
            using (GraphicsPath path = GetRoundedPath(bounds, radius, RoundedCorners.All))
            {
                control.Region = new Region(path);
            }

            // 3. Setup Border Drawing
            if (borderWidth >= 0)
            {
                PaintEventHandler handler = (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

                    if (borderWidth > 0)
                    {
                        Color paintColor = borderColor;

                        // Dynamic Color Logic
                        if (paintColor == Color.Empty)
                        {
                            if (control is Button btn && btn.FlatStyle == FlatStyle.Flat)
                            {
                                paintColor = btn.FlatAppearance.BorderColor;
                            }
                            if (paintColor == Color.Empty) paintColor = Color.Purple;
                        }

                        // FIX: Adjust the rect to be slightly smaller than the region.
                        // The Region clips strictly at '0' and 'Width'. 
                        // If we draw the border exactly at the edge, the anti-aliasing gets chopped off.
                        // We add a '0.5f' buffer to pull the border inside the jagged edge.
                        float halfBorder = borderWidth / 2.0f;
                        float pixelOffset = 0.5f; // Pulls the border inside to save the smooth edges

                        RectangleF borderRect = new RectangleF(
                            halfBorder + pixelOffset,
                            halfBorder + pixelOffset,
                            control.Width - borderWidth - (pixelOffset * 2),
                            control.Height - borderWidth - (pixelOffset * 2)
                        );

                        // Radius must be adjusted for the inset
                        int adjustedRadius = Math.Max(1, radius - (int)(halfBorder + pixelOffset));

                        using (GraphicsPath borderPath = GetRoundedPath(borderRect, adjustedRadius, RoundedCorners.All))
                        using (Pen pen = new Pen(paintColor, borderWidth))
                        {
                            pen.Alignment = PenAlignment.Center;
                            // Optional: Rounds the caps to prevent square artifacts at low angles
                            pen.StartCap = LineCap.Round;
                            pen.EndCap = LineCap.Round;

                            e.Graphics.DrawPath(pen, borderPath);
                        }
                    }
                };

                control.Paint += handler;
                _paintHandlers[control] = handler;
                control.Invalidate();
            }
        }

        public static GraphicsPath GetRoundedPath(RectangleF rect, float radius, RoundedCorners corners)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = radius * 2;

            // Top Left
            if ((corners & RoundedCorners.TopLeft) == RoundedCorners.TopLeft)
                path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            else
                path.AddLine(rect.X, rect.Y, rect.X, rect.Y);

            // Top Right
            if ((corners & RoundedCorners.TopRight) == RoundedCorners.TopRight)
                path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            else
                path.AddLine(rect.Right, rect.Y, rect.Right, rect.Y);

            // Bottom Right
            if ((corners & RoundedCorners.BottomRight) == RoundedCorners.BottomRight)
                path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            else
                path.AddLine(rect.Right, rect.Bottom, rect.Right, rect.Bottom);

            // Bottom Left
            if ((corners & RoundedCorners.BottomLeft) == RoundedCorners.BottomLeft)
                path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            else
                path.AddLine(rect.X, rect.Bottom, rect.X, rect.Bottom);

            path.CloseFigure();
            return path;
        }

        // Kept as requested, though unused in the optimized version
        public static Region BitmapToRegion(Bitmap bitmap)
        {
            GraphicsPath path = new GraphicsPath();
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (bitmap.GetPixel(x, y).A > 0)
                    {
                        path.AddRectangle(new Rectangle(x, y, 1, 1));
                    }
                }
            }
            return new Region(path);
        }
    }
}