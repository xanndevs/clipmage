using System;
using System.Drawing;
using System.Windows.Forms;
using Clipmage;

namespace Clipmage
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Create the invisible listener
            using (ClipboardWatcher watcher = new ClipboardWatcher())
            {
                // Define what happens when a screenshot is detected
                watcher.ScreenshotDetected += (sender, e) =>
                {
                    // 1. Grab the image from clipboard
                    Image img = Clipboard.GetImage();

                    // 2. Show the window
                    WindowController.DisplayWindow(img);
                };

                // Run the app in the background
                Application.Run(watcher);
            }
        }
    }
}