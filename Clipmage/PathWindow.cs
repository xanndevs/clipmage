using System;
using System.Diagnostics;
using System.DirectoryServices.ActiveDirectory;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using System.Windows.Forms;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Compression;
using Windows.Storage.Streams;
using static Clipmage.AppConfig;
using static Clipmage.WindowHelpers;

namespace Clipmage
{
    public class PathWindow : PlaceableWindow
    {
        //private GhostTextBox _textBox;
        private Panel _container; // The panel that contains folder icon and folder details like size information

        private Panel _iconDisplay;

        private Panel _iconContainer; // The panel that contains folder icon and folder details like size information

        private Panel _detailsContainer; // The panel that contains folder icon and folder details like size information

        public string _filePath { get; private set; }
        private string _fileSize = "0.0"; // Placeholder until we implement file size retrieval logic
        private string _fileDateModified = new DateTime(0).ToShortDateString(); // Placeholder until we implement file size retrieval logic
        private string _fileDisplayName = ""; // Placeholder until we implement file size retrieval logic
        private string _fileTypeName = ""; // Placeholder until we implement file size retrieval logic
        //private ModernScrollBar _scrollBar;
        private bool isEditing = false;
        private Button _editButton;
        private Panel _titleContainer;
        private GhostTextBox _titleText;
        private Label _titleLength;


        private Image _snapshot;

        protected Point _initialPosition;

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



        public PathWindow(string path, int durationSeconds) : base(durationSeconds)
        {
            _filePath = path;
            
            WindowHelpers.GetDetailsFromPath(_filePath, out _fileDisplayName, out _fileTypeName, out _fileSize, out _fileDateModified);

            this.WindowState = FormWindowState.Normal;
            this.BackColor = Color.FromArgb(32, 32, 32);

            CalculateSize();

            // Initialize controls
            // Order matters: Setup ScrollBar first so TextBox layout logic can reference it if needed
            SetupEditButton();
            SetupTitleContainer();
            SetupTitleText();

            _titleText.Text = SHOW_FILE_EXTENSIONS ? Path.GetFileName(_filePath) : Path.GetFileNameWithoutExtension(_filePath);

            SetupContainer();
            //SetupScrollBar(); // Attach scrollbar to container
            //SetupTextBox(text); // Create text box and put in container

            // Ensure controls are ordered correctly for clicking
            //if (_scrollBar != null) _scrollBar.BringToFront();
            if (_editButton != null) _editButton.BringToFront();
            if (_pinButton != null) _pinButton.BringToFront();

            // Initial calculation
            //UpdateScrollParams();

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
            _container = new Panel();
            _container.BackColor = Color.FromArgb(43, 43, 43);

            int topOffset = PADDING_NORMAL + INTERACTION_BUTTON_SIZE + PADDING_NORMAL;
            _container.Location = new Point(PADDING_NORMAL, topOffset);

            // Full width minus window padding
            _container.Width = this.ClientSize.Width - (PADDING_NORMAL * 2);
            _container.Height = this.ClientSize.Height - topOffset - PADDING_NORMAL;
            _container.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            // Forward mouse wheel to scrollbar
            //_container.MouseWheel += (s, e) => { if (_scrollBar != null) _scrollBar.DoScroll(e.Delta); };

            // Handle resize to reflow text
            //_container.Resize += (s, e) =>
            //{
            //    if (_textBox != null)
            //    {
            //        _textBox.Width = _container.Width;
            //        // Recalculate height because width change might change wrapping
            //        _textBox.Height = Math.Max(_container.Height, GetTextHeight(_textBox.Text, _textBox));
            //        UpdateScrollParams();
            //    }

            //    if (_scrollBar != null)
            //    {
            //        _scrollBar.Location = new Point(_container.Width - _scrollBar.Width, 0);
            //        _scrollBar.Height = _container.Height;
            //    }
            //};

            ApplyPassthrough(_container);
    
            ApplyRoundedRegion(_container, BUTTON_CORNER_RADIUS, 0, this.BackColor);
            this.Controls.Add(_container);

            SetupIconPanel();
            SetupDetailsPanel();


        }


        private void SetupIconPanel()
        {
            _iconContainer = new Panel();
            _iconContainer.BackColor = Color.FromArgb(60,60,60);

            _iconContainer.Location = new Point(PADDING_NORMAL, PADDING_NORMAL);

            // Full width minus window padding
            _iconContainer.Width = 110;
            _iconContainer.Height = _container.ClientSize.Height - (PADDING_NORMAL * 2);
            _iconContainer.Anchor = AnchorStyles.Top | AnchorStyles.Left;

            Bitmap bmp = WindowHelpers.GetIconFromPath(@$"{_filePath}").ToBitmap();

            //_iconContainer.BackgroundImage = bmp;
            //_iconContainer.BackgroundImageLayout = ImageLayout.Zoom;


            _iconDisplay = new Panel();
            _iconDisplay.BackColor = Color.Transparent;
            _iconDisplay.BackgroundImage = bmp;
            _iconDisplay.BackgroundImageLayout = ImageLayout.Zoom;
            
            _iconDisplay.Location = new Point(PADDING_NORMAL, PADDING_NORMAL);
            _iconDisplay.Size = new Size(_iconContainer.ClientSize.Width - (PADDING_NORMAL * 2), _iconContainer.ClientSize.Height - (PADDING_NORMAL * 2));
            
            ApplyPassthrough(_iconDisplay);
            ApplyPassthrough(_iconContainer);

            _iconContainer.Controls.Add(_iconDisplay);
            _container.Controls.Add(_iconContainer);

            ApplyRoundedRegion(_iconContainer, BUTTON_CORNER_RADIUS, 0, this.BackColor);

        }

        private void SetupDetailsPanel()
        {
            _detailsContainer = new Panel();
            _detailsContainer.BackColor = Color.Transparent;
            _detailsContainer.ForeColor = Color.FromArgb(250, 250, 250);

            _detailsContainer.Location = new Point(_iconContainer.Right + PADDING_NORMAL, 0);

            // Full width minus window padding
            _detailsContainer.Width = _container.ClientSize.Width - _iconContainer.ClientRectangle.Right - PADDING_NORMAL;
            _detailsContainer.Height = _container.ClientSize.Height;
            _detailsContainer.Anchor = AnchorStyles.Top | AnchorStyles.Left;

            int y = SetupSizeLabels(PADDING_LARGE);
            y = SetupDateLabels(y + PADDING_SMALL);

            SetupButtonsPanel();

            _container.Controls.Add(_detailsContainer);

            ApplyPassthrough(_detailsContainer);

        }

        private int SetupSizeLabels(int y)
        {
            Panel panel = new Panel();

            Label sizeIcon = new Label();
            sizeIcon.AutoSize = false;
            sizeIcon.Font = new Font("Segoe Fluent Icons", 13, FontStyle.Bold);
            sizeIcon.Text = "\ue7b8";
            sizeIcon.ForeColor = ForeColor = Color.LightGray;
            sizeIcon.Size = TextRenderer.MeasureText("\uf127", new Font("Segoe Fluent Icons", 15, FontStyle.Bold));
            sizeIcon.Location = new Point(0, 1);

            panel.Size = new Size(_detailsContainer.ClientSize.Width, sizeIcon.Size.Height);
            panel.Location = new Point(0, y);
            panel.Anchor = AnchorStyles.Left | AnchorStyles.Right;


            Label size = new Label();
            size.Font = new Font("Segoe UI", FONT_SIZE_SMALL, FontStyle.Bold);
            size.Text = $"{_fileSize}";
            size.Size = TextRenderer.MeasureText(size.Text, size.Font);
            size.Location = new Point(sizeIcon.Right, 0);

            ApplyPassthrough(sizeIcon);
            ApplyPassthrough(size);
            ApplyPassthrough(panel);

            panel.Controls.Add(sizeIcon);
            panel.Controls.Add(size);
            _detailsContainer.Controls.Add(panel);

            return panel.Bottom;
        }

        private int SetupDateLabels(int y)
        {
            Panel panel = new Panel();

            Label dateModifiedIcon = new Label();
            dateModifiedIcon.AutoSize = false;
            dateModifiedIcon.Font = new Font("Segoe Fluent Icons", 14, FontStyle.Regular);
            dateModifiedIcon.Text = "\ue787";
            dateModifiedIcon.ForeColor = Color.LightGray;
            dateModifiedIcon.Size = TextRenderer.MeasureText("\uf127", dateModifiedIcon.Font);
            dateModifiedIcon.Location = new Point(0, 0);

            panel.Size = new Size(_detailsContainer.ClientSize.Width, dateModifiedIcon.Size.Height);
            panel.Location = new Point(0, y);
            panel.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            Label dateModified = new Label();
            dateModified.Font = new Font("Segoe UI", FONT_SIZE_SMALL, FontStyle.Bold);
            dateModified.Text = _fileDateModified;
            dateModified.Size = TextRenderer.MeasureText(dateModified.Text, dateModified.Font);
            dateModified.Location = new Point(dateModifiedIcon.Right, 0);

            ApplyPassthrough(dateModifiedIcon);
            ApplyPassthrough(dateModified);
            ApplyPassthrough(panel);

            dateModifiedIcon.MouseDown += OnMouseDown;
            dateModifiedIcon.MouseMove += OnMouseMove;
            dateModifiedIcon.MouseUp += OnMouseUp;

            dateModified.MouseDown += OnMouseDown;
            dateModified.MouseMove += OnMouseMove;
            dateModified.MouseUp += OnMouseUp;

            _detailsContainer.Controls.Add(panel);
            panel.Controls.Add(dateModifiedIcon);
            panel.Controls.Add(dateModified);

            return panel.Bottom;
        }

        private void SetupButtonsPanel()
        {
            Panel panel = new Panel();
            panel.Size = new Size(_detailsContainer.ClientSize.Width - PADDING_NORMAL - PADDING_NORMAL, INTERACTION_BUTTON_SIZE );
            panel.Location = new Point(0, _detailsContainer.Bottom - panel.Size.Height - PADDING_NORMAL);
            panel.Anchor = AnchorStyles.Left | AnchorStyles.Right;



            Button deleteButton = new Button();

            deleteButton.Text = "\ue74d"; // Pencil icon
            deleteButton.Size = new Size(INTERACTION_BUTTON_SIZE, INTERACTION_BUTTON_SIZE);
            deleteButton.Location = new Point(0, 0);
            deleteButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            deleteButton.FlatStyle = FlatStyle.Flat;
            deleteButton.FlatAppearance.BorderSize = 0;
            deleteButton.FlatAppearance.BorderColor = Color.IndianRed;
            deleteButton.UseVisualStyleBackColor = true;
            deleteButton.Cursor = Cursors.Hand;
            deleteButton.Font = new Font("Segoe Fluent Icons", 11f, FontStyle.Regular);
            deleteButton.ForeColor = Color.IndianRed;
            deleteButton.BackColor = Color.FromArgb(255, 39, 41, 42);

            //delete button should replace the right side of the panel
            // with a delete modal so I dont accidently fuck people's computers up
            // :thumbsupemoji: like a boss
            //this is a dummy comment I dont know why I am writing this one
            //instead of actually working on the modal itself

            //button.Click += (s, e) => ToggleEdit();

            ApplyRoundedRegion(deleteButton, BUTTON_CORNER_RADIUS, 1, Color.Empty);

            panel.Controls.Add(deleteButton);


            Button compressButton = new Button();
            compressButton.TextAlign = ContentAlignment.MiddleLeft;
            compressButton.Padding = new Padding(PADDING_TINY, 0, 0, 0);
            compressButton.Font = new Font("Segoe Fluent Icons", 13f, FontStyle.Regular);
            compressButton.Text += "\uf012"; // Pencil icon
            compressButton.Size = new Size((panel.Width - deleteButton.Width) - (PADDING_TINY * 2), INTERACTION_BUTTON_SIZE);
            compressButton.Location = new Point(deleteButton.Right + PADDING_SMALL , 0);
            compressButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            compressButton.FlatStyle = FlatStyle.Flat;
            compressButton.FlatAppearance.BorderSize = 0;
            compressButton.FlatAppearance.BorderColor = Color.DimGray;
            compressButton.UseVisualStyleBackColor = true;
            compressButton.Cursor = Cursors.Hand;
            compressButton.ForeColor = Color.LightGray;
            compressButton.BackColor = Color.FromArgb(255, 39, 41, 42);

            // And also this one supposedly compress the selected path and close this window
            // replacing the old window with this new window that contains the information for the
            // compressed file... place the file in temp future me... thanks.


            Label compressLabel = new Label();
            compressLabel.Text = "Compress";
            compressLabel.Font = new Font("Segoe UI", FONT_SIZE_SMALL, FontStyle.Regular);
            compressLabel.AutoSize = true;
            compressLabel.Location = new Point(TextRenderer.MeasureText(compressButton.Text, compressButton.Font).Width + (PADDING_TINY + PADDING_SMALL), ((compressButton.ClientSize.Height - compressLabel.ClientSize.Height) / 2) + PADDING_TINY);
            compressLabel.ForeColor = Color.LightGray;
            compressButton.Controls.Add(compressLabel);
            //button.Click += (s, e) => ToggleEdit();

            compressButton.Click += CompressAndCreatePreview;
            compressLabel.Click += CompressAndCreatePreview;
            

            ApplyRoundedRegion(compressButton, BUTTON_CORNER_RADIUS, 1, Color.Empty);

            panel.Controls.Add(compressButton);
            _detailsContainer.Controls.Add(panel);


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

        private void SetupTitleContainer()
        {
            _titleContainer = new Panel();
            _titleContainer.BackColor = Color.FromArgb(43, 43, 43);
            _titleContainer.Location = new Point(PADDING_NORMAL, PADDING_NORMAL);
            _titleContainer.Width = this.ClientSize.Width - PADDING_NORMAL - PADDING_NORMAL - (INTERACTION_BUTTON_SIZE + PADDING_NORMAL) * 2;
            _titleContainer.Height = INTERACTION_BUTTON_SIZE;
            _titleContainer.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            ApplyRoundedRegion(_titleContainer, WINDOW_CORNER_RADIUS, 1, _titleContainer.BackColor);
            this.Controls.Add(_titleContainer);
        }

        private void SetupTitleText()
        {

            //title text
            _titleText = new GhostTextBox();
            _titleText.Enabled = false;
            _titleText.Multiline = false;
            _titleText.MaxLength = 100;
            _titleText.PlaceholderText = "Clipmage Text File";
            _titleText.Font = new System.Drawing.Font("Segoe UI", FONT_SIZE_SMALL);
            Size titleSize = TextRenderer.MeasureText(" ", _titleText.Font);
            _titleText.Width = _titleContainer.ClientSize.Width - (PADDING_NORMAL * 2);
            _titleText.Location = new Point(PADDING_NORMAL, (_titleContainer.ClientSize.Height - titleSize.Height) / 2);
            //_titleText.Dock = DockStyle.Left | DockStyle.Right;
            _titleText.BackColor = _titleContainer.BackColor;
            _titleText.ForeColor = Color.FromArgb(250, 250, 250);
            _titleText.BorderStyle = BorderStyle.None;

            _titleLength = new Label();
            _titleLength.AutoSize = true;
            _titleLength.ForeColor = Color.FromArgb(150, 150, 150);
            _titleLength.Font = new Font("Segoe UI", FONT_SIZE_TINY);
            _titleLength.BackColor = Color.Empty;
            _titleLength.Visible = false;

            CalculateRemainingSize();

            //events
            ApplyPassthrough(_titleContainer);

            _titleText.TextChanged += (s, e) => {

                CalculateRemainingSize();

            };

            _titleContainer.Controls.Add(_titleText);
            _titleContainer.Controls.Add(_titleLength);
            _titleLength.BringToFront();
        }

        private void CompressAndCreatePreview(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_filePath))
                return;

            bool isFile = File.Exists(_filePath);
            bool isDirectory = Directory.Exists(_filePath);

            if (!isFile && !isDirectory)
                throw new Exception("Path does not exist.");

            string tempDir = Path.Combine(Path.GetTempPath(), "Clipmage", "Compressed File");
            Directory.CreateDirectory(tempDir);

            string zipPath = GetUniqueZipPath(tempDir, _titleText.Text);

            if (isDirectory)
            {
                ZipFile.CreateFromDirectory(_filePath, zipPath);
            }
            else if (isFile)
            {
                using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    archive.CreateEntryFromFile(_filePath, Path.GetFileName(_filePath));
                }
            }
            WindowController.DisplayFolderWindow(zipPath);  
            if (CLOSE_AFTER_COMPRESS) StartFadeOut();
            return;
            //return WindowController.DisplayFolderWindow(zipPath);


        }

        private string GetUniqueZipPath(string directory, string baseName)
        {
            string path = Path.Combine(directory, baseName + ".zip");

            if (!File.Exists(path))
                return path;

            int counter = 1;

            while (true)
            {
                string newPath = Path.Combine(directory, $"{baseName} ({counter}).zip");

                if (!File.Exists(newPath))
                    return newPath;

                counter++;
            }
        }

        private void CalculateRemainingSize()
        {
            _titleLength.Text = $"{_titleText.MaxLength - _titleText.Text.Length}";
            _titleLength.Size = TextRenderer.MeasureText(_titleLength.Text, _titleLength.Font);
            _titleLength.Location = new Point(
                _titleContainer.ClientSize.Width - _titleLength.Width,
                _titleContainer.ClientSize.Height - _titleLength.Height
            );
            ApplyCroppedRegion(_titleLength, new Point(1, 1), new Point(_titleLength.Width - 2, _titleLength.Height - 2));
        }

        private void ToggleEdit()
        {
            if (isEditing)
            {
                // Save/Stop Editing
                isEditing = false;

                //Length Couunter Logic
                _titleLength.Visible = false;
                
                _editButton.Text = "\ue70f";
                //_editButton.BackColor = Color.FromArgb(255, 39, 41, 42);
                _editButton.ForeColor = Color.LightGray;
                _editButton.FlatAppearance.BorderColor = Color.DimGray;
                this.Focus(); // Release focus from the text box so it does not keep blinking
                _snapshot = null; // Clear cached snapshot so it regenerates with new text on next
                GetSnapshot();

            }
            else
            {
                // Start Editing

                //_editButton.BackColor = Color.FromArgb(255, 76, 162, 230);
                _editButton.ForeColor = Color.FromArgb(255, 76, 162, 230);
                _editButton.FlatAppearance.BorderColor = Color.FromArgb(255, 48, 117, 171);
                
                //Length Couunter Logic
                _titleLength.Visible = true;
                _titleText.Enabled = true;
   
  
                _editButton.ForeColor = Color.FromArgb(255, 76, 162, 230);
                _editButton.FlatAppearance.BorderColor = Color.FromArgb(255, 48, 117, 171);
                isEditing = true;
                _titleText.Enabled = true;
                _titleText.Focus();
                _editButton.Text = "\ue74e"; // Save icon
            }
        }

        public override Image GetSnapshot()
        {
            if (_snapshot == null)
            {
                // FORCE Internal Handle Creation
                // This tricks the control into calculating its layout even if it's not shown yet.
                if (!_container.IsHandleCreated)
                {
                    var h = _container.Handle;
                }

                // Ensure layout logic runs
                _container.PerformLayout();

                // Create the bitmap
                Bitmap bmp = new Bitmap(_container.Width, _container.Height);
                _container.DrawToBitmap(bmp, new Rectangle(0, 0, _container.Width, _container.Height));
                _snapshot = bmp;
            }
            return _snapshot;
        }


        protected override DataObject CreateDragDataObject()
        {
            try
            {
                var dataObject = new DataObject();

                // I choose to rename the file at this stage because otherwise
                // if the user does not want to drag the file into another place
                // the renaming opereation would happen right after saving the name
                //RenameFile();
                //Nevermind I added it to the last stage of the file drop code in placablewindow
                //because this one renamed files even if we dont release them

                // 1. Text Format (Preferred by Notepad, VS Code, Browser Text Fields)
                string path = Path.GetFullPath(_filePath);



                if (!string.IsNullOrEmpty(path))
                {
                    dataObject.SetData(DataFormats.SymbolicLink, path);
                }

                // 2. Bitmap Format (Preferred by Image Editors, Paint)
                if (_snapshot != null)
                {
                    dataObject.SetData(DataFormats.Bitmap, _snapshot);
                }

                //// 3. FileDrop Format (Preferred by Explorer, Discord Upload, Email Attachments)
                //// We need to actually create a physical file for this to work.
                //string tempFile = CreateTempTextFile(textContent);

                // FileDrop expects a string array of paths
                var filePaths = new string[] { _filePath };
                dataObject.SetData(DataFormats.FileDrop, filePaths);

                return dataObject;
            }
            catch
            {
                return null;
            }
        }
        protected override void RenameFile()
        {
            string oldPath = _filePath;
            string directory = Path.GetDirectoryName(oldPath);

            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("Invalid directory.");

            string newPath;

            if (File.Exists(oldPath))
            {
                // Determine new file name
                string extension = Path.GetExtension(oldPath);
                string baseName = _titleText.Text;
                if(SHOW_FILE_EXTENSIONS)
                    newPath = Path.Combine(directory, baseName);
                else
                    newPath = Path.Combine(directory, baseName + extension);

                if (!File.Exists(newPath))
                    File.Move(oldPath, newPath);
                // else: skip move if already exists
            }
            else if (Directory.Exists(oldPath))
            {
                // Directories don't have extensions
                newPath = Path.Combine(directory, _titleText.Text);

                if (!Directory.Exists(newPath))
                    Directory.Move(oldPath, newPath);
            }
            else
            {
                throw new FileNotFoundException("Source path not found.", oldPath);
            }

            _filePath = newPath;
        }

        protected override void OnStartDrag()
        {
            if (_editButton != null) _editButton.Visible = false;
        }

        protected override void OnEndDrag()
        {
            if (_editButton != null) _editButton.Visible = true;
        }

        private void ApplyPassthrough(Control control)
        {
            control.MouseDown += OnMouseDown;
            control.MouseMove += OnMouseMove;
            control.MouseUp += OnMouseUp;
        }
    }
}