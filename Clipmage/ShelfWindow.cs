using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices; // Required for DllImport
using static Clipmage.WindowHelpers;
using static Clipmage.AppConfig;
using System.Reflection;

namespace Clipmage
{
    public class ShelfWindow : Form
    {
        // Container structure for custom scrolling
        private Panel _viewPort;
        private FlowLayoutPanel _flowPanel;
        private ModernScrollBar _scrollBar;

        // System Tray
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;

        // Animation State
        private System.Windows.Forms.Timer _layoutTimer;
        private Dictionary<Control, Size> _targetSizes = new Dictionary<Control, Size>();

        // --- DWM Imports for Dark Title Bar ---
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public ShelfWindow()
        {
            InitializeComponent();

            // Apply Dark Title Bar
            ApplyDarkTitleBar();

            // Setup System Tray
            SetupSystemTray();
        }

        private void ApplyDarkTitleBar()
        {
            int useDarkMode = 1;
            // Try the modern attribute (Windows 11 / Windows 10 20H1+)
            if (DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int)) != 0)
            {
                // Fallback for older Windows 10 builds
                DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));
            }
        }

        private void SetupSystemTray()
        {
            _trayIcon = new NotifyIcon();
            _trayIcon.Text = "Clipmage Shelf";
            // Use the application icon or a default system icon
            _trayIcon.Icon = Properties.Resources.AppIcon;
            _trayIcon.Visible = true;

            // Left click to open/restore
            _trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    WindowController.ToggleShelf();
                }
            };

            // Context menu to actually exit the application
            _trayMenu = new ContextMenuStrip();

            ToolStripMenuItem versionNumber = new ToolStripMenuItem("Version - v" + Updater.currentVersion.ToString().Substring(0, Updater.currentVersion.ToString().Length - 2));
            versionNumber.Click += (s, e) =>
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Properties.Resources.GithubURL,
                    UseShellExecute = true
                }); 
                    
            };
            _trayMenu.Items.Add(versionNumber);

            ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit Clipmage");
            exitItem.Click += (s, e) =>
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                Application.Exit(); // Kill the app
            };
            _trayMenu.Items.Add(exitItem);



            _trayIcon.ContextMenuStrip = _trayMenu;
        }

        // Override Close to minimize to tray instead
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true; // Don't close the form
                this.Hide();     // Hide it instead
            }
            base.OnFormClosing(e);
        }

        private void InitializeComponent()
        {
            this.Text = "Clipmage Shelf";
            this.ShowInTaskbar = false;
            this.Icon = Properties.Resources.AppIcon;
            this.ShowIcon = true;
            this.Size = new Size(290, 400);
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(Screen.PrimaryScreen.WorkingArea.Right - this.Width, Screen.PrimaryScreen.WorkingArea.Bottom - this.Height);
            this.BackColor = Color.FromArgb(20, 20, 20);
            this.ForeColor = Color.White;
            this.TopMost = true;
            this.AllowDrop = true;
            this.FormBorderStyle = FormBorderStyle.SizableToolWindow;

            // Set minimum size based on image height config to prevent weird layout issues
            this.MinimumSize = new Size(SHELF_IMAGE_HEIGHT + 60, SHELF_IMAGE_HEIGHT + 60);

            // 1. ViewPort: The visible window area (clips content)
            _viewPort = new Panel();
            _viewPort.Dock = DockStyle.Fill;
            _viewPort.BackColor = Color.Transparent;
            // Forward mouse wheel from empty space to scrollbar
            _viewPort.MouseWheel += (s, e) => _scrollBar.DoScroll(e.Delta);

            // 2. FlowPanel: The actual content (grows in height)
            _flowPanel = new FlowLayoutPanel();
            _flowPanel.AutoSize = true;
            _flowPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _flowPanel.WrapContents = true;
            _flowPanel.Padding = new Padding(8);
            _flowPanel.BackColor = Color.Transparent;
            // Disable native scrolling to prevent ugly bars
            _flowPanel.AutoScroll = false;
            // Constrain width to ViewPort so it wraps correctly
            _flowPanel.MaximumSize = new Size(this.ClientSize.Width, 0);

            // Forward mouse wheel from inside content to scrollbar
            _flowPanel.MouseWheel += (s, e) => _scrollBar.DoScroll(e.Delta);

            // 3. Modern ScrollBar: Overlay control
            _scrollBar = new ModernScrollBar();
            // Use Dock.Right but inside the viewport it might push content? 
            // We use Anchor to make it an overlay.
            _scrollBar.Size = new Size(12, _viewPort.Height);
            _scrollBar.Location = new Point(_viewPort.Width - 12, 0);
            _scrollBar.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
            _scrollBar.Visible = false; // Hidden until needed
            _scrollBar.Scroll += (s, e) =>
            {
                // Move the content panel up/down
                _flowPanel.Top = -_scrollBar.Value;
            };

            // 4. Layout Animation Timer
            _layoutTimer = new System.Windows.Forms.Timer();
            _layoutTimer.Interval = WINDOW_REFRESH_INTERVAL;
            _layoutTimer.Tick += OnLayoutTick;

            // Compose controls
            _viewPort.Controls.Add(_flowPanel);
            _viewPort.Controls.Add(_scrollBar);

            // CRITICAL: Ensure ScrollBar is visually on TOP of the FlowPanel
            _scrollBar.BringToFront();

            this.Controls.Add(_viewPort);

            // Events
            this.DragEnter += Shelf_DragEnter;
            this.DragDrop += Shelf_DragDrop;

            // Layout Logic
            this.Resize += (s, e) => UpdateLayout();
            _flowPanel.SizeChanged += (s, e) => UpdateScrollParams();
            _viewPort.SizeChanged += (s, e) => UpdateScrollParams();
        }

        private void UpdateLayout()
        {
            // Force FlowPanel to match Viewport width exactly. 
            // This ensures WrapContents calculates lines based on the visible area.
            _flowPanel.MaximumSize = new Size(_viewPort.Width, 0);
            _flowPanel.Width = _viewPort.Width;

            // Trigger dynamic resizing of images based on new width
            ReflowItems();
        }

        private void ReflowItems()
        {
            // The available width threshold (Viewport width - 40px padding)
            int maxWidth = _viewPort.Width - 40;
            if (maxWidth < 1) return;

            bool needsAnimation = false;

            foreach (Control c in _flowPanel.Controls)
            {
                if (c is ShelfItem pb && pb.Image != null)
                {
                    // 1. Calculate Standard Size based on Config
                    int targetHeight = SHELF_IMAGE_HEIGHT;
                    int targetWidth = pb.Image.Width * targetHeight / pb.Image.Height;

                    // 2. Check if Standard Size fits in the current window width
                    if (targetWidth > maxWidth)
                    {
                        // Too big: Cap width to maxWidth and shrink height (maintain aspect ratio)
                        targetWidth = maxWidth;
                        targetHeight = targetWidth * pb.Image.Height / pb.Image.Width;
                    }
                    // Else: It fits, so we keep the SHELF_IMAGE_HEIGHT calculated in step 1.
                    // This allows the image to "grow back" when the window is widened.

                    Size targetSize = new Size(targetWidth, targetHeight);

                    // 3. Register target
                    if (!_targetSizes.ContainsKey(c))
                    {
                        _targetSizes[c] = targetSize;
                        needsAnimation = true;
                    }
                    else if (_targetSizes[c] != targetSize)
                    {
                        _targetSizes[c] = targetSize;
                        needsAnimation = true;
                    }

                    // Check if current size differs significantly
                    if (Math.Abs(c.Width - targetWidth) > 1 || Math.Abs(c.Height - targetHeight) > 1)
                    {
                        needsAnimation = true;
                    }
                }
            }

            if (needsAnimation)
            {
                _layoutTimer.Start();
            }
        }

        private void OnLayoutTick(object sender, EventArgs e)
        {
            bool allDone = true;
            float lerpSpeed = 0.25f; // Adjust for smoothness

            // Suspend layout during bulk updates to prevent jitter
            _flowPanel.SuspendLayout();

            // Use a list to iterate keys to allow modification if needed
            var controls = new List<Control>(_targetSizes.Keys);

            foreach (Control c in controls)
            {
                if (c.IsDisposed || c.Parent == null)
                {
                    _targetSizes.Remove(c);
                    continue;
                }

                Size target = _targetSizes[c];
                int currentW = c.Width;
                int currentH = c.Height;
                bool itemDone = true;

                // Animate Width
                if (Math.Abs(target.Width - currentW) > 1)
                {
                    currentW += (int)((target.Width - currentW) * lerpSpeed);
                    itemDone = false;
                }
                else
                {
                    currentW = target.Width; // Snap
                }

                // Animate Height
                if (Math.Abs(target.Height - currentH) > 1)
                {
                    currentH += (int)((target.Height - currentH) * lerpSpeed);
                    itemDone = false;
                }
                else
                {
                    currentH = target.Height; // Snap
                }

                if (!itemDone) allDone = false;

                if (c.Width != currentW || c.Height != currentH)
                {
                    c.Size = new Size(currentW, currentH);
                }
            }

            _flowPanel.ResumeLayout(true);
            UpdateScrollParams(); // Ensure scrollbar stays in sync during animation

            if (allDone)
            {
                _layoutTimer.Stop();
            }
        }

        private void UpdateScrollParams()
        {
            int contentHeight = _flowPanel.Height;
            int viewHeight = _viewPort.Height;

            if (contentHeight > viewHeight)
            {
                _scrollBar.Visible = true;
                _scrollBar.Maximum = contentHeight - viewHeight;
                _scrollBar.LargeChange = viewHeight;

                // Ensure it stays on top after layout updates
                _scrollBar.BringToFront();
            }
            else
            {
                _scrollBar.Visible = false;
                _scrollBar.Value = 0;
                _flowPanel.Top = 0;
            }
        }

        public void AddSource(Guid sourceId, Image img)
        {
            if (img == null) return;

            // Use custom ShelfItem instead of standard PictureBox for better performance
            ShelfItem pb = new ShelfItem();
            pb.Image = (Image)img.Clone();

            // Calculate initial size logic (using Config)
            int targetHeight = SHELF_IMAGE_HEIGHT;
            int targetWidth = img.Width * targetHeight / img.Height;

            // Constrain max width
            int maxWidth = Math.Max(1, this.ClientSize.Width - 40);
            if (targetWidth > maxWidth)
            {
                targetWidth = maxWidth;
                targetHeight = targetWidth * img.Height / img.Width;
            }

            // Set initial size immediately
            pb.Size = new Size(targetWidth, targetHeight);
            pb.Tag = sourceId;

            // Updated Interaction: MouseDown triggers detach
            pb.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    // Convert local click to screen coordinates
                    Point screenClick = pb.PointToScreen(e.Location);
                    Rectangle screenBounds = pb.RectangleToScreen(pb.ClientRectangle);

                    // Tell controller to wake up the window
                    WindowController.RestoreWindowFromShelf(sourceId, screenClick, screenBounds, img);

                    // Remove from shelf
                    _flowPanel.Controls.Remove(pb);
                    _targetSizes.Remove(pb);
                    pb.Dispose();

                    UpdateScrollParams();
                }
            };

            // Forward wheel events from children to main scroll
            pb.MouseWheel += (s, e) => _scrollBar.DoScroll(e.Delta);

            _flowPanel.Controls.Add(pb);

            // Trigger reflow to register it for future animations/adjustments
            ReflowItems();
        }

        public void CreateShelvedTextWindow(string text)
        {
            // 1. Create the window as normal (this adds it to activeWindows)
            Guid id = WindowController.DisplayTextWindow(text);

            if (id == Guid.Empty) return;

            // 2. Retrieve the specific window instance
            // (We cast to TextWindow to access specific methods if needed, 
            // though GetSnapshot is likely on the base class)
            var window = WindowController.GetWindowById(id);

            if (window != null)
            {
                // 3. Force a snapshot BEFORE hiding it
                // This ensures the shelf gets a real image, not a blank box.
                Image snap = window.GetSnapshot();

                // 4. Add to Shelf immediately
                AddSource(id, snap);

                // 5. Hide it immediately
                // Doing this in the same execution block often prevents the 
                // window from even flickering on screen.
                window.Hide();
            }
        }

        private void Shelf_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("ClipmageID") ||
                e.Data.GetDataPresent(DataFormats.Bitmap) ||
                e.Data.GetDataPresent(DataFormats.FileDrop)) {

                e.Effect = DragDropEffects.Move;
                return;
            }

            if (e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text))
            {
                e.Effect = DragDropEffects.Copy; // Show "Copy" cursor
                return;
            }

            e.Effect = DragDropEffects.None;
          
        }

        private void Shelf_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("ClipmageID"))
            {
                Guid id = (Guid)e.Data.GetData("ClipmageID");
                if (e.Data.GetDataPresent(DataFormats.Bitmap))
                {
                    var img = (Image)e.Data.GetData(DataFormats.Bitmap);
                    AddSource(id, img);
                }
            }
            else if (e.Data.GetDataPresent(DataFormats.Bitmap))
            {
                var img = (Image)e.Data.GetData(DataFormats.Bitmap);
                AddSource(Guid.Empty, img);
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var file in files)
                {
                    try
                    {
                        using (var stream = new System.IO.FileStream(file, System.IO.FileMode.Open))
                        {
                            AddSource(Guid.Empty, Image.FromStream(stream));
                        }
                    }
                    catch { }
                }
            }
            else if (e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text))
            {
                // 1. Retrieve the text safely
                string text = (string)e.Data.GetData(DataFormats.UnicodeText);
                if (string.IsNullOrEmpty(text))
                {
                    text = (string)e.Data.GetData(DataFormats.Text);
                }

                // 2. Pass it to your controller to generate the hidden, shelved window
                if (!string.IsNullOrEmpty(text))
                {
                    CreateShelvedTextWindow(text);
                }
            }

        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // The "Toggle" Hack:
            // This forces the Window Manager to re-evaluate the Z-order.
            this.TopMost = false;
            this.TopMost = true;
        }
    }

    // High-performance container item that handles rounded corners internally via OnPaint
    public class ShelfItem : PictureBox
    {
        private int _radius = 8;
        private int _borderWidth = 1;
        private Color _borderColor = Color.FromArgb(255, 75, 75, 75);

        public ShelfItem()
        {
            this.DoubleBuffered = true;
            this.BackColor = Color.Transparent; // CRITICAL: This fixes the black corners
            this.BorderStyle = BorderStyle.None;
            this.SizeMode = PictureBoxSizeMode.Zoom;
            this.Margin = new Padding(5);
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            pe.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            pe.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // 1. Draw Background (Black) restricted to rounded shape
            // This ensures inside the round shape is black (letterboxing), but corners are transparent
            using (GraphicsPath path = CreateRoundedPath(this.ClientRectangle, _radius))
            using (Brush backBrush = new SolidBrush(Color.Black))
            {
                pe.Graphics.FillPath(backBrush, path);
            }

            // 2. Draw Image Clipped
            if (this.Image != null)
            {
                RectangleF imgRect = GetImageRectangle(this.ClientRectangle, this.Image);
                using (GraphicsPath path = CreateRoundedPath(this.ClientRectangle, _radius))
                {
                    pe.Graphics.SetClip(path);
                    pe.Graphics.DrawImage(this.Image, imgRect);
                    pe.Graphics.ResetClip();
                }
            }

            // 3. Draw Border
            float halfPen = _borderWidth / 2f;
            RectangleF borderRect = new RectangleF(halfPen, halfPen, this.Width - _borderWidth, this.Height - _borderWidth);

            using (GraphicsPath borderPath = CreateRoundedPath(borderRect, Math.Max(1, _radius - halfPen)))
            using (Pen pen = new Pen(_borderColor, _borderWidth))
            {
                pe.Graphics.DrawPath(pen, borderPath);
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

        private GraphicsPath CreateRoundedPath(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        
    }

    /// <summary>
    /// A custom scrollbar that mimics the Windows 11 style:
    /// Thin line by default, expands on hover, rounded thumb.
    /// </summary>
    public class ModernScrollBar : Control
    {
        public event EventHandler Scroll;

        private int _value = 0;
        private int _maximum = 100;
        private int _largeChange = 10;
        private bool _isHovered = false;
        private bool _isDragging = false;
        private int _dragStartMouseY;
        private int _dragStartValue;

        // Visual Settings
        private int _idleWidth = 2;
        private int _hoverWidth = 6;
        private Color _thumbColor = Color.FromArgb(120, 120, 120);
        private Color _hoverColor = Color.FromArgb(180, 180, 180);

        public ModernScrollBar()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            this.BackColor = Color.Transparent;
            this.Cursor = Cursors.Default;
        }

        public int Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = Math.Max(0, Math.Min(value, _maximum));
                    Invalidate();
                    Scroll?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public int Maximum
        {
            get => _maximum;
            set { _maximum = value; Invalidate(); }
        }

        public int LargeChange
        {
            get => _largeChange;
            set { _largeChange = value; Invalidate(); }
        }

        public void DoScroll(int delta)
        {
            // Standard scroll amount (e.g. 40px per notch)
            int step = 40;
            if (delta > 0) Value -= step;
            else Value += step;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _isHovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _isHovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                _dragStartMouseY = e.Y;
                _dragStartValue = _value;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _isDragging = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_isDragging)
            {
                int trackHeight = this.Height;
                int contentTotal = _maximum + _largeChange;

                // Calculate scale factor
                float ratio = (float)contentTotal / trackHeight;

                int deltaY = e.Y - _dragStartMouseY;
                Value = _dragStartValue + (int)(deltaY * ratio);
            }
            base.OnMouseMove(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            // Calculate Thumb
            // Proportion: Viewport / TotalContent
            int contentTotal = _maximum + _largeChange;
            if (contentTotal <= 0) return;

            float viewRatio = (float)_largeChange / contentTotal;
            int thumbHeight = Math.Max(20, (int)(this.Height * viewRatio)); // Minimum 20px thumb

            // Position: Value / Maximum
            // Allowable track for thumb top is (Height - thumbHeight)
            float scrollRatio = (float)_value / _maximum;
            if (_maximum == 0) scrollRatio = 0;

            int trackArea = this.Height - thumbHeight;
            int thumbY = (int)(trackArea * scrollRatio);

            // Width animation state
            int currentWidth = (_isHovered || _isDragging) ? _hoverWidth : _idleWidth;
            int xOffset = this.Width - currentWidth - 2; // Right aligned with padding

            // Draw Thumb
            Color c = (_isHovered || _isDragging) ? _hoverColor : _thumbColor;
            using (Brush b = new SolidBrush(c))
            {
                // Draw rounded pill
                Rectangle thumbRect = new Rectangle(xOffset, thumbY, currentWidth, thumbHeight);

                // Simple rounded rect manual draw
                int r = currentWidth; // full round
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddArc(thumbRect.X, thumbRect.Y, r, r, 180, 90);
                    path.AddArc(thumbRect.Right - r, thumbRect.Y, r, r, 270, 90);
                    path.AddArc(thumbRect.Right - r, thumbRect.Bottom - r, r, r, 0, 90);
                    path.AddArc(thumbRect.X, thumbRect.Bottom - r, r, r, 90, 90);
                    path.CloseFigure();
                    e.Graphics.FillPath(b, path);
                }
            }
        }
        
    }
}