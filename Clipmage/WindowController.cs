using System.Drawing;
using System.Windows.Forms;
using Clipmage;

namespace Clipmage
{


    public static class WindowController
    {

        private static List<BlackWindow> activeWindows = new List<BlackWindow>();
        
        // Singleton-ish reference to the Shelf
        public static ShelfWindow Shelf { get; private set; }
        
        public const int WINDOW_DISAPPEAR_DELAY = 5;
        // We updated this to require an Image
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
                // When the window closes, remove it from the list
                RemoveWindow(bw.id);
                //If you aren't using the list anymore, ensure the variable is nulled 
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
                windowToRemove.Close();
            }
        }

        public static void ClearAllWindows()
        {
            foreach (var window in activeWindows)
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




        // this Handles the shelf window too
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

                if (Shelf.Visible) Shelf.BringToFront();
            }
        }




    }
}