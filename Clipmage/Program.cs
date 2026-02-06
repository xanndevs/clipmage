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
            WindowController.ToggleShelf();
            WindowController.Shelf.Hide();

            WindowController.DisplayTextWindow("Clipmage is running in the background...\nPress Ctrl+Shift+S to capture a screenshot.");

            // Create the invisible listener
            using (ClipboardWatcher watcher = new ClipboardWatcher())
            {
                // Define what happens when a screenshot is detected
                watcher.ScreenshotDetected += (sender, e) =>
                {
                    // 1. Grab the image from clipboard
                    Image img = Clipboard.GetImage();

                    // 2. Show the window
                    WindowController.DisplayImageWindow(img);
                };

                // Run the app in the background
                Application.Run(watcher);
            }
        }

    }
}