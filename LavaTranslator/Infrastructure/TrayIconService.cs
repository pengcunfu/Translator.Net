using System.Drawing;
using System.Windows.Forms;

namespace LavaTranslator.Infrastructure;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;

    public event EventHandler? ShowWindowRequested;
    public event EventHandler? QuickTranslateRequested;
    public event EventHandler? QuitRequested;

    public TrayIconService()
    {
        _menu = new ContextMenuStrip();
        _menu.Items.Add("显示窗口", null, (_, _) => ShowWindowRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("快速翻译（剪贴板）", null, (_, _) => QuickTranslateRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("退出程序", null, (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon = new NotifyIcon
        {
            Text = "熔岩翻译助手 - Alt+Space 唤醒",
            Icon = AppIcon.Get(),
            Visible = false,
            ContextMenuStrip = _menu
        };

        _notifyIcon.DoubleClick += (_, _) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Show()
    {
        _notifyIcon.Visible = true;
    }

    public void Hide()
    {
        _notifyIcon.Visible = false;
    }

    public void ShowBalloon(string title, string message, ToolTipIcon icon = ToolTipIcon.Info, int timeoutMs = 3000)
    {
        if (_notifyIcon.Visible)
            _notifyIcon.ShowBalloonTip(timeoutMs, title, message, icon);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }
}
