using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LavaTranslator.Infrastructure;
using LavaTranslator.Services;

namespace LavaTranslator;

public partial class App : Application
{
    private ConfigService? _configService;
    private TranslationService? _translationService;
    private TrayIconService? _tray;
    private IGlobalHotkey? _hotkey;
    private MainWindow? _mainWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

            _configService = new ConfigService();
            StartupService.SyncWithConfig(_configService.Current.General.RunAtStartup);

            _translationService = new TranslationService(_configService);
            _tray = new TrayIconService();

            _mainWindow = new MainWindow(_configService, _translationService, _tray);
            _hotkey = GlobalHotkey.Create(_mainWindow);
            _mainWindow.AttachHotkey(_hotkey);
            _mainWindow.InitializeTrayHandlers();

            desktop.MainWindow = _mainWindow;

            _tray.Show();
            _hotkey.RegisterAltSpace();

            var args = desktop.Args ?? [];
            if (StartupService.IsStartupLaunch(args))
            {
                _mainWindow.Hide();
            }
            else
            {
                _mainWindow.ShowAndActivate();
                _mainWindow.ShowStartupNotification();
            }

            desktop.Exit += (_, _) =>
            {
                _hotkey?.Dispose();
                _tray?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
