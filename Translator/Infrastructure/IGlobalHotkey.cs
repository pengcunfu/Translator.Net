namespace LavaTranslator.Infrastructure;

public interface IGlobalHotkey : IDisposable
{
    event EventHandler? HotkeyPressed;
    bool RegisterAltSpace();
    void Unregister();
}
