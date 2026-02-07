using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using static Clipmage.WindowHelpers;
using static Clipmage.AppConfig;

namespace Clipmage
{


    /// <summary>
    /// A high-performance image container with rounded corners.
    /// Replaces standard PictureBox for Shelf items and BlackWindow display.
    /// </summary>
    public class RoundedImageBox : PictureBox
    {
        private int _radius = 8;
        private int _borderWidth = 0;
        private Color _borderColor = Color.Empty;

        public RoundedImageBox()
        {
            this.DoubleBuffered = true;
            this.BackColor = Color.Transparent;
            this.SizeMode = PictureBoxSizeMode.Zoom;
            this.Margin = new Padding(5);
        }

        public int Radius
        {
            get => _radius;
            set { _radius = value; Invalidate(); }
        }

        public int BorderWidth
        {
            get => _borderWidth;
            set { _borderWidth = value; Invalidate(); }
        }

        public Color BorderColor
        {
            get => _borderColor;
            set { _borderColor = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            pe.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            pe.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // 1. Calculate Geometry (Same logic as Button for consistency)
            float borderSize = _borderWidth > 0 ? _borderWidth : 0;
            // If no border, we draw to the edge. If border, we inset.
            float inset = borderSize > 0 ? borderSize / 2f : 0;

            RectangleF pathRect = new RectangleF(
                inset, inset,
                this.Width - (inset * 2),
                this.Height - (inset * 2)
            );

            float pathRadius = Math.Max(0.1f, _radius - inset);

            using (GraphicsPath path = WindowHelpers.GetRoundedPath(pathRect, pathRadius, RoundedCorners.All))
            {
                // 2. Draw Background (Letterbox filler)
                // Only draw if we have an image, otherwise transparent
                using (Brush backBrush = new SolidBrush(Color.Transparent))
                {
                    pe.Graphics.FillPath(backBrush, path);
                }

                // 3. Draw Image
                if (this.Image != null)
                {
                    // Calculate Zoom Rectangle
                    RectangleF imgRect = GetImageRectangle(this.ClientRectangle, this.Image);

                    // We must clip the image to the rounded path
                    pe.Graphics.SetClip(path);
                    pe.Graphics.DrawImage(this.Image, imgRect);
                    pe.Graphics.ResetClip();
                }

                // 4. Draw Border
                if (_borderWidth > 0 && _borderColor != Color.Empty)
                {
                    using (Pen pen = new Pen(_borderColor, _borderWidth))
                    {
                        pen.Alignment = PenAlignment.Center;
                        pe.Graphics.DrawPath(pen, path);
                    }
                }
            }
        }

        private RectangleF GetImageRectangle(Rectangle clientRect, Image image)
        {
            if (image == null) return RectangleF.Empty;

            float ratioX = (float)clientRect.Width / image.Width;
            float ratioY = (float)clientRect.Height / image.Height;
            float ratio = Math.Min(ratioX, ratioY);

            float w = image.Width * ratio;
            float h = image.Height * ratio;
            float x = clientRect.X + (clientRect.Width - w) / 2;
            float y = clientRect.Y + (clientRect.Height - h) / 2;

            return new RectangleF(x, y, w, h);
        }
    }

    public class GhostTextBox : TextBox
    {
        private const int WM_NCHITTEST = 0x84;
        private const int HTTRANSPARENT = -1;

        protected override void WndProc(ref Message m)
        {
            // If we are in "Read Only" mode, tell Windows that the mouse 
            // is actually over the window BENEATH us (HTTRANSPARENT).
            if (m.Msg == WM_NCHITTEST && this.ReadOnly)
            {
                m.Result = (IntPtr)HTTRANSPARENT;
                return;
            }

            base.WndProc(ref m);
        }
    }
}