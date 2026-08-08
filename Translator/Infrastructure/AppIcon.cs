using System.Drawing;

namespace LavaTranslator.Infrastructure;

public static class AppIcon
{
    private static Icon? _icon;

    public static Icon Get()
    {
        if (_icon is not null)
            return _icon;

        var path = Path.Combine(AppContext.BaseDirectory, "icon.ico");
        _icon = File.Exists(path) ? new Icon(path) : SystemIcons.Application;
        return _icon;
    }
}
