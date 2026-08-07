using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LavaTranslator.Infrastructure;

public sealed class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x4C41; // "LA"

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;
    private bool _registered;

    public event EventHandler? HotkeyPressed;

    public GlobalHotkey(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _source = HwndSource.FromHwnd(helper.EnsureHandle())
                   ?? throw new InvalidOperationException("无法创建窗口消息源");
        _source.AddHook(WndProc);
    }

    public bool RegisterAltSpace()
    {
        Unregister();
        _registered = RegisterHotKey(_source.Handle, HotkeyId, 0x0001, 0x20); // MOD_ALT, VK_SPACE
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
    }
}
