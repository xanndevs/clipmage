using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static Clipmage.AppConfig;
namespace Clipmage
{
    public class DropZone : Form
    {

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const UInt32 SWP_NOSIZE = 0x0001;
        private const UInt32 SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const UInt32 TOPMOST_FLAGS = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW;

        private System.Windows.Forms.Timer _zOrderEnforcer;
        // Source - https://stackoverflow.com/a/34703664
        // Posted by clamum, modified by community. See post 'Timeline' for change history
        // Retrieved 2026-02-15, License - CC BY-SA 4.0

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);


        public DropZone()
        {
            // Make it a borderless tool window that doesn't show in the taskbar
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.StartPosition = FormStartPosition.Manual;
            //this.Bounds = bounds; // Set the specific area (e.g., 100, 100, 50, 50)
            this.Bounds = new Rectangle(0, Screen.PrimaryScreen.WorkingArea.Bottom + 1, 200, Screen.PrimaryScreen.Bounds.Height - Screen.PrimaryScreen.WorkingArea.Bottom - 1); // Set the specific area (e.g., 100, 100, 50, 50)

            // Make it effectively invisible, but still able to receive mouse/drag events.
            // (If you use Opacity = 0, Windows ignores it for drops)
            this.Opacity = 0.01f;
            this.BackColor = Color.FromArgb(255,20,20,20);

            // Enable Drag and Drop
            this.AllowDrop = true;

            this.DragEnter += DropZone_DragEnter;
            //this.DragLeave += DropZone_DragLeave;
            this.DragDrop += (s, e) => { 
                //Redirect the event to the shelf
                WindowController.Shelf.ProcessExternalDrop(e);
                //Check if user wants to open the shelf after dropping (Hard coded in app config)
                if (!WindowController.Shelf.Visible && OPEN_SHELF_AFTER_DROP)
                {
                    WindowController.Shelf.Show();
                }
            };

            this.Show();
            // --- The Aggressive Enforcer ---
            _zOrderEnforcer = new System.Windows.Forms.Timer();
            _zOrderEnforcer.Interval = 500; // Hammer the OS 20 times a second
            _zOrderEnforcer.Tick += (s, e) => ForceTopMost();
            _zOrderEnforcer.Start();
        }

        private void DropZone_DragEnter(object sender, DragEventArgs e)
        {
            // Only react if they are dragging a file or text
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text))
            {
                e.Effect = DragDropEffects.Copy;


            }
        }

        private void DropZone_DragLeave(object sender, EventArgs e)
        {
            // If they drag away without dropping, you can hide the shelf again
            // (Optional, depending on how you want it to behave)
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            // --- 3. Pin it aggressively when the handle is created ---
            SetWindowPos(this.Handle, HWND_TOPMOST, 0, Screen.PrimaryScreen.WorkingArea.Bottom + 1, 0, Screen.PrimaryScreen.WorkingArea.Bottom + 1,
                TOPMOST_FLAGS);
        }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;

                // 0x00000008 = WS_EX_TOPMOST (Forces it to the top native band)
                // 0x00000080 = WS_EX_TOOLWINDOW (Hides from Alt-Tab and Taskbar)
                // 0x08000000 = WS_EX_NOACTIVATE (Prevents it from taking focus)
                cp.ExStyle |= 0x00000008 | 0x00000080 | 0x08000000;

                return cp;
            }
        }
        private void ForceTopMost()
        {
            if (this.IsHandleCreated)
            {
                SetWindowPos(this.Handle, HWND_TOPMOST, 0, Screen.PrimaryScreen.WorkingArea.Bottom + 1, 0, Screen.PrimaryScreen.WorkingArea.Bottom + 1,
                TOPMOST_FLAGS);
            }
        }
    }
}
