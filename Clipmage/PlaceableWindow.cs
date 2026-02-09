using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static Clipmage.WindowHelpers;
using static Clipmage.AppConfig;

namespace Clipmage
{
    /// <summary>
    /// Base class for any floating, draggable, physics-enabled window.
    /// Handles DWM, Physics, Drag-Drop loop, and Lifecycle.
    /// </summary>
    public abstract class PlaceableWindow : Form
    {
        public readonly Guid id = Guid.NewGuid();

        private int _durationSeconds;
        private bool _isPinned = false;

        protected Button _pinButton;
        private Button _editButton;

        private System.Windows.Forms.Timer _lifeTimer;
        private System.Windows.Forms.Timer _fadeTimer;

        // --- Physics / Dragging Variables ---
        private bool _isDragging = false;
        private PointF _velocity;
        private System.Windows.Forms.Timer _physicsTimer;

        // --- Animation / Scaling Variables ---
        protected Size _baseSize;
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

        // --- Native Imports ---
        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        [DllImport("user32.dll")]
        protected static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        protected static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        protected const int GWL_EXSTYLE = -20;
        protected const int WS_EX_TRANSPARENT = 0x20;

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS { public int leftWidth, rightWidth, topHeight, bottomHeight; }

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        public PlaceableWindow(int durationSeconds)
        {
            _durationSeconds = durationSeconds;

            // Common Form Properties
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.Black;
            this.StartPosition = FormStartPosition.Manual;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.DoubleBuffered = true;

            SetupPinButton();
            SetupContextMenu();

            SetupLifeTimer(_durationSeconds);
            SetupPhysicsTimer();
            SetupAnimationTimer();
            ApplyNativeShadowAndRounding();

            // Bind Events
            this.MouseDown += OnMouseDown;
            this.MouseMove += OnMouseMove;
            this.MouseUp += OnMouseUp;
            this.GiveFeedback += OnGiveFeedback;
        }

        // --- Abstract / Virtual Methods for Children ---

        /// <summary>
        /// Child classes must implement this to provide the data dragged (Image, Text, etc.)
        /// </summary>
        protected abstract DataObject CreateDragDataObject();

        /// <summary>
        /// Optional: Child classes can override to get a thumbnail for the shelf
        /// </summary>
        public abstract Image GetSnapshot();

        // --- Setup & Styling ---

        protected void ApplyNativeShadowAndRounding()
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

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            base.OnPaint(e);
        }

        protected void SetupContextMenu()
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
            
        private void SetupPinButton()
        {
            _pinButton = new Button();
            _pinButton.Text = "\ue718";
            _pinButton.Size = new Size(INTERACTION_BUTTON_SIZE, INTERACTION_BUTTON_SIZE);
            _pinButton.Location = new Point(this.ClientSize.Width - ((INTERACTION_BUTTON_SIZE + 8) * 1), 8);
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

        protected virtual void TogglePin()
        {
            _isPinned = !_isPinned;
            if (_isPinned)
            {
                _lifeTimer.Stop();
                _pinButton.Text = "\uE77a";
                _pinButton.ForeColor = Color.FromArgb(255, 76, 162, 230);
                _pinButton.FlatAppearance.BorderColor = Color.FromArgb(255, 48, 117, 171);
            }
            else
            {
                _pinButton.Text = "\ue718";
                _pinButton.ForeColor = Color.LightGray;
                _pinButton.FlatAppearance.BorderColor = Color.DimGray;
                _lifeTimer.Stop();
                SetupLifeTimer(2);
            }
        }

        // --- Logic: Restore from Shelf ---
        public void WakeUpFromShelf(Point mousePos, Rectangle startBounds)
        {
            this.Opacity = 1.0;
            _fadeTimer?.Stop();
            _lifeTimer.Stop();

            this.Location = startBounds.Location;
            this.Size = startBounds.Size;
            this.Show();
            this.BringToFront();

            _currentScale = (float)this.Width / _baseSize.Width;

            float relX = (float)(mousePos.X - this.Left) / this.Width;
            float relY = (float)(mousePos.Y - this.Top) / this.Height;
            _anchorRatio = new PointF(relX, relY);

            _isDragging = true;
            _targetScale = DRAG_SCALE_FACTOR;

            _lastMouseMoveTime = DateTime.Now;
            _dragWaitStartPos = Cursor.Position;
            _lastTickMousePos = Cursor.Position;

            _dragHistory.Clear();
            _dragHistory.Enqueue((DateTime.Now, Cursor.Position));

            if (_pinButton != null) _pinButton.Visible = false;
            OnStartDrag(); // Hook for children to hide extra buttons

            _animationTimer.Start();
        }

        protected virtual void OnStartDrag() { }
        protected virtual void OnEndDrag() { }

        // --- Input Handling ---

        protected virtual void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // Initialize _baseSize if it's never been set
                if (_baseSize.Width == 0 || _baseSize.Height == 0) _baseSize = this.Size;

                // Sync _currentScale to the ACTUAL window size right now.
                _currentScale = (float)this.Width / _baseSize.Width;

                _isDragging = true;
                _physicsTimer.Stop();
                _lifeTimer.Stop();
                if (_fadeTimer != null)
                {
                    _fadeTimer.Stop();
                    this.Opacity = 1.0;

                }
                _velocity = PointF.Empty;

                _lastMouseMoveTime = DateTime.Now;
                _dragWaitStartPos = Cursor.Position;
                _lastTickMousePos = Cursor.Position;

                _dragHistory.Clear();
                _dragHistory.Enqueue((DateTime.Now, Cursor.Position));

                Point mousePos = Cursor.Position;

                // The anchor ratio based on the current visual bounds
                float relX = (float)(mousePos.X - this.Left) / this.Width;
                float relY = (float)(mousePos.Y - this.Top) / this.Height;
                _anchorRatio = new PointF(relX, relY);

                _targetScale = DRAG_SCALE_FACTOR;
                if (_pinButton != null) _pinButton.Visible = false;
                OnStartDrag();

                _animationTimer.Start();
            }
        }

        protected virtual void OnMouseMove(object sender, MouseEventArgs e)
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

        protected virtual void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _isDragging)
            {
                HandleMouseRelease();
            }
        }

        // --- Drag & Drop Loop ---

        private void StartFileDrag()
        {
            _isFileDragActive = true;
            _wasFileDragCancelledByMovement = false;
            _fileDragStartPos = Cursor.Position;
            bool dropHandled = false;

            int initialStyle = GetWindowLong(this.Handle, GWL_EXSTYLE);

            try
            {
                SetWindowLong(this.Handle, GWL_EXSTYLE, initialStyle | WS_EX_TRANSPARENT);

                DataObject dataObject = CreateDragDataObject();
                if (dataObject == null) return;

                // Add ID for Shelf interaction
                dataObject.SetData("ClipmageID", this.id);

                this.QueryContinueDrag += OnQueryContinueDrag;
                DragDropEffects result = this.DoDragDrop(dataObject, DragDropEffects.Copy | DragDropEffects.Move);

                if (!_wasFileDragCancelledByMovement && result == DragDropEffects.Move)
                {
                    // Clean up drag state immediately
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
                UpdateWindowPositionForDrag();
            }
        }

        private void UpdateWindowPositionForDrag()
        {
            int newW = (int)(_baseSize.Width * _currentScale);
            int newH = (int)(_baseSize.Height * _currentScale);
            int newX = Cursor.Position.X - (int)(newW * _anchorRatio.X);
            int newY = Cursor.Position.Y - (int)(newH * _anchorRatio.Y);

            float t = (1.0f - _currentScale) / (1.0f - DRAG_SCALE_FACTOR);
            newY -= (int)(((_baseSize.Height * (1 - _anchorRatio.Y) * DRAG_SCALE_FACTOR) + FILE_DRAG_OFFSET) * Math.Min(1.0f, Math.Max(0.0f, t)));

            this.Location = new Point(newX, newY);

            // Keep history updated
            DateTime now = DateTime.Now;
            _dragHistory.Enqueue((now, Cursor.Position));
            while (_dragHistory.Count > 0 && (now - _dragHistory.Peek().Time).TotalMilliseconds > SMOOTHING_RANGE_MS)
            {
                _dragHistory.Dequeue();
            }
        }

        protected void HandleMouseRelease()
        {
            _isDragging = false;
            _targetScale = 1.0f;

            if (WindowController.Shelf != null && WindowController.Shelf.Visible && !WindowController.Shelf.IsDisposed)
            {
                // Now only checks if mouse is actually inside of the shelf bounds.
                // Previously it checked if the window bounds intersected, which made taking a window out of the shelf a little bit harder.
                if (new Rectangle(MousePosition.X,MousePosition.Y,1,1).IntersectsWith(WindowController.Shelf.Bounds))
                {
                    WindowController.Shelf.AddSource(this.id, this.GetSnapshot());
                    this.Hide();
                    _lifeTimer.Stop();
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
                    _velocity = new PointF(Math.Min(pxPerMsX * WINDOW_REFRESH_INTERVAL, MAX_VELOCITY), Math.Min(pxPerMsY * WINDOW_REFRESH_INTERVAL, MAX_VELOCITY));
                }
            }

            double speed = Math.Sqrt(_velocity.X * _velocity.X + _velocity.Y * _velocity.Y);
            if (speed > 1.0) _physicsTimer.Start();
        }

        // --- Timers ---

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

        private void SetupLifeTimer(int seconds)
        {
            _lifeTimer = new System.Windows.Forms.Timer();
            _lifeTimer.Interval = seconds * 1000;
            _lifeTimer.Tick += OnTimerTick;
            _lifeTimer.Start();
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
                    if (moveDist > DRAG_CANCEL_DISTANCE) _dragWaitStartPos = currentPos;
                }

                if ((DateTime.Now - _lastMouseMoveTime).TotalMilliseconds > DRAG_WAIT_MS)
                {
                    StartFileDrag();
                    return;
                }
            }

            // 1. Calculate the OLD Lift (Before we update the scale)
            // We need this to find the "True" Top position of the window, stripping away the offset.
            float denom = (1.0f - DRAG_SCALE_FACTOR);
            float t_prev = 0;
            if (Math.Abs(denom) > 0.001f) t_prev = (1.0f - _currentScale) / denom;
            t_prev = Math.Min(1.0f, Math.Max(0.0f, t_prev));

            int lift_prev = (int)(((_baseSize.Height * (1 - _anchorRatio.Y) * DRAG_SCALE_FACTOR) + FILE_DRAG_OFFSET) * t_prev);

            // 2. Update Scale
            float lerpSpeed = 0.25f;
            _currentScale += (_targetScale - _currentScale) * lerpSpeed;

            if (Math.Abs(_targetScale - _currentScale) < 0.005f)
            {
                _currentScale = _targetScale;
                if (!_isDragging)
                {
                    _animationTimer.Stop();
                    if (_pinButton != null) _pinButton.Visible = true;
                    OnEndDrag();
                }
            }

            // 3. Calculate NEW Dimensions
            int newW = (int)(_baseSize.Width * _currentScale);
            int newH = (int)(_baseSize.Height * _currentScale);
            int newX, newY;

            if (_isDragging)
            {
                // Dragging Logic
                newX = Cursor.Position.X - (int)(newW * _anchorRatio.X);
                newY = Cursor.Position.Y - (int)(newH * _anchorRatio.Y);

                // Division by 0 fix and clamp t between 0 and 1 to avoid weird jumps when scale goes beyond expected range.
                float t = 0;
                if (Math.Abs(denom) > 0.001f) t = (1.0f - _currentScale) / denom;
                t = Math.Min(1.0f, Math.Max(0.0f, t));

                newY -= (int)(((_baseSize.Height * (1 - _anchorRatio.Y) * DRAG_SCALE_FACTOR) + FILE_DRAG_OFFSET) * t);
            }
            else
            {
                // Releasing Logic

                // A. Calculate NEW Lift for the current frame (it will be smaller than lift_prev)
                float t_new = 0;
                if (Math.Abs(denom) > 0.001f) t_new = (1.0f - _currentScale) / denom;
                t_new = Math.Min(1.0f, Math.Max(0.0f, t_new));

                int lift_new = (int)(((_baseSize.Height * (1 - _anchorRatio.Y) * DRAG_SCALE_FACTOR) + FILE_DRAG_OFFSET) * t_new);

                // B. Recover the "True" Top by removing the old lift
                int trueTop = this.Top + lift_prev;

                // C. Calculate the Anchor based on the TRUE position
                Point trueAnchor = new Point(
                    this.Left + (int)(this.Width * _anchorRatio.X),
                    trueTop + (int)(this.Height * _anchorRatio.Y)
                );

                // D. Calculate standard resized position
                newX = trueAnchor.X - (int)(newW * _anchorRatio.X);
                newY = trueAnchor.Y - (int)(newH * _anchorRatio.Y);

                // E. Re-apply the NEW (smaller) lift
                newY -= lift_new;
            }

            this.Bounds = new Rectangle(newX, newY, newW, newH);
        }

        private void OnPhysicsTick(object sender, EventArgs e)
        {
            if (this.IsDisposed || !this.Created) { _physicsTimer.Stop(); return; }
            //_velocity = new PointF(_velocity.X, _velocity.Y + 9); // Apply gravity to vertical velocity
            _velocity.X = Math.Min(_velocity.X, MAX_VELOCITY);
            _velocity.Y = Math.Min(_velocity.Y, MAX_VELOCITY);

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

            if (Math.Abs(_velocity.X) < MIN_VELOCITY && Math.Abs(_velocity.Y) < MIN_VELOCITY) _physicsTimer.Stop();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            _animationTimer.Stop(); _physicsTimer.Stop(); _lifeTimer.Stop();
            StartFadeOut();
        }

        private void StartFadeOut()
        {
            _fadeTimer = new System.Windows.Forms.Timer();
            _fadeTimer.Interval = WINDOW_REFRESH_INTERVAL;
            _fadeTimer.Tick += OnFadeTick;
            _fadeTimer.Start();
        }

        private void OnFadeTick(object sender, EventArgs e)
        {
            if (this.Opacity > 0) this.Opacity -= FADE_OUT_PERCENT;
            else { _fadeTimer.Stop(); _fadeTimer.Dispose(); _lifeTimer.Dispose(); this.Close(); }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_physicsTimer != null) { _physicsTimer.Stop(); _physicsTimer.Dispose(); }
            if (_animationTimer != null) { _animationTimer.Stop(); _animationTimer.Dispose(); }
            if (_fadeTimer != null) { _fadeTimer.Stop(); _fadeTimer.Dispose(); }
            base.OnFormClosed(e);
        }
    }
}