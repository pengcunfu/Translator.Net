using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Win32;

namespace LavaTranslator.Infrastructure;

public static class GlobalHotkey
{
    public static IGlobalHotkey Create(Window window) =>
        OperatingSystem.IsWindows()
            ? new WindowsGlobalHotkey(window)
            : new NullGlobalHotkey();
}

file sealed class NullGlobalHotkey : IGlobalHotkey
{
#pragma warning disable CS0067
    public event EventHandler? HotkeyPressed;
#pragma warning restore CS0067
    public bool RegisterAltSpace() => false;
    public void Unregister() { }
    public void Dispose() { }
}

file sealed class WindowsGlobalHotkey : IGlobalHotkey
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x4C41;
    private const uint ModAlt = 0x0001;
    private const uint VkSpace = 0x20;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Window _window;
    private IntPtr _hwnd;
    private bool _registered;
    private bool _hooked;

    public event EventHandler? HotkeyPressed;

    public WindowsGlobalHotkey(Window window)
    {
        _window = window;
        _window.Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        EnsureHook();
        RegisterAltSpace();
    }

    private void EnsureHook()
    {
        if (_hooked)
            return;

        var handle = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
            return;

        _hwnd = handle;
        Win32Properties.AddWndProcHookCallback(_window, WndProc);
        _hooked = true;
    }

    public bool RegisterAltSpace()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        EnsureHook();
        if (_hwnd == IntPtr.Zero)
            return false;

        Unregister();
        _registered = RegisterHotKey(_hwnd, HotkeyId, ModAlt, VkSpace);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered && _hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HotkeyId);
            _registered = false;
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Dispatcher.UIThread.Post(() => HotkeyPressed?.Invoke(this, EventArgs.Empty));
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _window.Opened -= OnOpened;
        Unregister();
        if (_hooked)
        {
            Win32Properties.RemoveWndProcHookCallback(_window, WndProc);
            _hooked = false;
        }
    }
}
