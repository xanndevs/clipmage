using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;

//snipping tool connection
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

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

    public class BlackWindow : Form
    {
        private int FixedWindowWidth = 300;
        private int FixedWindowHeight = 300;
        private const int buttonSize = 32;
        private Image _img;

        private int _durationSeconds;
        private int _cornerRadius = 3;
        private bool _isPinned = false;
        private Button _pinButton;
        private Button _editButton;

        private System.Windows.Forms.Timer _lifeTimer;

        // --- Physics / Dragging Variables ---
        private bool _isDragging = false;
        private PointF _velocity;
        private System.Windows.Forms.Timer _physicsTimer;

        // --- Animation / Scaling Variables ---
        private Size _baseSize;           // The full 100% size
        private float _currentScale = 1.0f;
        private float _targetScale = 1.0f;
        private PointF _anchorRatio;      // Where we grabbed the window (0.0-1.0)
        private System.Windows.Forms.Timer _animationTimer;
        private const float DRAG_SCALE_FACTOR = 0.2f;

        // Physics Constants 
        private const float FRICTION = 0.86f;
        private const float MIN_VELOCITY = 0.85f;
        private const int WINDOW_REFRESH_INTERVAL = 8;
        
        private const float BOUNCE_FACTOR = 0.85f;
        private const int SMOOTHING_RANGE_MS = 14;

        // Smoothing for throw velocity
        private Queue<(DateTime Time, Point Position)> _dragHistory = new Queue<(DateTime, Point)>();

        // --- DWM / Shadow Imports ---
        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int leftWidth;
            public int rightWidth;
            public int topHeight;
            public int bottomHeight;
        }

        // Constants for DWM
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2; // Force round corners (Win11)

        public BlackWindow(Image screenshotToDisplay, int durationSeconds)
        {
            _durationSeconds = durationSeconds;
            _img = screenshotToDisplay;
            InitializeComponent(screenshotToDisplay);
            SetupPinButton();
            SetupEditButton();

            SetupLifeTimer(_durationSeconds);
            SetupPhysicsTimer();
            SetupAnimationTimer(); // Init animation loop

            // Apply native shadow and rounding
            ApplyNativeShadowAndRounding();
        }

        private void InitializeComponent(Image img)
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.Black;
            this.BackgroundImage = img;
            this.BackgroundImageLayout = ImageLayout.Zoom;
            this.WindowState = FormWindowState.Normal;

            if ((double)this.BackgroundImage.Height / this.BackgroundImage.Width > 1)
            {
                this.Height = FixedWindowHeight;
                this.Width = (BackgroundImage.Width * FixedWindowHeight) / BackgroundImage.Height;
            }
            else
            {
                this.Width = FixedWindowWidth;
                this.Height = (BackgroundImage.Height * FixedWindowWidth) / BackgroundImage.Width;
            }

            // Capture the calculated full size as our base
            _baseSize = this.Size;

            this.Location = new Point((Screen.PrimaryScreen.WorkingArea.Width - this.Width) - 16, (Screen.PrimaryScreen.WorkingArea.Height - this.Height) - 16);
            this.StartPosition = FormStartPosition.Manual;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.DoubleBuffered = true;

            this.MouseDown += OnMouseDown;
            this.MouseMove += OnMouseMove;
            this.MouseUp += OnMouseUp;
        }

        // --- DWM Logic for Beautiful Shadows ---
        private void ApplyNativeShadowAndRounding()
        {
            MARGINS margins = new MARGINS { leftWidth = 1, rightWidth = 1, topHeight = 1, bottomHeight = 1 };
            DwmExtendFrameIntoClientArea(this.Handle, ref margins);

            int preference = DWMWCP_ROUND;
            DwmSetWindowAttribute(this.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }

        // --- Physics / Dragging / Animation Logic ---

        private void SetupPhysicsTimer()
        {
            _physicsTimer = new System.Windows.Forms.Timer();
            _physicsTimer.Interval = WINDOW_REFRESH_INTERVAL;
            _physicsTimer.Tick += OnPhysicsTick;
        }

        private void SetupAnimationTimer()
        {
            _animationTimer = new System.Windows.Forms.Timer();
            _animationTimer.Interval = WINDOW_REFRESH_INTERVAL; // ~60 FPS
            _animationTimer.Tick += OnAnimationTick;
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                _physicsTimer.Stop();
                _velocity = PointF.Empty;

                // Clear physics history
                _dragHistory.Clear();
                _dragHistory.Enqueue((DateTime.Now, Cursor.Position));

                // 1. Calculate Anchor Ratio (0.0 to 1.0)
                // This tells us where we grabbed the window relative to its size.
                // We use this to keep the window "glued" to the mouse while scaling.
                Point mousePos = Cursor.Position;
                float relX = (float)(mousePos.X - this.Left) / this.Width;
                float relY = (float)(mousePos.Y - this.Top) / this.Height;
                _anchorRatio = new PointF(relX, relY);

                // 2. Set Target Scale (Shrink)
                _targetScale = DRAG_SCALE_FACTOR;

                // 3. Hide Pin Button and Edit Button
                if (_pinButton != null) _pinButton.Visible = false;
                if (_editButton != null) _editButton.Visible = false;

                // 4. Start Animation Loop (Handles both scaling and movement)
                _animationTimer.Start();
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                // NOTE: We do NOT update this.Location here anymore.
                // The position update is handled in OnAnimationTick to sync with resizing.

                // We only log history for the "Throw" physics
                DateTime now = DateTime.Now;
                Point currentPos = Cursor.Position;
                _dragHistory.Enqueue((now, currentPos));

                while (_dragHistory.Count > 0 && (now - _dragHistory.Peek().Time).TotalMilliseconds > SMOOTHING_RANGE_MS)
                {
                    _dragHistory.Dequeue();
                }
            }
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _isDragging)
            {
                _isDragging = false;

                // 1. Set Target Scale (Restore)
                _targetScale = 1.0f;

                // 2. Restore Pin Button
                //if (_pinButton != null) _pinButton.Visible = true;
                //It should come back after the animation exits

                // 3. Calculate Physics Throw Velocity
                if (_dragHistory.Count >= 2)
                {
                    var newest = _dragHistory.Last();
                    var oldest = _dragHistory.First();

                    double totalMs = (newest.Time - oldest.Time).TotalMilliseconds;
                    if (totalMs > 0)
                    {
                        float dx = newest.Position.X - oldest.Position.X;
                        float dy = newest.Position.Y - oldest.Position.Y;
                        float pxPerMsX = dx / (float)totalMs;
                        float pxPerMsY = dy / (float)totalMs;
                        _velocity = new PointF(pxPerMsX * WINDOW_REFRESH_INTERVAL, pxPerMsY * WINDOW_REFRESH_INTERVAL);
                    }
                }

                double speed = Math.Sqrt(_velocity.X * _velocity.X + _velocity.Y * _velocity.Y);
                if (speed > 1.0)
                {
                    _physicsTimer.Start();
                }

                // Keep _animationTimer running until we finish expanding back to 1.0
            }
        }

        private void OnAnimationTick(object sender, EventArgs e)
        {
            // 1. Lerp the Scale
            float lerpSpeed = 0.25f; // Adjust for snap vs smooth
            _currentScale += (_targetScale - _currentScale) * lerpSpeed;

            // Snap if close enough
            if (Math.Abs(_targetScale - _currentScale) < 0.005f)
            {
                _currentScale = _targetScale;
                // Only stop timer if we are done scaling AND not dragging
                if (!_isDragging) _animationTimer.Stop();
                if (_pinButton != null && !_isDragging) _pinButton.Visible = true;
                if (_editButton != null && !_isDragging) _editButton.Visible = true;
            }

            // 2. Calculate New Dimensions
            int newW = (int)(_baseSize.Width * _currentScale);
            int newH = (int)(_baseSize.Height * _currentScale);

            // 3. Calculate New Position
            int newX, newY;

            if (_isDragging)
            {
                // If dragging, position relative to the mouse anchor
                // This keeps the specific pixel you grabbed under the cursor
                newX = Cursor.Position.X - (int)(newW * _anchorRatio.X);
                newY = Cursor.Position.Y - (int)(newH * _anchorRatio.Y);
            }
            else
            {
                // If restoring (mouse up), expand from the CENTER of the current window
                // This prevents the window from jumping; it just grows in place
                Point currentCenter = new Point(this.Left + this.Width / 2, this.Top + this.Height / 2);
                newX = currentCenter.X - newW / 2;
                newY = currentCenter.Y - newH / 2;
            }

            // 4. Apply Updates
            this.Bounds = new Rectangle(newX, newY, newW, newH);
        }

        private void OnPhysicsTick(object sender, EventArgs e)
        {
            if (this.IsDisposed || !this.Created)
            {
                _physicsTimer.Stop();
                return;
            }

            float nextX = this.Left + _velocity.X;
            float nextY = this.Top + _velocity.Y;

            Rectangle bounds = Screen.FromControl(this).WorkingArea;

            // Left Wall
            if (nextX < bounds.Left)
            {
                nextX = bounds.Left;
                _velocity.X = -_velocity.X * BOUNCE_FACTOR;
            }
            // Right Wall
            else if (nextX + this.Width > bounds.Right)
            {
                nextX = bounds.Right - this.Width;
                _velocity.X = -_velocity.X * BOUNCE_FACTOR;
            }

            // Top Wall
            if (nextY < bounds.Top)
            {
                nextY = bounds.Top;
                _velocity.Y = -_velocity.Y * BOUNCE_FACTOR;
            }
            // Bottom Wall
            else if (nextY + this.Height > bounds.Bottom)
            {
                nextY = bounds.Bottom - this.Height;
                _velocity.Y = -_velocity.Y * BOUNCE_FACTOR;
            }

            this.Location = new Point((int)nextX, (int)nextY);

            _velocity.X *= FRICTION;
            _velocity.Y *= FRICTION;

            if (Math.Abs(_velocity.X) < MIN_VELOCITY && Math.Abs(_velocity.Y) < MIN_VELOCITY)
            {
                _physicsTimer.Stop();
            }
        }

        private void SetupPinButton()
        {
            _pinButton = new Button();
            _pinButton.Text = "\ue718";
            _pinButton.Size = new Size(buttonSize, buttonSize);
            _pinButton.Location = new Point((this.Width - buttonSize) - 8, 8);

            // Anchor ensures the button stays in the corner if we resize (though we hide it during drag)
            _pinButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            _pinButton.FlatStyle = FlatStyle.Flat;
            _pinButton.ForeColor = Color.LightGray;
            _pinButton.BackColor = Color.FromArgb(255, 39, 41, 42);
            _pinButton.FlatAppearance.BorderSize = 0;
            _pinButton.FlatAppearance.BorderColor = Color.DimGray;
            _pinButton.FlatStyle = FlatStyle.Flat;
            _pinButton.UseVisualStyleBackColor = true;

            _pinButton.Cursor = Cursors.Hand;
            _pinButton.Font = new Font("Segoe Fluent Icons", 10f, FontStyle.Regular);

            _pinButton.Click += (s, e) => TogglePin();

            // We still use manual region rounding for the BUTTON itself
            ApplyRoundedRegion(_pinButton, _cornerRadius);

            this.Controls.Add(_pinButton);
        }

        private void TogglePin()
        {
            _isPinned = !_isPinned;

            if (_isPinned)
            {
                _lifeTimer.Stop();
                _pinButton.Text = "\uE77a";
                _pinButton.ForeColor = Color.LightGray;
                _pinButton.BackColor = Color.FromArgb(255, 76, 162, 230);
                _pinButton.FlatAppearance.BorderColor = Color.FromArgb(255, 48, 117, 171);
            }
            else
            {
                _pinButton.Text = "\ue718";
                _pinButton.ForeColor = Color.LightGray;
                _pinButton.BackColor = Color.FromArgb(255, 39, 41, 42);
                _pinButton.FlatAppearance.BorderColor = Color.DimGray;

                _lifeTimer.Stop();
                SetupLifeTimer(2);
            }
        }

        private void SetupEditButton()
        {
            _editButton = new Button();
            _editButton.Text = "\ue70f";
            _editButton.Size = new Size(buttonSize, buttonSize);
            _editButton.Location = new Point(this.Width - ((buttonSize + 8) * 2), 8);

            // Anchor ensures the button stays in the corner if we resize (though we hide it during drag)
            _editButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            _editButton.FlatStyle = FlatStyle.Flat;
            _editButton.ForeColor = Color.LightGray;
            _editButton.BackColor = Color.FromArgb(255, 39, 41, 42);
            _editButton.FlatAppearance.BorderSize = 0;
            _editButton.FlatAppearance.BorderColor = Color.DimGray;
            _editButton.FlatStyle = FlatStyle.Flat;
            _editButton.UseVisualStyleBackColor = true;

            _editButton.Cursor = Cursors.Hand;
            _editButton.Font = new Font("Segoe Fluent Icons", 10f, FontStyle.Regular);

            _editButton.Click += (s, e) => SwitchToEdit();

            // We still use manual region rounding for the BUTTON itself
            ApplyRoundedRegion(_editButton, _cornerRadius);

            this.Controls.Add(_editButton);
        }

        private async void SwitchToEdit() 
        {

            // 1. Save the Image to a temp file in a location UWP can access
            string tempFileName = $"snip_{Guid.NewGuid()}.png";
            string tempFilePath = Path.Combine(Path.GetTempPath(), tempFileName);
            _img.Save(tempFilePath, ImageFormat.Png);

            // 2. Convert to a Windows StorageFile object
            StorageFile file = await StorageFile.GetFileFromPathAsync(tempFilePath);

            // 3. Generate the sharedAccessToken
            // This adds the file to a "safe list" so the Snipping Tool can read it
            string token = SharedStorageAccessManager.AddFile(file);

            // 4. Build the URI using the token you just created
            // We use isTemporary=true so Windows cleans up the token/access later
            string uriString = $"ms-screensketch:edit?source=MyApp&isTemporary=true&sharedAccessToken={token}";

            // 5. Launch
            await Launcher.LaunchUriAsync(new Uri(uriString));
        }

        private void ApplyRoundedRegion(Control control, int radius)
        {
            if (control == null || control.Width <= 0 || control.Height <= 0) return;

            using (Bitmap mask = new Bitmap(control.Width, control.Height))
            {
                using (Graphics g = Graphics.FromImage(mask))
                {
                    g.Clear(Color.Transparent);
                    using (Brush b = new SolidBrush(Color.Black))
                    {
                        DrawRoundedShape(g, b, new Rectangle(0, 0, control.Width, control.Height), radius, RoundedCorners.All);
                    }
                }
                control.Region = BitmapToRegion(mask);
            }
        }

        private void DrawRoundedShape(Graphics g, Brush brush, Rectangle rec, int radius, RoundedCorners corners)
        {
            int x = rec.X;
            int y = rec.Y;
            int diameter = radius * 2;

            var horiz = new Rectangle(x, y + radius, rec.Width, rec.Height - diameter);
            var vert = new Rectangle(x + radius, y, rec.Width - diameter, rec.Height);

            g.FillRectangle(brush, horiz);
            g.FillRectangle(brush, vert);

            if ((corners & RoundedCorners.TopLeft) == RoundedCorners.TopLeft)
                g.FillEllipse(brush, x, y, diameter, diameter);
            else
                g.FillRectangle(brush, x, y, diameter, diameter);

            if ((corners & RoundedCorners.TopRight) == RoundedCorners.TopRight)
                g.FillEllipse(brush, x + rec.Width - (diameter + 1), y, diameter, diameter);
            else
                g.FillRectangle(brush, x + rec.Width - (diameter + 1), y, diameter, diameter);

            if ((corners & RoundedCorners.BottomLeft) == RoundedCorners.BottomLeft)
                g.FillEllipse(brush, x, y + rec.Height - (diameter + 1), diameter, diameter);
            else
                g.FillRectangle(brush, x, y + rec.Height - (diameter + 1), diameter, diameter);

            if ((corners & RoundedCorners.BottomRight) == RoundedCorners.BottomRight)
                g.FillEllipse(brush, x + rec.Width - (diameter + 1), y + rec.Height - (diameter + 1), diameter, diameter);
            else
                g.FillRectangle(brush, x + rec.Width - (diameter + 1), y + rec.Height - (diameter + 1), diameter, diameter);
        }

        private Region BitmapToRegion(Bitmap bitmap)
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

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            base.OnPaint(e);
        }

        private void SetupLifeTimer(int seconds)
        {
            _lifeTimer = new System.Windows.Forms.Timer();
            _lifeTimer.Interval = seconds * 1000;
            _lifeTimer.Tick += OnTimerTick;
            _lifeTimer.Start();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            _animationTimer.Stop(); // Ensure animation stops
            _physicsTimer.Stop();
            _lifeTimer.Stop();
            _lifeTimer.Dispose();
            this.Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_physicsTimer != null)
            {
                _physicsTimer.Stop();
                _physicsTimer.Dispose();
            }
            if (_animationTimer != null)
            {
                _animationTimer.Stop();
                _animationTimer.Dispose();
            }
            base.OnFormClosed(e);
        }
    }
}