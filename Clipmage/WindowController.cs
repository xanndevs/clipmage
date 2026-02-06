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


            return AppendWindow( new TextWindow(text, WINDOW_DISAPPEAR_DELAY) );
        }

        private static Guid AppendWindow(PlaceableWindow bw)
        {
            activeWindows.Add(bw);
            bw.FormClosed += (sender, args) =>
            {
                RemoveWindow(bw.id);
            };
            bw.Show();
            return bw.id;
        }

        private static void RemoveWindow(Guid id)
        {
            var windowToRemove = activeWindows.FirstOrDefault(w => w.id == id);
            if (windowToRemove != null)
            {
                activeWindows.Remove(windowToRemove);
                if (!windowToRemove.IsDisposed)
                    windowToRemove.Close();
            }
        }

        // New method to bring a window back from the shelf... a.k.a. wake up samurai
        public static void RestoreWindowFromShelf(Guid id, Point mousePos, Rectangle startBounds, Image img = null)
        {
            var window = activeWindows.FirstOrDefault(w => w.id == id);
            if (window != null)
            {
                window.WakeUpFromShelf(mousePos, startBounds);
            }
            else
            {
                // Optionally, handle the case where the window is not found
                // For example, lets create a blackwindow for this case so user can still interact with it

                var newWindow = new ImageWindow(img,  WINDOW_DISAPPEAR_DELAY);
                activeWindows.Add(newWindow);
                newWindow.WakeUpFromShelf(mousePos, startBounds);
            }
        }

        public static void ClearAllWindows()
        {
            var windows = activeWindows.ToList();
            foreach (var window in windows)
            {
                window.Close();
            }
            activeWindows.Clear();
        }

        public static void HideAllWindows()
        {
            foreach (var window in activeWindows)
            {
                window.Hide();
            }
        }

        public static void HideWindowWithId(Guid id)
        {
            var windowToClose = activeWindows.FirstOrDefault(w => w.id == id);
            if (windowToClose != null)
            {
                windowToClose.Hide();
            }
        }

        public static void ShowAllWindows()
        {
            foreach (var window in activeWindows)
            {
                window.Show();
            }
        }

        public static PlaceableWindow GetWindowById(Guid id)
        {

            return activeWindows.FirstOrDefault(w => w.id == id);

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
    }
}