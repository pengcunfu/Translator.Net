using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace LavaTranslator.Infrastructure;

public static class AppIcon
{
    private static readonly Uri IconUri = new("avares://Translator/Assets/icon.ico");

    public static WindowIcon CreateWindowIcon()
    {
        using var stream = AssetLoader.Open(IconUri);
        return new WindowIcon(stream);
    }

    public static Bitmap LoadBitmap()
    {
        using var stream = AssetLoader.Open(IconUri);
        return new Bitmap(stream);
    }
}
