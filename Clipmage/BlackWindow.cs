using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Clipmage;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;

using static Clipmage.WindowHelpers;
using static Clipmage.AppConfig;

namespace Clipmage
{
    public class BlackWindow : Form
    {
        private Image _img;

        private int _durationSeconds;
        private bool _isPinned = false;

        private Button _pinButton;
        private Button _editButton;

        private System.Windows.Forms.Timer _lifeTimer;
        private System.Windows.Forms.Timer _fadeTimer;

        // --- Physics / Dragging Variables ---
        private bool _isDragging = false;
        private PointF _velocity;
        private System.Windows.Forms.Timer _physicsTimer;

        // --- Animation / Scaling Variables ---
        private Size _baseSize;
        private float _currentScale = 1.0f;
        private float _targetScale = 1.0f;
        private PointF _anchorRatio;
        private System.Windows.Forms.Timer _animationTimer;

        // --- File Drag Logic Variables ---
        private DateTime _lastMouseMoveTime;
        private Point _dragWaitStartPos;
        private Point _lastTickMousePos;
        private Point _fileDragStartPos;
        private bool _isFileDragActive = false;
        private bool _wasFileDragCancelledByMovement = false;

        private Queue<(DateTime Time, Point Position)> _dragHistory = new Queue<(DateTime, Point)>();

        // --- Imports ---
        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS { public int leftWidth, rightWidth, topHeight, bottomHeight; }

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        public readonly Guid id;

        public BlackWindow(Image screenshotToDisplay, int durationSeconds)
        {
            id = Guid.NewGuid();
            _durationSeconds = durationSeconds;
            _img = screenshotToDisplay;
            InitializeComponent(screenshotToDisplay);
            SetupPinButton();
            SetupEditButton();
            SetupContextMenu();

            SetupLifeTimer(_durationSeconds);
            SetupPhysicsTimer();
            SetupAnimationTimer();
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
                this.Height = DRAG_WINDOW_MAXIMUM_HEIGHT;
                this.Width = (BackgroundImage.Width * DRAG_WINDOW_MAXIMUM_HEIGHT) / BackgroundImage.Height;
            }
            else
            {
                this.Width = DRAG_WINDOW_MAXIMUM_WIDTH;
                this.Height = (BackgroundImage.Height * DRAG_WINDOW_MAXIMUM_WIDTH) / BackgroundImage.Width;
            }

            _baseSize = this.Size;
            this.Location = new Point((Screen.PrimaryScreen.WorkingArea.Width - this.Width) - 16, (Screen.PrimaryScreen.WorkingArea.Height - this.Height) - 16);
            this.StartPosition = FormStartPosition.Manual;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.DoubleBuffered = true;

            this.MouseDown += OnMouseDown;
            this.MouseMove += OnMouseMove;
            this.MouseUp += OnMouseUp;
            this.GiveFeedback += OnGiveFeedback;
        }

        // New method to restore and attach window from shelf
        public void WakeUpFromShelf(Point mousePos, Rectangle startBounds)
        {
            // 1. Reset state to visible
            this.Opacity = 1.0;
            _fadeTimer?.Stop();
            _lifeTimer.Stop(); // Ensure it doesn't close immediately

            // 2. Position exactly over the shelf item
            this.Location = startBounds.Location;
            this.Size = startBounds.Size;
            this.Show();
            this.BringToFront();

            // 3. Calculate Scale based on shelf size vs full size
            _currentScale = (float)this.Width / _baseSize.Width;

            // 4. Calculate Anchor so mouse grabs the same relative spot
            float relX = (float)(mousePos.X - this.Left) / this.Width;
            float relY = (float)(mousePos.Y - this.Top) / this.Height;
            _anchorRatio = new PointF(relX, relY);

            // 5. Init Dragging State
            _isDragging = true;
            _targetScale = DRAG_SCALE_FACTOR; // It will likely be close to this already

            // Reset tracking vars
            _lastMouseMoveTime = DateTime.Now;
            _dragWaitStartPos = Cursor.Position;
            _lastTickMousePos = Cursor.Position;

            _dragHistory.Clear();
            _dragHistory.Enqueue((DateTime.Now, Cursor.Position));

            // Hide buttons during drag
            if (_pinButton != null) _pinButton.Visible = false;
            if (_editButton != null) _editButton.Visible = false;

            // 6. Start Loop
            _animationTimer.Start();
        }

        private void SetupContextMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem itemShelf = new ToolStripMenuItem("Open Shelf");
            itemShelf.Click += (s, e) => WindowController.ToggleShelf();
            menu.Items.Add(itemShelf);

            ToolStripMenuItem itemClose = new ToolStripMenuItem("Close");
            itemClose.Click += (s, e) => this.Close();
            menu.Items.Add(itemClose);

            this.ContextMenuStrip = menu;
        }

        private void ApplyNativeShadowAndRounding()
        {
            MARGINS margins = new MARGINS { leftWidth = 1, rightWidth = 1, topHeight = 1, bottomHeight = 1 };
            DwmExtendFrameIntoClientArea(this.Handle, ref margins);
            int preference = DWMWCP_ROUND;
            DwmSetWindowAttribute(this.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTTRANSPARENT = -1;
            if (m.Msg == WM_NCHITTEST && _isFileDragActive)
            {
                m.Result = (IntPtr)HTTRANSPARENT;
                return;
            }
            base.WndProc(ref m);
        }

        private void SetupPhysicsTimer()
        {
            _physicsTimer = new System.Windows.Forms.Timer();
            _physicsTimer.Interval = WINDOW_REFRESH_INTERVAL;
            _physicsTimer.Tick += OnPhysicsTick;
        }

        private void SetupAnimationTimer()
        {
            _animationTimer = new System.Windows.Forms.Timer();
            _animationTimer.Interval = WINDOW_REFRESH_INTERVAL;
            _animationTimer.Tick += OnAnimationTick;
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                _physicsTimer.Stop();
                _lifeTimer.Stop();
                _velocity = PointF.Empty;

                _lastMouseMoveTime = DateTime.Now;
                _dragWaitStartPos = Cursor.Position;
                _lastTickMousePos = Cursor.Position;

                _dragHistory.Clear();
                _dragHistory.Enqueue((DateTime.Now, Cursor.Position));

                Point mousePos = Cursor.Position;
                float relX = (float)(mousePos.X - this.Left) / this.Width;
                float relY = (float)(mousePos.Y - this.Top) / this.Height;
                _anchorRatio = new PointF(relX, relY);

                _targetScale = DRAG_SCALE_FACTOR;
                if (_pinButton != null) _pinButton.Visible = false;
                if (_editButton != null) _editButton.Visible = false;

                _animationTimer.Start();
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
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
                HandleMouseRelease();
            }
        }

        private void StartFileDrag()
        {
            _isFileDragActive = true;
            _wasFileDragCancelledByMovement = false;
            _fileDragStartPos = Cursor.Position;
            bool dropHandled = false; // Flag to prevent double processing

            string tempPath = Path.Combine(Path.GetTempPath(), $"Clipmage_{DateTime.Now.Ticks}.png");
            int initialStyle = GetWindowLong(this.Handle, GWL_EXSTYLE);

            try
            {
                SetWindowLong(this.Handle, GWL_EXSTYLE, initialStyle | WS_EX_TRANSPARENT);
                _img.Save(tempPath, ImageFormat.Png);

                var dataObject = new DataObject();
                dataObject.SetData(DataFormats.FileDrop, new string[] { tempPath });
                dataObject.SetData(DataFormats.Bitmap, _img);
                dataObject.SetData("ClipmageID", this.id);

                this.QueryContinueDrag += OnQueryContinueDrag;

                DragDropEffects result = this.DoDragDrop(dataObject, DragDropEffects.Copy | DragDropEffects.Move);

                // Check if shelf consumed it via OLE drag
                if (!_wasFileDragCancelledByMovement && result == DragDropEffects.Move)
                {
                    // Clean up drag state immediately to prevent next OnAnimationTick from triggering Release logic
                    _isDragging = false;
                    _animationTimer.Stop();

                    this.Hide();
                    dropHandled = true;
                    return;
                }
            }
            catch { }
            finally
            {
                this.QueryContinueDrag -= OnQueryContinueDrag;
                SetWindowLong(this.Handle, GWL_EXSTYLE, initialStyle);
                _isFileDragActive = false;

                if (!this.IsDisposed)
                {
                    if (_wasFileDragCancelledByMovement)
                    {
                        _lastMouseMoveTime = DateTime.Now;
                        _dragWaitStartPos = Cursor.Position;
                    }
                    else if (!dropHandled)
                    {
                        HandleMouseRelease();
                    }
                }
            }
        }

        private void OnQueryContinueDrag(object sender, QueryContinueDragEventArgs e)
        {
            Point currentPos = Cursor.Position;
            double dist = Math.Sqrt(Math.Pow(currentPos.X - _fileDragStartPos.X, 2) + Math.Pow(currentPos.Y - _fileDragStartPos.Y, 2));

            if (dist > DRAG_CANCEL_DISTANCE)
            {
                e.Action = DragAction.Cancel;
                _wasFileDragCancelledByMovement = true;
            }
        }

        private void OnGiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            if (_isFileDragActive)
            {
                e.UseDefaultCursors = false;
                Cursor.Current = Cursors.Default;

                int newW = (int)(_baseSize.Width * _currentScale);
                int newH = (int)(_baseSize.Height * _currentScale);
                int newX = Cursor.Position.X - (int)(newW * _anchorRatio.X);
                int newY = Cursor.Position.Y - (int)(newH * _anchorRatio.Y);

                float t = (1.0f - _currentScale) / (1.0f - DRAG_SCALE_FACTOR);
                newY -= (int)(((_baseSize.Height * (1 - _anchorRatio.Y) * DRAG_SCALE_FACTOR) + FILE_DRAG_OFFSET) * Math.Min(1.0f, Math.Max(0.0f, t)));

                this.Location = new Point(newX, newY);

                DateTime now = DateTime.Now;
                Point currentPos = Cursor.Position;
                _dragHistory.Enqueue((now, currentPos));
                while (_dragHistory.Count > 0 && (now - _dragHistory.Peek().Time).TotalMilliseconds > SMOOTHING_RANGE_MS)
                {
                    _dragHistory.Dequeue();
                }
            }
        }

        private void HandleMouseRelease()
        {
            _isDragging = false;
            _targetScale = 1.0f;

            // --- CHECK FOR SHELF DROP (Physical Window Drag Mode) ---
            if (WindowController.Shelf != null && WindowController.Shelf.Visible && !WindowController.Shelf.IsDisposed)
            {
                if (this.Bounds.IntersectsWith(WindowController.Shelf.Bounds))
                {
                    WindowController.Shelf.AddSource(this.id, this._img);
                    this.Hide(); // Hide instead of Close
                    _lifeTimer.Stop(); // Ensure timer stops
                    return;
                }
            }

            if (!_isPinned) _lifeTimer.Start();

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
            if (speed > 1.0) _physicsTimer.Start();
        }

        private void OnAnimationTick(object sender, EventArgs e)
        {
            if (_isFileDragActive) return;

            if (_isDragging)
            {
                if ((Control.MouseButtons & MouseButtons.Left) == 0)
                {
                    HandleMouseRelease();
                    return;
                }

                Point currentPos = Cursor.Position;
                if (currentPos != _lastTickMousePos)
                {
                    _lastMouseMoveTime = DateTime.Now;
                    _lastTickMousePos = currentPos;
                    double moveDist = Math.Sqrt(Math.Pow(currentPos.X - _dragWaitStartPos.X, 2) + Math.Pow(currentPos.Y - _dragWaitStartPos.Y, 2));
                    if (moveDist > DRAG_CANCEL_DISTANCE)
                    {
                        _dragWaitStartPos = currentPos;
                    }
                }

                if ((DateTime.Now - _lastMouseMoveTime).TotalMilliseconds > DRAG_WAIT_MS)
                {
                    StartFileDrag();
                    return;
                }
            }

            float lerpSpeed = 0.25f;
            _currentScale += (_targetScale - _currentScale) * lerpSpeed;

            if (Math.Abs(_targetScale - _currentScale) < 0.005f)
            {
                _currentScale = _targetScale;
                if (!_isDragging) _animationTimer.Stop();
                if (_pinButton != null && !_isDragging) _pinButton.Visible = true;
                if (_editButton != null && !_isDragging) _editButton.Visible = true;
            }

            int newW = (int)(_baseSize.Width * _currentScale);
            int newH = (int)(_baseSize.Height * _currentScale);
            int newX, newY;

            if (_isDragging)
            {
                newX = Cursor.Position.X - (int)(newW * _anchorRatio.X);
                newY = Cursor.Position.Y - (int)(newH * _anchorRatio.Y);
                float t = (1.0f - _currentScale) / (1.0f - DRAG_SCALE_FACTOR);
                newY -= (int)(((_baseSize.Height * (1 - _anchorRatio.Y) * DRAG_SCALE_FACTOR) + FILE_DRAG_OFFSET) * Math.Min(1.0f, Math.Max(0.0f, t)));
            }
            else
            {
                Point currentCenter = new Point(this.Left + this.Width / 2, this.Top + this.Height / 2);
                newX = currentCenter.X - newW / 2;
                newY = currentCenter.Y - newH / 2;
            }

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

            if (nextX < bounds.Left) { nextX = bounds.Left; _velocity.X = -_velocity.X * BOUNCE_FACTOR; }
            else if (nextX + this.Width > bounds.Right) { nextX = bounds.Right - this.Width; _velocity.X = -_velocity.X * BOUNCE_FACTOR; }

            if (nextY < bounds.Top) { nextY = bounds.Top; _velocity.Y = -_velocity.Y * BOUNCE_FACTOR; }
            else if (nextY + this.Height > bounds.Bottom) { nextY = bounds.Bottom - this.Height; _velocity.Y = -_velocity.Y * BOUNCE_FACTOR; }

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
            _pinButton.Size = new Size(INTERACTION_BUTTON_SIZE, INTERACTION_BUTTON_SIZE);
            _pinButton.Location = new Point((this.Width - INTERACTION_BUTTON_SIZE) - 8, 8);
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
            ApplyRoundedRegion(_pinButton, BUTTON_CORNER_RADIUS, 1, Color.Empty);
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
            _editButton.Size = new Size(INTERACTION_BUTTON_SIZE, INTERACTION_BUTTON_SIZE);
            _editButton.Location = new Point(this.Width - ((INTERACTION_BUTTON_SIZE + 8) * 2), 8);
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
            ApplyRoundedRegion(_editButton, BUTTON_CORNER_RADIUS, 1, Color.Empty);
            this.Controls.Add(_editButton);
        }

        private async void SwitchToEdit()
        {
            string tempFileName = $"snip_{Guid.NewGuid()}.png";
            string tempFilePath = Path.Combine(Path.GetTempPath(), tempFileName);
            _img.Save(tempFilePath, ImageFormat.Png);
            StorageFile file = await StorageFile.GetFileFromPathAsync(tempFilePath);
            string token = SharedStorageAccessManager.AddFile(file);
            string uriString = $"ms-screensketch:edit?source=MyApp&isTemporary=true&sharedAccessToken={token}";
            await Launcher.LaunchUriAsync(new Uri(uriString));
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
            _animationTimer.Stop(); _physicsTimer.Stop(); _lifeTimer.Stop();
            StartFadeOut();
        }

        private void StartFadeOut()
        {
            _fadeTimer = new System.Windows.Forms.Timer();
            _fadeTimer.Interval = 16;
            _fadeTimer.Tick += OnFadeTick;
            _fadeTimer.Start();
        }

        private void OnFadeTick(object sender, EventArgs e)
        {
            if (this.Opacity > 0) this.Opacity -= 0.05;
            else { _fadeTimer.Stop(); _fadeTimer.Dispose(); _lifeTimer.Dispose(); this.Close(); }
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
            if (_fadeTimer != null)
            {
                _fadeTimer.Stop();
                _fadeTimer.Dispose();
            }
            base.OnFormClosed(e);
        }
    }
}