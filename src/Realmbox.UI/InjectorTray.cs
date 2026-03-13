using System.Runtime.InteropServices;

using NotifyIcon        = System.Windows.Forms.NotifyIcon;
using ContextMenuStrip  = System.Windows.Forms.ContextMenuStrip;
using ToolStripSeparator = System.Windows.Forms.ToolStripSeparator;
using DrawingIcon       = System.Drawing.Icon;
using DrawingColor      = System.Drawing.Color;
using DrawingBitmap     = System.Drawing.Bitmap;
using DrawingGraphics   = System.Drawing.Graphics;
using DrawingSolidBrush = System.Drawing.SolidBrush;

namespace Realmbox.UI
{
    /// <summary>
    /// System-tray icon representing Realmbox itself.
    /// Double-click or "Open" to restore the window.
    /// </summary>
    internal sealed class RealmboxTray : IDisposable
    {
        private NotifyIcon  _tray = null!;
        private DrawingIcon _icon = null!;
        private readonly System.Windows.Window _owner;

        public RealmboxTray(System.Windows.Window owner)
        {
            _owner = owner;
            BuildIcon();
            BuildTray();
            HookWindow();
        }

        public void Dispose()
        {
            _tray.Visible = false;
            _tray.Dispose();
            _icon.Dispose();
        }

        // ── Build ─────────────────────────────────────────────────────────────
        private void BuildIcon()
        {
            // Try to use the app's own .ico first
            string icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Realmbox.exe");
            try
            {
                _icon = DrawingIcon.ExtractAssociatedIcon(icoPath) ?? DrawDotIcon();
            }
            catch
            {
                _icon = DrawDotIcon();
            }
        }

        private void BuildTray()
        {
            ContextMenuStrip menu = new();
            menu.Items.Add("Open Window",  null, (_, _) => Restore());
            menu.Items.Add("Hide Window",  null, (_, _) => Hide());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit Realmbox",  null, (_, _) =>
                _owner.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown()));

            _tray = new NotifyIcon
            {
                Icon             = _icon,
                Text             = "Realmbox",
                Visible          = true,
                ContextMenuStrip = menu,
            };

            _tray.DoubleClick += (_, _) => Restore();
        }

        private void HookWindow()
        {
            _owner.StateChanged += (_, _) =>
            {
                if (_owner.WindowState == System.Windows.WindowState.Minimized)
                    _owner.Dispatcher.Invoke(() => _owner.Hide());
            };
        }

        private void Restore()
        {
            _owner.Dispatcher.Invoke(() =>
            {
                _owner.Show();
                _owner.WindowState = System.Windows.WindowState.Normal;
                _owner.Activate();
            });
        }

        private void Hide()
        {
            _owner.Dispatcher.Invoke(() => _owner.Hide());
        }

        // ── Fallback icon ─────────────────────────────────────────────────────
        private static DrawingIcon DrawDotIcon()
        {
            DrawingBitmap bmp = new(16, 16);
            using DrawingGraphics g = DrawingGraphics.FromImage(bmp);
            g.Clear(DrawingColor.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using DrawingSolidBrush brush = new(DrawingColor.CornflowerBlue);
            g.FillEllipse(brush, 2, 2, 12, 12);
            return DrawingIcon.FromHandle(bmp.GetHicon());
        }
    }
}
