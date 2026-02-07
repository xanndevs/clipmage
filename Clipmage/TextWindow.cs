using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static Clipmage.AppConfig;
using static Clipmage.WindowHelpers;

namespace Clipmage
{
    public class TextWindow : PlaceableWindow
    {
        private Panel _textContainer; // Acts as the viewport for the text
        private TextBox _textBox;
        private ModernScrollBar _scrollBar;
        private bool isEditing = false;
        private Button _editButton;


        private Image _snapshot;


        // 1. Import the SendMessage function from Windows
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, int[] lParam);

        private const int EM_SETTABSTOPS = 0x00CB;

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, int nFlags);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }



        public TextWindow(string text, int durationSeconds) : base(durationSeconds)
        {
            this.WindowState = FormWindowState.Normal;
            this.BackColor = Color.FromArgb(32, 32, 32);

            CalculateSize();

            // Initialize controls
            // Order matters: Setup ScrollBar first so TextBox layout logic can reference it if needed
            SetupEditButton();
            SetupContainer();
            SetupScrollBar(); // Attach scrollbar to container
            SetupTextBox(text); // Create text box and put in container

            // Ensure controls are ordered correctly for clicking
            if (_scrollBar != null) _scrollBar.BringToFront();
            if (_editButton != null) _editButton.BringToFront();
            if (_pinButton != null) _pinButton.BringToFront();

            // Initial calculation
            UpdateScrollParams();

            _snapshot = null; // Initialize snapshot as null until requested to generate
            this.Shown += (s, e) => {
                GetSnapshot();
            };
        }

        private void CalculateSize()
        {
            this.Width = DRAG_WINDOW_MAXIMUM_WIDTH;
            this.Height = DRAG_WINDOW_MAXIMUM_HEIGHT / 3 * 2;

            // Capture base size for scaling animation
            _baseSize = this.Size;

            // Place it on the bottom right on screen initially
            this.Location = new Point(
                (Screen.PrimaryScreen.WorkingArea.Width - this.Width) - 16,
                (Screen.PrimaryScreen.WorkingArea.Height - this.Height) - 16
            );
        }

        private void SetupContainer()
        {
            _textContainer = new Panel();
            _textContainer.BackColor = Color.FromArgb(43, 43, 43);

            int topOffset = PADDING_NORMAL + INTERACTION_BUTTON_SIZE + PADDING_NORMAL;
            _textContainer.Location = new Point(PADDING_NORMAL, topOffset);

            // Full width minus window padding
            _textContainer.Width = this.ClientSize.Width - (PADDING_NORMAL * 2);
            _textContainer.Height = this.ClientSize.Height - topOffset - PADDING_NORMAL;
            _textContainer.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            // Forward mouse wheel to scrollbar
            _textContainer.MouseWheel += (s, e) => { if (_scrollBar != null) _scrollBar.DoScroll(e.Delta); };

            // Handle resize to reflow text
            _textContainer.Resize += (s, e) =>
            {
                if (_textBox != null)
                {
                    _textBox.Width = _textContainer.Width;
                    // Recalculate height because width change might change wrapping
                    _textBox.Height = Math.Max(_textContainer.Height, GetTextHeight(_textBox.Text, _textBox));
                    UpdateScrollParams();
                }

                if (_scrollBar != null)
                {
                    _scrollBar.Location = new Point(_textContainer.Width - _scrollBar.Width, 0);
                    _scrollBar.Height = _textContainer.Height;
                }
            };

            this.Controls.Add(_textContainer);
        }

        private void SetupTextBox(string text)
        {
            _textBox = new TextBox();
            _textBox.Multiline = true;
            _textBox.ReadOnly = false;
            _textBox.Dock = DockStyle.None;
            _textBox.BackColor = _textContainer.BackColor;
            _textBox.ForeColor = Color.FromArgb(250, 250, 250);
            _textBox.BorderStyle = BorderStyle.None;
            _textBox.AcceptsReturn = true;
            _textBox.AcceptsTab = IS_TAB_TO_INDENT;
            SetTabWidth(_textBox, TAB_WIDTH);


            _textBox.Font = new System.Drawing.Font("Segoe UI", FONT_SIZE_NORMAL);

            // Initial Positioning
            _textBox.Location = new Point(0,0);
            _textBox.Width = _textContainer.Width;
            _textBox.Text = text;

            // Calculate height AFTER setting width and text
            _textBox.Height = Math.Max(_textContainer.Height, GetTextHeight(text, _textBox));

            _textBox.Enabled = false; // Start in view-only mode

            _textBox.MouseWheel += (s, e) => { if (_scrollBar != null) _scrollBar.DoScroll(e.Delta); };

            // Update height when text changes (during editing)
            _textBox.TextChanged += (s, e) =>
            {
                _textBox.Height = Math.Max(_textContainer.Height, GetTextHeight(_textBox.Text, _textBox));
                UpdateScrollParams();
            };

            ApplyRoundedRegion(_textContainer, WINDOW_CORNER_RADIUS);


            _textContainer.Controls.Add(_textBox);
            
        }

        private int GetTextHeight(string text, TextBox tb)
        {
            // TextBoxControl flag is critical for accurate internal TextBox measurement
            Size sz = TextRenderer.MeasureText(text, tb.Font, new Size(tb.Width, 0),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

            // Add a little buffer for margins
            return sz.Height + ((int)tb.Font.Size);
        }

        private void SetupScrollBar()
        {
            _scrollBar = new ModernScrollBar();

            // Overlay on the right side of the container
            _scrollBar.Size = new Size(12, _textContainer.Height);
            _scrollBar.Location = new Point(_textContainer.Width - 12, 0);
            _scrollBar.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
            _scrollBar.Visible = false;

            _scrollBar.Scroll += (s, e) =>
            {
                // Move the TextBox inside the container
                if (_textBox != null)
                {
                    _textBox.Top = -_scrollBar.Value;
                }
            };

            _textContainer.Controls.Add(_scrollBar);
            _scrollBar.BringToFront();
        }

        private void UpdateScrollParams()
        {
            if (_textContainer == null || _textBox == null || _scrollBar == null) return;

            int viewHeight = _textContainer.Height;
            int contentHeight = _textBox.Height;

            if (contentHeight > viewHeight)
            {
                _scrollBar.Visible = true;
                _scrollBar.Maximum = contentHeight - viewHeight;
                _scrollBar.LargeChange = viewHeight;
                _scrollBar.BringToFront();
            }
            else
            {
                _scrollBar.Visible = false;
                _scrollBar.Value = 0;
                _textBox.Top = 0;
            }

        }

        private void SetupEditButton()
        {
            _editButton = new Button();
            _editButton.Text = "\ue70f"; // Pencil icon
            _editButton.Size = new Size(INTERACTION_BUTTON_SIZE, INTERACTION_BUTTON_SIZE);
            _editButton.Location = new Point(this.ClientSize.Width - ((INTERACTION_BUTTON_SIZE + PADDING_NORMAL) * 2), PADDING_NORMAL);
            _editButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _editButton.FlatStyle = FlatStyle.Flat;
            _editButton.FlatAppearance.BorderSize = 0;
            _editButton.FlatAppearance.BorderColor = Color.DimGray;
            _editButton.UseVisualStyleBackColor = true;
            _editButton.Cursor = Cursors.Hand;
            _editButton.Font = new Font("Segoe Fluent Icons", 10f, FontStyle.Regular);
            _editButton.ForeColor = Color.LightGray;
            _editButton.BackColor = Color.FromArgb(255, 39, 41, 42);

            _editButton.Click += (s, e) => ToggleEdit();

            ApplyRoundedRegion(_editButton, BUTTON_CORNER_RADIUS, 1, Color.Empty);
            this.Controls.Add(_editButton);
        }

        private void ToggleEdit()
        {
            if (isEditing)
            {
                // Save/Stop Editing
                isEditing = false;
                _textBox.Enabled = false;
                _editButton.Text = "\ue70f";
                //_editButton.BackColor = Color.FromArgb(255, 39, 41, 42);
                _editButton.ForeColor= Color.LightGray;
                _editButton.FlatAppearance.BorderColor = Color.DimGray;
                
                _snapshot = null; // Clear cached snapshot so it regenerates with new text on next
                GetSnapshot();
            }
            else
            {
                // Start Editing

                //_editButton.BackColor = Color.FromArgb(255, 76, 162, 230);
                _editButton.ForeColor = Color.FromArgb(255, 76, 162, 230);
                _editButton.FlatAppearance.BorderColor = Color.FromArgb(255, 48, 117, 171);
                isEditing = true;
                _textBox.Enabled = true;
                _textBox.Focus();
                _editButton.Text = "\ue74e"; // Save icon
            }
        }

        private void SetTabWidth(TextBox textbox, int tabSize)
        {
            // Windows measures tabs in "dialog template units". 
            // Roughly, 4 units = 1 average character width. 
            // So '16' usually equals about 4 characters of space.
            int[] stops = { tabSize * 4 };

            SendMessage(textbox.Handle, EM_SETTABSTOPS, new IntPtr(1), stops);
        }

        // --- Abstract Implementations ---

        public override Image GetSnapshot()
        {
            if (_snapshot == null)
            {
                // FORCE Internal Handle Creation
                // This tricks the control into calculating its layout even if it's not shown yet.
                if (!_textContainer.IsHandleCreated)
                {
                    var h = _textContainer.Handle;
                }

                // Ensure layout logic runs
                _textContainer.PerformLayout();

                // Create the bitmap
                Bitmap bmp = new Bitmap(_textContainer.Width, _textContainer.Height);
                _textContainer.DrawToBitmap(bmp, new Rectangle(0, 0, _textContainer.Width, _textContainer.Height));
                _snapshot = bmp;
            }
            return _snapshot;
        }


        protected override DataObject CreateDragDataObject()
        {
            try
            {
                var dataObject = new DataObject();

                // 1. Text Format (Preferred by Notepad, VS Code, Browser Text Fields)
                string textContent = _textBox.Text;
                if (!string.IsNullOrEmpty(textContent))
                {
                    dataObject.SetData(DataFormats.UnicodeText, textContent);
                }

                // 2. Bitmap Format (Preferred by Image Editors, Paint)
                if (_snapshot != null)
                {
                    dataObject.SetData(DataFormats.Bitmap, _snapshot);
                }

                // 3. FileDrop Format (Preferred by Explorer, Discord Upload, Email Attachments)
                // We need to actually create a physical file for this to work.
                string tempFile = CreateTempTextFile(textContent);

                // FileDrop expects a string array of paths
                var filePaths = new string[] { tempFile };
                dataObject.SetData(DataFormats.FileDrop, filePaths);

                return dataObject;
            }
            catch
            {
                return null;
            }
        }

        // Helper to create the file
        private string CreateTempTextFile(string content)
        {
            // Get the system temp folder
            string tempPath = System.IO.Path.GetTempPath();

            // Create a safe filename. You could also use a snippet of the text as the name.
            // e.g., "Note_20231025.txt"
            string fileName = $"Clipmage_Text_{DateTime.Now.Ticks}.txt";
            string fullPath = System.IO.Path.Combine(tempPath, fileName);

            // Write the text to the file
            System.IO.File.WriteAllText(fullPath, content);

            return fullPath;
        }

        protected override void OnStartDrag()
        {
            if (_editButton != null) _editButton.Visible = false;
        }

        protected override void OnEndDrag()
        {
            if (_editButton != null) _editButton.Visible = true;
        }
    }
}