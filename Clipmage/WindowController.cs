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
        private static List<BlackWindow> activeWindows = new List<BlackWindow>();

        public static ShelfWindow Shelf { get; private set; }

        public const int WINDOW_DISAPPEAR_DELAY = 5;

        public static void DisplayWindow(Image screenshot)
        {
            if (screenshot == null) return;

            AppendWindow(
                new BlackWindow(screenshot, WINDOW_DISAPPEAR_DELAY)
            );
        }

        private static Guid AppendWindow(BlackWindow bw)
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

        // New method to bring a window back from the shelf
        public static void RestoreWindowFromShelf(Guid id, Point mousePos, Rectangle startBounds)
        {
            var window = activeWindows.FirstOrDefault(w => w.id == id);
            if (window != null)
            {
                window.WakeUpFromShelf(mousePos, startBounds);
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

        public static void ShowAllWindows()
        {
            foreach (var window in activeWindows)
            {
                window.Show();
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
    }
}