using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LavaTranslator.Infrastructure;

public static class DialogHelper
{
    public static async Task ShowAsync(Window owner, string message, string title)
    {
        Button? okButton = null;
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Icon = AppIcon.CreateWindowIcon()
        };

        okButton = new Button
        {
            Content = "确定",
            Classes = { "primary" },
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true
        };
        okButton.Click += (_, _) => dialog.Close();
        DockPanel.SetDock(okButton, Dock.Bottom);
        okButton.Margin = new Thickness(0, 12, 0, 0);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                okButton,
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };

        await dialog.ShowDialog(owner);
    }
}
