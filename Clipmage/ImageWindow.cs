using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using Windows.System;
using Windows.Storage;
using Windows.ApplicationModel.DataTransfer;
using static Clipmage.AppConfig;
using static Clipmage.WindowHelpers;

namespace Clipmage
{
    /// <summary>
    /// Specialized class for displaying images. Inherits all physics/drag behavior from PlaceableWindow.
    /// </summary>
    public class ImageWindow : PlaceableWindow
    {
        private Image _img;
        private Button _editButton;

        public ImageWindow(Image screenshotToDisplay, int durationSeconds)
            : base(durationSeconds)
        {
            _img = screenshotToDisplay;

            // Set visuals
            this.BackgroundImage = _img;
            this.BackgroundImageLayout = ImageLayout.Zoom;
            this.WindowState = FormWindowState.Normal;

            // Calculate Aspect Ratio Size
            CalculateSize();

            // Image Specific Controls
            SetupEditButton();
        }

        private void CalculateSize()
        {
            if ((double)_img.Height / _img.Width > 1)
            {
                this.Height = DRAG_WINDOW_MAXIMUM_HEIGHT;
                this.Width = (_img.Width * DRAG_WINDOW_MAXIMUM_HEIGHT) / _img.Height;
            }
            else
            {
                this.Width = DRAG_WINDOW_MAXIMUM_WIDTH;
                this.Height = (_img.Height * DRAG_WINDOW_MAXIMUM_WIDTH) / _img.Width;
            }

            // Capture base size for scaling animation
            _baseSize = this.Size;

            // Place it on the bottom right on screen initially
            this.Location = new Point(
                (Screen.PrimaryScreen.WorkingArea.Width - this.Width) - 16,
                (Screen.PrimaryScreen.WorkingArea.Height - this.Height) - 16
            );
        }

        // --- Implement Abstract Methods ---

        public override Image GetSnapshot()
        {
            return _img;
        }

        protected override DataObject CreateDragDataObject()
        {
            // Create temp file for explorer drop
            string tempPath = Path.Combine(Path.GetTempPath(), $"Clipmage_{DateTime.Now.Ticks}.png");
            try
            {
                _img.Save(tempPath, ImageFormat.Png);
                var dataObject = new DataObject();
                dataObject.SetData(DataFormats.FileDrop, new string[] { tempPath });
                dataObject.SetData(DataFormats.Bitmap, _img);
                return dataObject;
            }
            catch
            {
                return null;
            }
        }

        // --- Image Specific Controls ---

        private void SetupEditButton()
        {
            _editButton = new Button();
            _editButton.Text = "\ue70f";

            _editButton.Size = new Size(INTERACTION_BUTTON_SIZE, INTERACTION_BUTTON_SIZE);
            _editButton.Location = new Point(this.ClientSize.Width - ((INTERACTION_BUTTON_SIZE + 8) * 2), 8);
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

        protected override void OnStartDrag()
        {
            // Hide edit button when dragging starts
            if (_editButton != null) _editButton.Visible = false;
        }

        protected override void OnEndDrag()
        {
            // Restore edit button when dragging ends (and not pinned/cancelled)
            if (_editButton != null) _editButton.Visible = true;
        }

    }
}