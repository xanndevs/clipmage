using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;       // Ensure this is present
using System.Linq;
using System.Reflection;
using System.Security.Policy;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms; // Ensure this is present
using Clipmage.Properties;
using static Clipmage.AppConfig;

namespace Clipmage
{
    struct ReleaseInfo
    {
        public Version Version { get; set; }
        public string DownloadUrl { get; set; }
        public string ReleaseUrl { get; set; }
    }

    public class Updater : Form
    {
        private static Updater? _instance;
        public static readonly Version currentVersion = typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
        private static ReleaseInfo releaseInfo;
        private LinkLabel _releaseLabel;
        private Label _newVersionLabel;
        private Label _installPrompt;

        private ProgressBar _progressBar;
        private Panel _progressBarPanel;

        private RoundedButton _cancelButton;
        private CancellationTokenSource _cts;
        private int _cancelWidth;
        private Button _updateButton;
        private bool _isUpdating = false;

        public static Updater Instance
        {
            get
            {
                // Fix: Check if instance is null OR if it has been disposed (closed)
                if (_instance == null || _instance.IsDisposed)
                {
                    _instance = new Updater();
                }
                return _instance;
            }
        }

        private Updater()
        {
            // Private constructor to prevent instantiation
            InitializeComponent();

            SetupNewVersionLabel();
            SetupReleaseLabel();
            SetupInstallPrompt();

            SetupUpdateButton();
            SetupCancelButton();

            SetupProgressBar();
        }



        private void InitializeComponent()
        {
            this.Text = Properties.Resources.AppName;
            this.ShowInTaskbar = true;
            this.Icon = Properties.Resources.AppIcon;
            this.ShowIcon = true;
            this.Size = new Size(430, 175);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(20, 20, 20);
            this.ForeColor = Color.White;
            this.TopMost = true;
            this.FormBorderStyle = FormBorderStyle.FixedToolWindow;
            this.Padding = Padding.Empty;

            this.Controls.Add(_releaseLabel);

            // Fix: Prevent the form from being destroyed when the user closes it
            this.FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    this.Hide();
                }
            };

        }

        private void SetupNewVersionLabel()
        {
            _newVersionLabel = new Label();

            _newVersionLabel.Text = "New version of "+ Properties.Resources.AppName +" is available!";
            _newVersionLabel.AutoSize = true;
            _newVersionLabel.Location = new Point(PADDING_NORMAL, PADDING_NORMAL);
            _newVersionLabel.Font = new Font("Segoe UI", FONT_SIZE_SMALL, FontStyle.Bold);
            this.Controls.Add(_newVersionLabel);
        }

        private void SetupReleaseLabel()
        {
            _releaseLabel = new LinkLabel();

            _releaseLabel.Text = "Click here to view the release notes.";
            _releaseLabel.AutoSize = true;
            _releaseLabel.Location = new Point(PADDING_NORMAL, ((PADDING_NORMAL + FONT_SIZE_SMALL) * 1 ) + PADDING_NORMAL);
            _releaseLabel.Font = new Font("Segoe UI", FONT_SIZE_SMALL, FontStyle.Regular);
            _releaseLabel.LinkColor = Color.FromArgb(100, 180, 255);
            _releaseLabel.ActiveLinkColor = Color.FromArgb(130, 194, 255);
            _releaseLabel.VisitedLinkColor = Color.FromArgb(100, 180, 255);
            _releaseLabel.LinkBehavior = LinkBehavior.HoverUnderline;

            _releaseLabel.Click += (s, e) =>
            {
                //Open the release URL in the default browser
                if (!string.IsNullOrEmpty(releaseInfo.ReleaseUrl))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = releaseInfo.ReleaseUrl,
                        UseShellExecute = true
                    });
                }
            };

            this.Controls.Add(_releaseLabel);
        }

        private void SetupInstallPrompt()
        {
            _installPrompt = new Label();

            _installPrompt.Text = "Do you want to update to the newest version?";
            _installPrompt.AutoSize = true;
            _installPrompt.Location = new Point(PADDING_NORMAL, ((PADDING_NORMAL + FONT_SIZE_SMALL) * 2) + PADDING_NORMAL);
            _installPrompt.Font = new Font("Segoe UI", FONT_SIZE_SMALL, FontStyle.Regular);
            this.Controls.Add(_installPrompt);
        }

        private void SetupProgressBar()
        {   
            _progressBarPanel = new Panel();
            _progressBarPanel.BorderStyle = BorderStyle.None;
            _progressBarPanel.Size = new Size(
                    this.ClientSize.Width - (PADDING_NORMAL * 2) - (PADDING_NORMAL + _updateButton.Width + PADDING_NORMAL + _cancelButton.Width),
                    DIALOG_BUTTON_SIZE
            );

            _progressBarPanel.Location = new Point( PADDING_NORMAL, this.ClientSize.Height - _progressBarPanel.Height - PADDING_NORMAL);
            WindowHelpers.ApplyRoundedRegion(_progressBarPanel, BUTTON_CORNER_RADIUS, 1, Color.DimGray);
            _progressBarPanel.BackColor = Color.DimGray;
            _progressBar = new ProgressBar();
            
            _progressBar.Dock = DockStyle.None;
            _progressBar.Size = new Size(_progressBarPanel.Width - 4, _progressBarPanel.Height - 4);
            _progressBar.Location = new Point(2, 2);
            _progressBar.Style = ProgressBarStyle.Continuous;

            _progressBarPanel.Controls.Add(_progressBar);
            WindowHelpers.ApplyRoundedRegion(_progressBar, BUTTON_CORNER_RADIUS - 1);

            this.Controls.Add( _progressBarPanel );

            _progressBarPanel.Hide();

        }
        private void SetupUpdateButton()
        {
            _updateButton = new RoundedButton();

            _updateButton.Text = "Update Now";
            _updateButton.Font = new Font("Segoe UI", FONT_SIZE_SMALL, FontStyle.Regular);

            _updateButton.BackColor = this.BackColor;
            _updateButton.Height = DIALOG_BUTTON_SIZE;

            _updateButton.Location = new Point(this.ClientSize.Width - _updateButton.Width - PADDING_NORMAL, this.ClientSize.Height - _updateButton.Height - PADDING_NORMAL);

            this.Controls.Add(_updateButton);

            _updateButton.Click += (s, e) =>
            {
                if (!_isUpdating)
            {
                BeginUpdateProcess();
                }
                
                //this.Hide();
            };


        }


        private void SetupCancelButton()
        {
            _cancelButton = new RoundedButton();

            _cancelButton.Text = "Close";
            _cancelButton.Font = new Font("Segoe UI", FONT_SIZE_SMALL, FontStyle.Regular);

            _cancelButton.isAutoResizing = false;
            _cancelButton.Height = DIALOG_BUTTON_SIZE;
            _cancelButton.BackColor = this.BackColor;
            
            _cancelButton.Location = new Point(_updateButton.Location.X - _cancelButton.Width - PADDING_NORMAL, this.ClientSize.Height - _cancelButton.Height - PADDING_NORMAL);
            _cancelWidth = _cancelButton.Width;
            this.Controls.Add(_cancelButton);

            _cancelButton.Click += (s, e) =>
            {
                if(_cts != null)
                {
                    _cts.Cancel();
                    _progressBar.Value = 0;
                    _progressBarPanel.Hide();
                    _cancelButton.Text = "Close";
                }
                    
                else this.Hide();
            };




        }


        private async void BeginUpdateProcess()
        {
            // 1. Validation
            if (string.IsNullOrEmpty(releaseInfo.DownloadUrl))
            {
                MessageBox.Show("Invalid download URL.");
                return;
        }

            // 2. Setup UI for Download State
            _cts = new CancellationTokenSource();
            _progressBar.Value = 0;
            _progressBarPanel.Show();
            _isUpdating = true;
            _updateButton.ForeColor = Color.Gray;
            _progressBarPanel.Visible = true;       // Make sure it's visible
            _cancelButton.Enabled = true;      // Ensure cancel is clickable
            _cancelButton.Text = "Cancel";

            try
        {
                // 3. Define Progress Handler
                var progressHandler = new Progress<double>(percent =>
            {
                    // Update UI on the main thread
                    _progressBar.Value = (int)percent;

                    //_updateButton.Text = $"Downloading... {percent:0}%";
                });

                // 4. Start Download (Await it)
                // Ensure your DownloadFileToTempAsync accepts the CancellationToken
                string downloadedPath = await DownloadFileToTempAsync(releaseInfo.DownloadUrl, progressHandler, _cts.Token);

                // 5. If we get here, download is 100% complete and successful
                PerformUpdate(downloadedPath);
            }
            catch (OperationCanceledException)
            {
                // 6. Handle Cancellation (User clicked Cancel)
                // Reset UI back to "Ready" state
                _progressBarPanel.Visible = false;
                _updateButton.Text = "Update Now";
                _updateButton.ForeColor = Color.White;
                _isUpdating = false;
                _updateButton.Enabled = true;
                //MessageBox.Show("Update cancelled by user.");
            }
            catch (Exception ex)
            {
                // 7. Handle Errors (Network fail, disk full, etc.)
                _progressBarPanel.Visible = false;
                _updateButton.Text = "Update Now";
                _updateButton.ForeColor = Color.White;
                _isUpdating = false;
                _updateButton.Enabled = true;
                MessageBox.Show($"Update failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                // 8. Clean up the token source
                _cts?.Dispose();
                _cts = null;
            }
        }

        public static async Task<string> DownloadFileToTempAsync(string fileUrl, IProgress<double> progress, CancellationToken token)
        {
            // 1. Create a unique temporary file path 
            // We use a GUID to ensure the name doesn't conflict with existing files
            string tempFolder = Path.GetTempPath();
            string fileName = $"{Guid.NewGuid()}.exe";
            string filePath = Path.Combine(tempFolder, fileName);

            // 2. Initialize HttpClient (Best practice: use a shared instance in real apps)
            using (HttpClient client = new HttpClient())
            {
                try
                {
                    // Pass 'token' here. If cancelled while connecting, it throws immediately.
                    using (HttpResponseMessage response = await client.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, token))
                    {
                        response.EnsureSuccessStatusCode();
                        long? totalBytes = response.Content.Headers.ContentLength;

                        using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                        using (FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            byte[] buffer = new byte[8192];
                            long totalRead = 0;
                            int bytesRead;

                            while (true)
                            {
                                // Check if cancellation was requested before reading
                                token.ThrowIfCancellationRequested();

                                // Pass token to ReadAsync so it stops even if waiting for data
                                bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, token);

                                if (bytesRead == 0) break;

                                await fileStream.WriteAsync(buffer, 0, bytesRead, token);

                                totalRead += bytesRead;
                                if (totalBytes.HasValue)
                                {
                                    progress?.Report((double)totalRead / totalBytes.Value * 100);
                                }
                            }
                        }
                    }
                    return filePath;
                }
                catch (OperationCanceledException)
                {
                    // If cancelled, delete the partial file so we don't leave junk
                    if (File.Exists(filePath)) File.Delete(filePath);
                    throw; // Rethrow so the caller knows it was cancelled
                }
            }
        }

        static void PerformUpdate(string newVersionPath)
        {
            try
            {
                // 1. Get the path of the current running executable
                string currentExePath = Process.GetCurrentProcess().MainModule.FileName;

                // Get the Process ID (PID) to ensure we wait for this specific instance to close
                int currentPid = Process.GetCurrentProcess().Id;

                Console.WriteLine("Preparing to update...");
                Console.WriteLine($"Current File: {currentExePath}");
                Console.WriteLine($"New File:     {newVersionPath}");

                // 2. Build the PowerShell command
                // Logic:
                // a. Wait-Process: Waits for the C# app to fully exit so the file lock is released.
                // b. Move-Item: Overwrites the current exe with the new one.
                // c. Start-Process: Relaunches the updated application.

                string psCommand = $@"
                    $appPid = {currentPid};
                    $currentPath = '{currentExePath}';
                    $newPath = '{newVersionPath}';
                    
                    # Wait for the main app to exit
                    Write-Host 'Waiting for application to close...';
                    try {{ Wait-Process -Id $appPid -ErrorAction SilentlyContinue }} catch {{ }}
                    
                    # specific small delay to ensure file handles are free
                    Start-Sleep -Seconds 1; 
                    
                    # Replace the file
                    Write-Host 'Updating files...';
                    Move-Item -Path $newPath -Destination $currentPath -Force;
                    
                    # Restart the application
                    Write-Host 'Restarting application...';
                    Start-Process -FilePath $currentPath;
                ";

                // 3. Configure the ProcessStartInfo to run PowerShell
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                    UseShellExecute = false,
                    CreateNoWindow = false, // Set to true if you want the update to be invisible
                    WindowStyle = ProcessWindowStyle.Hidden // Set to Hidden for invisible updates
                };

                // 4. Start the PowerShell process
                Process.Start(psi);

                // 5. Quit the current application immediately
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Update failed: {ex.Message}");
            }
        }



        // Fix: Changed to async Task to avoid UI freezing
        public async Task CheckForUpdates()
        {
            // Logic to check for updates goes here
            //Console.WriteLine("Checking for updates...");
            //Clipmage.Properties.Resources.ReleasesURL

            try
            {
                releaseInfo = await RetrieveLatestReleaseInfo();

                if (releaseInfo.Version > currentVersion)
                {
                    // Promt user to update and provide release details link
                    // Fix: Ensure we are on the UI thread and show the form
                    this.Show();
                    this.BringToFront();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Update check failed: {ex.Message}");
            }
        }

        private static async Task<ReleaseInfo> RetrieveLatestReleaseInfo()
        {
            // Logic to retrieve the latest version information from the server
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Clipmage-Updater"); // Set a user agent for GitHub API requests

            string json = await client.GetStringAsync(Clipmage.Properties.Resources.ReleasesURL);
            using JsonDocument doc = JsonDocument.Parse(json);

            // Fix: Safely find the asset. Using FirstOrDefault prevents crash if list is empty.
            var assetsArray = doc.RootElement.GetProperty("assets").EnumerateArray();

            var exeAsset = assetsArray.FirstOrDefault(asset =>
                asset.TryGetProperty("name", out var nameProp) &&
                nameProp.GetString()?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true
            );

            // If no exe found, exeAsset.ValueKind will be Undefined
            string downloadUrl = "";
            if (exeAsset.ValueKind != JsonValueKind.Undefined)
            {
                downloadUrl = exeAsset.GetProperty("browser_download_url").GetString() ?? "";
            }

            Version version = ParseGithubVersion(doc.RootElement.GetProperty("tag_name").GetString() ?? "0.0.0");

            var releaseInfo = new ReleaseInfo
            {
                DownloadUrl = downloadUrl,
                ReleaseUrl = doc.RootElement.GetProperty("html_url").GetString() ?? "",
                Version = version
            };

            return releaseInfo;
        }

        private static Version ParseGithubVersion(string versionString)
        {
            if (versionString.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                versionString = versionString.Substring(1); // Remove the leading 'v'    
            }

            // Fix: Using TryParse is safer to avoid crashes on malformed tags
            if (Version.TryParse(versionString, out Version? result))
            {
                return result;
            }
            return new Version(0, 0, 0, 0);
        }
    }
}