using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Clipmage
{
    // Enum for specifying which corners to round
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
        // Helper to generate the path directly 
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
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    // Use a threshold to grab even partially transparent pixels (from AA)
                    // This ensures the region is slightly larger/smoother at the pixel level
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