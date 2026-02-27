using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
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

        public static void ApplyCroppedRegion(Control control, Point vector1, Point vector2)
        {
            if (control == null || control.Width <= 0 || control.Height <= 0) return;

            // 1. Remove existing handler
            if (_paintHandlers.ContainsKey(control))
            {
                control.Paint -= _paintHandlers[control];
                _paintHandlers.Remove(control);
            }

            // 2. Apply Region (Clipping)
            Point size = new Point(vector2.X - vector1.X, vector2.Y - vector1.Y);
            if (size.X < 0 || size.Y < 0) return;

            Rectangle bounds = new Rectangle(vector1.X, vector1.Y, size.X, size.Y);
            control.Region = new Region(bounds);

            PaintEventHandler handler = (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            };

            control.Paint += handler;
            _paintHandlers[control] = handler;
            control.Invalidate();
        }

        public static Icon GetIconFromPath(string path)
        {
            // 1. Get the index of the icon for this specific path
            SHFILEINFO shinfo = new SHFILEINFO();
            SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_SYSICONINDEX);

            // 2. Get the System Image List (Jumbo size = 0x4)
            Guid iidImageList = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
            IImageList imgList = null;
            int hResult = SHGetImageList(SHIL_JUMBO, ref iidImageList, out imgList);

            // Safety check: if Jumbo fails, it usually returns null or non-zero hResult
            if (hResult != 0 || imgList == null) return null;

            // 3. Extract the Icon using the index found in step 1
            IntPtr hIcon = IntPtr.Zero;
            imgList.GetIcon(shinfo.iIcon, ILD_TRANSPARENT, out hIcon);

            if (hIcon == IntPtr.Zero) return null;

            // 4. Clone to managed Icon and clean up native handle
            Icon myIcon = (Icon)Icon.FromHandle(hIcon).Clone();
            DestroyIcon(hIcon);

            return myIcon;
        }

        public static Icon ResizeIcon(Icon originalIcon, int width, int height)
        {


            using (Bitmap originalBitmap = originalIcon.ToBitmap())
            {
                // 3. Create a new Bitmap with your desired dimensions
                Bitmap resizedBitmap = new Bitmap(width, height);

                using (Graphics g = Graphics.FromImage(resizedBitmap))
                {
                    // 4. Set High Quality settings for best upscaling results
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    // 5. Draw the original image resized
                    g.DrawImage(originalBitmap, 0, 0, width, height);
                }

                return Icon.FromHandle(resizedBitmap.GetHicon());
            }
        }




        // --- Minimal P/Invoke Definitions ---

        private const uint SHGFI_SYSICONINDEX = 0x4000;
        private const int SHIL_JUMBO = 0x4;
        private const int ILD_TRANSPARENT = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

        [DllImport("shell32.dll", EntryPoint = "#727")] // Ordinal for SHGetImageList
        private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [ComImport]
        [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")] // IID_IImageList
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IImageList
        {
            // We only need GetIcon, but we must define the placeholders for the 
            // preceding methods in the vtable so the interface maps correctly.
            // We use IntPtr for unused args to avoid defining extra structs.
            [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
            [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, ref int pi);
            [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
            [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
            [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
            [PreserveSig] int Draw(IntPtr pimldp);
            [PreserveSig] int Remove(int i);
            [PreserveSig] int GetIcon(int i, int flags, out IntPtr picon);
        }


        public static void GetDetailsFromPath(string path, out string displayName, out string typeName, out string fileSize, out string dateModified)
        {
            SHFILEINFO shinfo = new SHFILEINFO();

            // Correct Constants:
            const uint SHGFI_TYPENAME = 0x000000400;
            const uint SHGFI_DISPLAYNAME = 0x000000200;

            // Request DisplayName and TypeName
            SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_TYPENAME | SHGFI_DISPLAYNAME);

            // Set defaults from Shell Info
            displayName = shinfo.szDisplayName;
            typeName = shinfo.szTypeName;

            try
            {
                // 1. Check Attributes to see if it's a File or Directory
                FileAttributes attr = System.IO.File.GetAttributes(path);

                if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                {
                    // --- IT IS A DIRECTORY ---
                    // Directories don't have a simple "Length". 
                    // We usually display nothing or "<DIR>" to avoid freezing the UI with calculation.
                    fileSize = "";

                    // Get Directory Date
                    dateModified = System.IO.Directory.GetLastWriteTime(path).ToString("g");
                }
                else
                {
                    // --- IT IS A FILE ---
                    FileInfo fi = new System.IO.FileInfo(path);
                    fileSize = FormatFileSize(fi.Length);
                    dateModified = fi.LastWriteTime.ToString("g");
                }
            }
            catch
            {
                // Fallback if permission denied or path vanished
                fileSize = "Unknown";
                dateModified = DateTime.Now.ToString("g");
            }
        }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}