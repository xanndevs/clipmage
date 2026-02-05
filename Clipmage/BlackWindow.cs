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
        private System.Windows.Forms.Timer _fadeTimer; // Timer for the fade-out effect

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


        // --- File Drag Logic Variables ---
        private DateTime _lastMouseMoveTime;       // Tracks when we last moved the mouse
        private Point _dragWaitStartPos;           // Where the mouse was when the wait started
        private Point _lastTickMousePos;           // Tracks mouse pos between animation ticks
        private Point _fileDragStartPos;           // Where the active file drag started
        private bool _isFileDragActive = false;    // Are we currently in a DoDragDrop loop?
        private bool _wasFileDragCancelledByMovement = false; // Flag to handle resume logic




        // Smoothing for throw velocity
        private Queue<(DateTime Time, Point Position)> _dragHistory = new Queue<(DateTime, Point)>();

        // --- DWM / Shadow Imports ---
        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        // --- Window Styles Imports (For Click-Through) ---
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20; // Makes window transparent to mouse clicks

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

        public readonly Guid id;

        public BlackWindow(Image screenshotToDisplay, int durationSeconds)
        {
            id = Guid.NewGuid();
            _durationSeconds = durationSeconds;
            _img = screenshotToDisplay;
            InitializeComponent(screenshotToDisplay);
            SetupPinButton();
            SetupEditButton();
            SetupContextMenu(); // Added Context Menu

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
                this.Height = DRAG_WINDOW_MAXIMUM_HEIGHT;
                this.Width = (BackgroundImage.Width * DRAG_WINDOW_MAXIMUM_HEIGHT) / BackgroundImage.Height;
            }
            else
            {
                this.Width = DRAG_WINDOW_MAXIMUM_WIDTH;
                this.Height = (BackgroundImage.Height * DRAG_WINDOW_MAXIMUM_WIDTH) / BackgroundImage.Width;
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
            this.GiveFeedback += OnGiveFeedback; // Hook up feedback for custom drag cursor
        }

        // --- DWM Logic for Beautiful Shadows ---
        private void ApplyNativeShadowAndRounding()
        {
            MARGINS margins = new MARGINS { leftWidth = 1, rightWidth = 1, topHeight = 1, bottomHeight = 1 };
            DwmExtendFrameIntoClientArea(this.Handle, ref margins);

            int preference = DWMWCP_ROUND;
            DwmSetWindowAttribute(this.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }

        // --- Hit Test Logic for Drag Transparency ---
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
            _animationTimer.Interval = WINDOW_REFRESH_INTERVAL;
            _animationTimer.Tick += OnAnimationTick;
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                _physicsTimer.Stop();

                // Stop timer to prevent auto-close while holding
                _lifeTimer.Stop();

                _velocity = PointF.Empty;

                // Track time for "Hold to Drag" logic
                _lastMouseMoveTime = DateTime.Now;
                _dragWaitStartPos = Cursor.Position;
                _lastTickMousePos = Cursor.Position;

                // Clear physics history
                _dragHistory.Clear();
                _dragHistory.Enqueue((DateTime.Now, Cursor.Position));

                // 1. Calculate Anchor Ratio (0.0 to 1.0)
                Point mousePos = Cursor.Position;
                float relX = (float)(mousePos.X - this.Left) / this.Width;
                float relY = (float)(mousePos.Y - this.Top) / this.Height;

                _anchorRatio = new PointF(relX, relY);
                // 2. Set Target Scale (Shrink)
                _targetScale = DRAG_SCALE_FACTOR;

                // 3. Hide Buttons
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
                // We only log history for the "Throw" physics here.
                // NOTE: The "Hold to Drag" cancellation logic has been moved to OnAnimationTick.

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

        // New method to initiate the File Drag operation
        private void StartFileDrag()
        {
            _isFileDragActive = true;
            _wasFileDragCancelledByMovement = false;

            // Record where the FILE drag actually started so we can measure delta inside the loop
            _fileDragStartPos = Cursor.Position;

            string tempPath = Path.Combine(Path.GetTempPath(), $"Clipmage_{DateTime.Now.Ticks}.png");

            // CRITICAL FIX: Temporarily make the window transparent to input (click-through)
            int initialStyle = GetWindowLong(this.Handle, GWL_EXSTYLE);

            try
            {
                // Apply WS_EX_TRANSPARENT
                SetWindowLong(this.Handle, GWL_EXSTYLE, initialStyle | WS_EX_TRANSPARENT);

                _img.Save(tempPath, ImageFormat.Png);

                //Normal Drag And Drop
                var dataObject = new DataObject();
                dataObject.SetData(DataFormats.FileDrop, new string[] { tempPath });
                dataObject.SetData(DataFormats.Bitmap, _img);

                //Clipmage Exception - passing the ID
                dataObject.SetData("ClipmageID", this.id);


                // Listen for drag updates to cancel if moved too far
                this.QueryContinueDrag += OnQueryContinueDrag;

                // Stores the result of the drag operation for post-drag logic
                DragDropEffects result = this.DoDragDrop(dataObject, DragDropEffects.Copy | DragDropEffects.Move);

                // Check if shelf consumed it
                if (!_wasFileDragCancelledByMovement && result == DragDropEffects.Move)
                {
                    this.Hide();
                    return;
                }
            }
            catch { }
            finally
            {
                this.QueryContinueDrag -= OnQueryContinueDrag;

                // Always restore the window to be interactive again
                SetWindowLong(this.Handle, GWL_EXSTYLE, initialStyle);
                _isFileDragActive = false;

                if (_wasFileDragCancelledByMovement)
                {
                    // If cancelled by movement, we RESUME normal window dragging.
                    // We do NOT call HandleMouseRelease.
                    // We reset the hold timer so it doesn't just trigger again instantly.
                    _lastMouseMoveTime = DateTime.Now;
                    _dragWaitStartPos = Cursor.Position;

                    // We remain in _isDragging = true state, so window follows mouse in OnAnimationTick
                }
                else
                {
                    // Normal drop or user pressed Escape -> Release window
                    HandleMouseRelease();
                }
            }
        }

        private void OnQueryContinueDrag(object sender, QueryContinueDragEventArgs e)
        {
            // This fires continuously while dragging.
            // Check if cursor has moved far enough to cancel "File Drag Mode" and return to "Window Drag Mode"
            Point currentPos = Cursor.Position;
            double dist = Math.Sqrt(Math.Pow(currentPos.X - _fileDragStartPos.X, 2) + Math.Pow(currentPos.Y - _fileDragStartPos.Y, 2));

            if (dist > DRAG_CANCEL_DISTANCE)
            {
                // Cancel the DoDragDrop loop
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
                newY -= (int)(((_baseSize.Height * (1-_anchorRatio.Y) * DRAG_SCALE_FACTOR) + FILE_DRAG_OFFSET) * Math.Min(1.0f, Math.Max(0.0f, t)));

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

        // Shared logic for finishing a drag (either from MouseUp or End of FileDrag)
        private void HandleMouseRelease()
        {
            _isDragging = false;
            _targetScale = 1.0f;

            if (!_isPinned)
            {
                _lifeTimer.Start();
            }

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
        }

        private void OnAnimationTick(object sender, EventArgs e)
        {
            // Guard against running logic if DoDragDrop has hijacked the thread
            if (_isFileDragActive) return;

            // --- Check for Hold-to-Drag Trigger ---
            if (_isDragging)
            {
                // Ensure we detect mouse up even if event was lost
                if ((Control.MouseButtons & MouseButtons.Left) == 0)
                {
                    HandleMouseRelease();
                    return;
                }

                // Check physical mouse movement regardless of window position updates
                Point currentPos = Cursor.Position;
                if (currentPos != _lastTickMousePos)
                {
                    // Mouse moved -> Reset hold timer
                    _lastMouseMoveTime = DateTime.Now;
                    _lastTickMousePos = currentPos;

                    // If moved too far from initial wait spot, reset the anchor
                    double moveDist = Math.Sqrt(Math.Pow(currentPos.X - _dragWaitStartPos.X, 2) + Math.Pow(currentPos.Y - _dragWaitStartPos.Y, 2));
                    if (moveDist > DRAG_CANCEL_DISTANCE)
                    {
                        _dragWaitStartPos = currentPos;
                    }
                }

                // If holding still for > 200ms, start file drag
                if ((DateTime.Now - _lastMouseMoveTime).TotalMilliseconds > DRAG_WAIT_MS)
                {
                    StartFileDrag();
                    return; // StartFileDrag blocks; return to ensure clean state on resume
                }
            }

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
                newX = Cursor.Position.X - (int)(newW * _anchorRatio.X);
                newY = Cursor.Position.Y - (int)(newH * _anchorRatio.Y);

                // Apply interpolated offset based on scale to expose cursor
                // This makes the window "slide up" 16px as it shrinks
                float t = (1.0f - _currentScale) / (1.0f - DRAG_SCALE_FACTOR);
                newY -= (int)(((_baseSize.Height * (1 - _anchorRatio.Y) * DRAG_SCALE_FACTOR) + FILE_DRAG_OFFSET) * Math.Min(1.0f, Math.Max(0.0f, t)));
            }
            else
            {
                // If restoring (mouse up), expand from the CENTER of the current window
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
            _pinButton.Size = new Size(INTERACTION_BUTTON_SIZE, INTERACTION_BUTTON_SIZE);
            _pinButton.Location = new Point((this.Width - INTERACTION_BUTTON_SIZE) - 8, 8);

            // Anchor ensures the button stays in the corner if we resize (though we hide it during drag)
            _pinButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            _pinButton.FlatStyle = FlatStyle.Flat;
            _pinButton.BackColor = Color.FromArgb(255, 39, 41, 42);
            _pinButton.FlatAppearance.BorderSize = 0;
            _pinButton.FlatAppearance.BorderColor = Color.DimGray;
            _pinButton.ForeColor = Color.LightGray;

            _pinButton.FlatStyle = FlatStyle.Flat;
            _pinButton.UseVisualStyleBackColor = true;

            _pinButton.Cursor = Cursors.Hand;
            _pinButton.Font = new Font("Segoe Fluent Icons", 10f, FontStyle.Regular);

            _pinButton.Click += (s, e) => TogglePin();

            // We still use manual region rounding for the BUTTON itself
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
                //_pinButton.BackColor = Color.FromArgb(255, 76, 162, 230);
                _pinButton.FlatAppearance.BorderColor = Color.FromArgb(255, 48, 117, 171);
                _pinButton.ForeColor= Color.FromArgb(255, 48, 117, 171);
            }
            else
            {
                _pinButton.Text = "\ue718";
                //_pinButton.BackColor = Color.FromArgb(255, 39, 41, 42);
                _pinButton.FlatAppearance.BorderColor = Color.DimGray;
                _pinButton.ForeColor= Color.LightGray;


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

        // --- Fade Out Logic ---
        private void OnTimerTick(object sender, EventArgs e)
        {
            // Instead of closing immediately, start the fade out.
            _animationTimer.Stop();
            _physicsTimer.Stop();
            _lifeTimer.Stop();

            StartFadeOut();
        }

        private void StartFadeOut()
        {
            _fadeTimer = new System.Windows.Forms.Timer();
            _fadeTimer.Interval = WINDOW_REFRESH_INTERVAL; // ~120 FPS
            _fadeTimer.Tick += OnFadeTick;
            _fadeTimer.Start();
        }

        private void OnFadeTick(object sender, EventArgs e)
        {
            // Reduce opacity by 2.5% per tick
            if (this.Opacity > 0)
            {
                this.Opacity -= 0.025;
            }
            else
            {
                // When fully invisible, clean up and close
                _fadeTimer.Stop();
                _fadeTimer.Dispose();
                _lifeTimer.Dispose();
                this.Close();
            }
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