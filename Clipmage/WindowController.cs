using System.Drawing;
using System.Windows.Forms;
using Clipmage;

namespace Clipmage
{
    public static class WindowController
    {
        public const int WINDOW_DISAPPEAR_DELAY = 5;
        // We updated this to require an Image
        public static void DisplayWindow(Image screenshot)
        {
            if (screenshot == null) return;

            BlackWindow win = new BlackWindow(screenshot, WINDOW_DISAPPEAR_DELAY);
            win.Show();
        }
    }
}