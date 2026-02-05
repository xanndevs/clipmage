using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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

            // 1. Remove existing handler to prevent stacking
            if (_paintHandlers.ContainsKey(control))
            {
                control.Paint -= _paintHandlers[control];
                _paintHandlers.Remove(control);
            }

            // 2. Apply Region (Clipping)
            using (GraphicsPath path = GetRoundedPath(new Rectangle(0, 0, control.Width, control.Height), radius, RoundedCorners.All))
            using (Bitmap mask = new Bitmap(control.Width, control.Height))
            using (Graphics g = Graphics.FromImage(mask))
            {
                // High quality settings for the mask generation
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                
                using (Brush b = new SolidBrush(Color.Black))
                {
                    g.FillPath(b, path);
                }
                control.Region = BitmapToRegion(mask);
            }

            // 3. Setup Border Drawing
            if (borderWidth > 0)
            {
                PaintEventHandler handler = (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

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

                    // Define the rect for the border
                    // We inset by half the border width so the Pen stroke stays inside the region
                    float halfBorder = borderWidth / 2.0f;
                    RectangleF borderRect = new RectangleF(
                        halfBorder, 
                        halfBorder, 
                        control.Width - borderWidth, 
                        control.Height - borderWidth
                    );

                    // Radius must be adjusted for the inset
                    int adjustedRadius = Math.Max(1, radius - (int)halfBorder);

                    using (GraphicsPath borderPath = GetRoundedPath(borderRect, adjustedRadius, RoundedCorners.All))
                    using (Pen pen = new Pen(paintColor, borderWidth))
                    {
                        pen.Alignment = PenAlignment.Center;
                        e.Graphics.DrawPath(pen, borderPath);
                    }
                };

                control.Paint += handler;
                _paintHandlers[control] = handler;
                control.Invalidate();
            }
        }

        // Helper to generate the path directly (Replaces DrawRoundedShape for better precision)
        public static GraphicsPath GetRoundedPath(RectangleF rect, float radius, RoundedCorners corners)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = radius * 2;
            
            // Top Left
            if ((corners & RoundedCorners.TopLeft) == RoundedCorners.TopLeft)
                path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            else
                path.AddLine(rect.X, rect.Y, rect.X, rect.Y); // Placeholder line to keep path connected correctly

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

        public static Region BitmapToRegion(Bitmap bitmap)
        {
            GraphicsPath path = new GraphicsPath();
            // Scan for non-transparent pixels to create the region
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    // Threshold > 0 catches the anti-aliased feathering, making the click region mostly accurate
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