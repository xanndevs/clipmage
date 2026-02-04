using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using static Clipmage.WindowHelpers;

namespace Clipmage
{
    public class ShelfWindow : Form
    {
        // Container structure for custom scrolling
        private Panel _viewPort;
        private FlowLayoutPanel _flowPanel;
        private ModernScrollBar _scrollBar;

        private const int IMAGE_SHELF_HEIGHT = 80;

        public ShelfWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Clipmage Shelf";
            this.Size = new Size(290, 500);
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(Screen.PrimaryScreen.WorkingArea.Right - this.Width, Screen.PrimaryScreen.WorkingArea.Bottom - this.Height);
            this.BackColor = Color.FromArgb(20, 20, 20);
            this.ForeColor = Color.White;
            this.TopMost = true;
            this.AllowDrop = true;
            this.FormBorderStyle = FormBorderStyle.SizableToolWindow;

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
            _scrollBar.Size = new Size(10, _viewPort.Height);
            _scrollBar.Location = new Point(_viewPort.Width - (10 + 1), 0);
            _scrollBar.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
            _scrollBar.Visible = true; // Hidden until needed
            _scrollBar.Scroll += (s, e) =>
            {
                // Move the content panel up/down
                _flowPanel.Top = -_scrollBar.Value;
            };

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

            PictureBox pb = new PictureBox();
            pb.Image = (Image)img.Clone();
            pb.SizeMode = PictureBoxSizeMode.Zoom;

            // Calculate size
            int targetHeight = IMAGE_SHELF_HEIGHT;
            int targetWidth = img.Width * targetHeight / img.Height;

            // Constrain max width
            if (targetWidth > (this.Width - 40))
            {
                targetWidth = this.Width - 40;
                targetHeight = targetWidth * img.Height / img.Width;
            }

            pb.Size = new Size(targetWidth, targetHeight);
            pb.Margin = new Padding(5);
            pb.BackColor = Color.Black;
            pb.BorderStyle = BorderStyle.None;
            pb.Tag = sourceId;

            pb.Click += (s, e) =>
            {
                MessageBox.Show($"Clicked Image from Source ID: {sourceId}");
            };

            // Forward wheel events from children to main scroll
            pb.MouseWheel += (s, e) => _scrollBar.DoScroll(e.Delta);

            // Apply rounded look using your helper
            ApplyRoundedRegion(pb, 8, 1, Color.FromArgb(255, 75, 75, 75));

            _flowPanel.Controls.Add(pb);
        }

        private void Shelf_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("ClipmageID") ||
                e.Data.GetDataPresent(DataFormats.Bitmap) ||
                e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Move;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
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
            if (contentTotal <= 0) { 
            return;
            } 

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