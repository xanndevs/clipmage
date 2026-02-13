using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Clipmage;

namespace Clipmage
{
    public static class WindowController
    {
        private static List<PlaceableWindow> activeWindows = new List<PlaceableWindow>();
        private static readonly object _listLock = new object();
        public static ShelfWindow Shelf { get; private set; }

        public const int WINDOW_DISAPPEAR_DELAY = 5;

        public static Guid DisplayImageWindow(Image screenshot)
        {
            if (screenshot == null) return Guid.Empty;

            return AppendWindow(
                new ImageWindow(screenshot, WINDOW_DISAPPEAR_DELAY)
            );
        }

        public static Guid DisplayTextWindow(string text)
        {
            if (string.IsNullOrEmpty(text)) return Guid.Empty;

            return AppendWindow(new TextWindow(text, WINDOW_DISAPPEAR_DELAY));
        }

        public static Guid DisplayFolderWindow(string path)
        {
            if (string.IsNullOrEmpty(path)) return Guid.Empty;

            return AppendWindow(new PathWindow(path, WINDOW_DISAPPEAR_DELAY));
        }

        private static Guid AppendWindow(PlaceableWindow bw)
        {
            lock (_listLock) {
                activeWindows.Add(bw);
            }

            bw.FormClosed += (sender, args) =>
            {
                RemoveWindow(bw.id);
            };
            bw.Show();
            return bw.id;
        }

        private static void RemoveWindow(Guid id)
        {
            lock (_listLock) {
                var windowToRemove = activeWindows.FirstOrDefault(w => w.id == id);
                if (windowToRemove != null)
                {
                    activeWindows.Remove(windowToRemove);

                    if (!windowToRemove.IsDisposed)
                        windowToRemove.Close();
                }
            }
        }

        // New method to bring a window back from the shelf... a.k.a. wake up samurai
        public static void RestoreWindowFromShelf(Guid id, Point mousePos, Rectangle startBounds, Image img = null)
        {
            // 1. Search safely
            PlaceableWindow window = null;
            lock (_listLock) {
                window = activeWindows.FirstOrDefault(w => w.id == id);

                if (window != null)
                {
                    window.WakeUpFromShelf(mousePos, startBounds);
                }
                else
                {
                    var newWindow = new ImageWindow(img, WINDOW_DISAPPEAR_DELAY);

                    // 2. Add safely
                    lock (_listLock)
                    {
                        activeWindows.Add(newWindow);
                    }

                    newWindow.WakeUpFromShelf(mousePos, startBounds);
                }
            }
        }

        public static void ClearAllWindows()
        {
            List<PlaceableWindow> windowsSnapshot;

            lock (_listLock) {
                windowsSnapshot = activeWindows.ToList();
                activeWindows.Clear();
            }

            // Iterate over the COPY to close windows (prevents locking the UI for too long)
            foreach (var window in windowsSnapshot)
            {
                if (!window.IsDisposed) window.Close();
            }
        }

        public static void HideAllWindows()
        {
            lock (_listLock) {
                foreach (var window in activeWindows)
                {
                    window.Hide();
                }
            }
        }

        public static void ShowAllWindows()
        {
            lock (_listLock) {
                foreach (var window in activeWindows)
                {
                    window.Show();
                }
            }
        }

        public static void HideWindowWithId(Guid id)
        {
            lock (_listLock) {
                var windowToClose = activeWindows.FirstOrDefault(w => w.id == id);
                if (windowToClose != null)
                {
                    windowToClose.Hide();
                }
            }
        }

        public static PlaceableWindow GetWindowById(Guid id)
        {
            lock (_listLock) {
                return activeWindows.FirstOrDefault(w => w.id == id);
            }
        }

        public static void ToggleShelf()
        {
            if (Shelf == null || Shelf.IsDisposed)
            {
                Shelf = new ShelfWindow();
                Shelf.Show();
            }
            else
            {
                if (Shelf.Visible) Shelf.Hide();
                else Shelf.Show();

                if (Shelf.Visible)
                {
                    Shelf.WindowState = FormWindowState.Normal;
                    Shelf.BringToFront();
                }
            }
        }

        //Debugging Purposes Only - Print Active Windows to a MessageBox
        public static void PrintActiveWindows()
        {

            string messageBoxText = "";

            foreach (PlaceableWindow item in activeWindows)
            {
                messageBoxText += item.GetType().ToString() + "\n";

            }

            MessageBox.Show(messageBoxText);
        }
    }
}