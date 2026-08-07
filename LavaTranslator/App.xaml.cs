using System.Windows;
using LavaTranslator.Infrastructure;
using LavaTranslator.Services;
using Application = System.Windows.Application;

namespace LavaTranslator;

public partial class App : Application
{
    private ConfigService? _configService;
    private TranslationService? _translationService;
    private TrayIconService? _tray;
    private GlobalHotkey? _hotkey;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _configService = new ConfigService();
        StartupService.SyncWithConfig(_configService.Current.General.RunAtStartup);

        _translationService = new TranslationService(_configService);
        _tray = new TrayIconService();

        _mainWindow = new MainWindow(_configService, _translationService, _tray);
        _hotkey = new GlobalHotkey(_mainWindow);
        _mainWindow.AttachHotkey(_hotkey);

        _mainWindow.InitializeTrayHandlers();
        _tray.Show();
        _hotkey.RegisterAltSpace();

        if (StartupService.IsStartupLaunch(e.Args))
        {
            _mainWindow.Hide();
        }
        else
        {
            _mainWindow.ShowAndActivate();
            _mainWindow.ShowStartupNotification();
        }

        Exit += OnExit;
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _tray?.Dispose();
    }
}
