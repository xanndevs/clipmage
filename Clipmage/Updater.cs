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
        private static readonly Version currentVersion = typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
        private static ReleaseInfo releaseInfo;
        private LinkLabel _releaseLabel;
        private Label _newVersionLabel;
        private Label _installPrompt;

        private Button _CancelButton;
        private Button _UpdateButton;

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

            SetupCancelButton();
            SetupUpdateButton();
        }


        private void InitializeComponent()
        {
            this.Text = Properties.Resources.AppName;
            this.ShowInTaskbar = true;
            this.Icon = Properties.Resources.AppIcon;
            this.ShowIcon = true;
            this.Size = new Size(500, 200);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(20, 20, 20);
            this.ForeColor = Color.White;
            this.TopMost = true;
            this.FormBorderStyle = FormBorderStyle.FixedToolWindow;

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
            _newVersionLabel.Font = new Font("Segoe UI", FONT_SIZE_NORMAL, FontStyle.Bold);
            this.Controls.Add(_newVersionLabel);
        }

        private void SetupReleaseLabel()
        {
            _releaseLabel = new LinkLabel();

            _releaseLabel.Text = "Click here to view the release notes.";
            _releaseLabel.AutoSize = true;
            _releaseLabel.Location = new Point(PADDING_NORMAL, ((PADDING_NORMAL + FONT_SIZE_NORMAL) * 1 ) + PADDING_NORMAL);
            _releaseLabel.Font = new Font("Segoe UI", FONT_SIZE_NORMAL, FontStyle.Regular);
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
            _installPrompt.Location = new Point(PADDING_NORMAL, ((PADDING_NORMAL + FONT_SIZE_NORMAL) * 2) + PADDING_NORMAL);
            _installPrompt.Font = new Font("Segoe UI", FONT_SIZE_NORMAL, FontStyle.Regular);
            this.Controls.Add(_installPrompt);
        }


        private void SetupUpdateButton()
        {
            _UpdateButton = new Button();

             _UpdateButton.Text = "Update Now";

            _UpdateButton.Location = new Point(this.ClientSize.Width - PADDING_NORMAL - _CancelButton.Width - PADDING_NORMAL - _UpdateButton.Width, this.ClientSize.Height - PADDING_NORMAL - _CancelButton.Height);

            this.Controls.Add(_UpdateButton);

            _UpdateButton.Click += (s, e) =>
            {
                BeginUpdateProcess();
                this.Hide();
            };


        }

        private static async void BeginUpdateProcess()
        {
            // Logic to start the update process goes here
            // For example, you could download the new version and launch the installer
            if (!string.IsNullOrEmpty(releaseInfo.DownloadUrl))
            {
                string downloadedPath = await DownloadFileToTempAsync(releaseInfo.DownloadUrl);


                PerformUpdate(downloadedPath);

            }
        }

        public static async Task<string> DownloadFileToTempAsync(string fileUrl)
        {
            // 1. Create a unique temporary file path 
            // We use a GUID to ensure the name doesn't conflict with existing files
            string tempFolder = Path.GetTempPath();
            string fileName = $"{Guid.NewGuid()}.exe"; // You can change extension if needed (e.g., .exe)
            string filePath = Path.Combine(tempFolder, fileName);

            // 2. Initialize HttpClient (Best practice: use a shared instance in real apps)
            using (HttpClient client = new HttpClient())
            {
                try
                {
                    // 3. Download the file bytes
                    byte[] fileBytes = await client.GetByteArrayAsync(fileUrl);

                    // 4. Write bytes to the temporary file
                    await File.WriteAllBytesAsync(filePath, fileBytes);

                    return filePath;
                }
                catch (Exception ex)
                {
                    // Handle network errors or invalid URLs
                    Console.WriteLine($"Download failed: {ex.Message}");
                    return null;
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

        private void SetupCancelButton()
        {
            _CancelButton = new Button();

            _CancelButton.Text = "Cancel";

            _CancelButton.Location = new Point(this.ClientSize.Width - PADDING_NORMAL - _CancelButton.Width, this.ClientSize.Height - PADDING_NORMAL - _CancelButton.Height);



            this.Controls.Add(_CancelButton);

            _CancelButton.Click += (s, e) =>
            {
                this.Hide();
            };


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
                Version = new Version(2,3,5)
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