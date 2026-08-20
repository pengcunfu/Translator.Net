using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace LavaTranslator.Infrastructure;

public sealed class TrayIconService : IDisposable
{
    private TrayIcon? _trayIcon;

    public event EventHandler? ShowWindowRequested;
    public event EventHandler? QuitRequested;

    public void Show()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = true;
            return;
        }

        var showItem = new NativeMenuItem("显示窗口");
        showItem.Click += (_, _) => Raise(ShowWindowRequested);

        var quitItem = new NativeMenuItem("退出程序");
        quitItem.Click += (_, _) => Raise(QuitRequested);

        _trayIcon = new TrayIcon
        {
            ToolTipText = "熔岩翻译助手 - Alt+Space 唤醒",
            Icon = AppIcon.CreateWindowIcon(),
            IsVisible = true,
            Menu = new NativeMenu
            {
                showItem,
                quitItem
            }
        };

        _trayIcon.Clicked += (_, _) => Raise(ShowWindowRequested);

        if (Application.Current is not null)
            TrayIcon.SetIcons(Application.Current, [_trayIcon]);
    }

    public void Hide()
    {
        if (_trayIcon is not null)
            _trayIcon.IsVisible = false;
    }

    public void ShowBalloon(string title, string message)
    {
        // Avalonia TrayIcon has no balloon API; keep tooltip informative.
        if (_trayIcon is not null)
            _trayIcon.ToolTipText = $"{title}: {message.Replace('\n', ' ')}";
    }

    private void Raise(EventHandler? handler)
    {
        if (handler is null)
            return;

        if (Dispatcher.UIThread.CheckAccess())
            handler.Invoke(this, EventArgs.Empty);
        else
            Dispatcher.UIThread.Post(() => handler.Invoke(this, EventArgs.Empty));
    }

    public void Dispose()
    {
        if (Application.Current is not null)
            TrayIcon.SetIcons(Application.Current, null);

        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
