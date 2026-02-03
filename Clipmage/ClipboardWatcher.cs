using System;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace Clipmage
{
    public class ClipboardWatcher : Form
    {
        public event EventHandler ScreenshotDetected;

        [DllImport("user32.dll")]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        private const int WM_CLIPBOARDUPDATE = 0x031D;

        // Cooldown variables to prevent double-triggering
        private DateTime _lastTriggerTime = DateTime.MinValue;
        private readonly TimeSpan _cooldown = TimeSpan.FromMilliseconds(500);

        public ClipboardWatcher()
        {
            this.ShowInTaskbar = false;
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Minimized;
            AddClipboardFormatListener(this.Handle);
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                // Check if enough time has passed since the last trigger
                if ((DateTime.Now - _lastTriggerTime) > _cooldown)
                {
                    try
                    {
                        if (Clipboard.ContainsImage())
                        {
                            _lastTriggerTime = DateTime.Now; // Update trigger time
                            ScreenshotDetected?.Invoke(this, EventArgs.Empty);
                        }
                    }
                    catch { }
                }
            }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                RemoveClipboardFormatListener(this.Handle);
            }
            base.Dispose(disposing);
        }
    }
}