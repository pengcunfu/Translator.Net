using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using LavaTranslator.Infrastructure;
using LavaTranslator.Models;
using LavaTranslator.Services;

namespace LavaTranslator;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService;
    private readonly TrayIconService _tray;
    private IGlobalHotkey? _hotkey;
    private bool _suppressWebSiteSync;
    private bool _forceClose;
    private bool _webInitialized;
    private bool _webReady;
    private TaskCompletionSource<bool>? _webReadyTcs;
    private int _mainTabIndex;

    /// <summary>Design-time / XAML loader entry point.</summary>
    public MainWindow()
        : this(new ConfigService(), new TrayIconService())
    {
    }

    public MainWindow(ConfigService configService, TrayIconService tray)
    {
        InitializeComponent();
        Icon = AppIcon.CreateWindowIcon();

        _configService = configService;
        _tray = tray;

        InitWebSites();
        LoadSettingsFields();
        ApplyPlatformUi();

        SelectMainTab(0);

        // 默认「网页」：窗口打开后再加载站点（WebView2 就绪）
        Opened += (_, _) =>
        {
            if (!_webInitialized)
                NavigateSelectedWebSite();
        };

        Closing += (_, e) =>
        {
            if (_forceClose)
                return;

            e.Cancel = true;
            Hide();
        };
    }

    public void AttachHotkey(IGlobalHotkey hotkey)
    {
        _hotkey = hotkey;
        _hotkey.HotkeyPressed += (_, _) => Dispatcher.UIThread.Post(OnHotkeyPressed);
    }

    public void InitializeTrayHandlers()
    {
        _tray.ShowWindowRequested += (_, _) => Dispatcher.UIThread.Post(ShowAndActivate);
        _tray.QuitRequested += (_, _) => Dispatcher.UIThread.Post(ShutdownApp);
    }

    public void ShowStartupNotification()
    {
        _tray.ShowBalloon(
            "熔岩翻译助手",
            "程序已启动 · 托盘图标打开窗口 · Alt+Space 自动填入剪贴板内容 / 显示隐藏");
    }

    private void ApplyPlatformUi()
    {
        if (!StartupService.IsSupported)
        {
            RunAtStartupBox.IsEnabled = false;
            RunAtStartupBox.IsChecked = false;
            StartupHint.IsVisible = true;
        }

        if (!OperatingSystem.IsWindows())
            HotkeyHint.Text = "全局快捷键 Alt+Space 目前仅在 Windows 上可用。";
    }

    private void OnNavTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var index))
            SelectMainTab(index);
        else if (sender is Button { Tag: int indexInt })
            SelectMainTab(indexInt);
    }

    private void SelectMainTab(int index)
    {
        _mainTabIndex = index;

        WebPage.IsVisible = index == 0;
        SettingsPage.IsVisible = index == 1;
        AboutPage.IsVisible = index == 2;

        WebToolbar.IsVisible = index == 0;

        SetNavTabSelected(TabWebButton, index == 0);
        SetNavTabSelected(TabSettingsButton, index == 1);
        SetNavTabSelected(TabAboutButton, index == 2);

        if (index == 0 && !_webInitialized)
            NavigateSelectedWebSite();
    }

    private static void SetNavTabSelected(Button button, bool selected)
    {
        button.Classes.Set("selected", selected);
    }

    private void InitWebSites()
    {
        WebSiteCombo.ItemsSource = WebTranslateCatalog.All;
        var lastId = _configService.Current.General.LastWebSiteId;
        var site = WebTranslateCatalog.FindById(lastId) ?? WebTranslateCatalog.All[0];

        _suppressWebSiteSync = true;
        WebSiteCombo.SelectedItem = site;
        _suppressWebSiteSync = false;
    }

    private WebTranslateSite? CurrentWebSite => WebSiteCombo.SelectedItem as WebTranslateSite;

    private void OnWebSiteSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressWebSiteSync)
            return;

        if (CurrentWebSite is not { } site)
            return;

        _configService.Current.General.LastWebSiteId = site.Id;
        _configService.Save();
        NavigateSelectedWebSite();
    }

    private void NavigateSelectedWebSite()
    {
        if (CurrentWebSite is not { } site)
            return;

        try
        {
            WebView.Source = site.HomeUrl;
            _webInitialized = true;
            SetStatus($"已加载 {site.Name}");
        }
        catch (Exception ex)
        {
            SetStatus($"无法打开网页：{ex.Message}", isError: true);
        }
    }

    private void OnWebNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        _webReady = false;
    }

    private void OnWebNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        _webReady = e.IsSuccess;
        _webReadyTcs?.TrySetResult(e.IsSuccess);
        _webReadyTcs = null;

        if (!e.IsSuccess)
        {
            SetStatus("网页加载失败，请检查网络或 WebView2 运行时", isError: true);
            return;
        }

        _ = InjectThinScrollbarStyleAsync();
    }

    private async Task InjectThinScrollbarStyleAsync()
    {
        // WebView 内页滚动条由页面 CSS 控制，注入 5px 细滚动条
        const string script = """
            (function () {
              const id = 'lava-thin-scrollbar';
              if (document.getElementById(id)) return;
              const style = document.createElement('style');
              style.id = id;
              style.textContent = `
                html { scrollbar-width: thin; }
                ::-webkit-scrollbar { width: 5px; height: 5px; }
                ::-webkit-scrollbar-track { background: transparent; }
                ::-webkit-scrollbar-thumb { background: rgba(0,0,0,.28); border-radius: 3px; }
                ::-webkit-scrollbar-thumb:hover { background: rgba(0,0,0,.42); }
              `;
              (document.head || document.documentElement).appendChild(style);
            })();
            """;

        try
        {
            await WebView.InvokeScript(script);
        }
        catch
        {
            // 部分站点 CSP 可能阻止注入，忽略即可
        }
    }

    private void LoadSettingsFields()
    {
        var config = _configService.Current;
        RunAtStartupBox.IsChecked = config.General.RunAtStartup && StartupService.IsSupported;
    }

    private async void OnSaveSettings(object? sender, RoutedEventArgs e)
    {
        var config = _configService.Current;
        config.General.RunAtStartup = StartupService.IsSupported && RunAtStartupBox.IsChecked == true;

        if (!_configService.Save(config))
        {
            await DialogHelper.ShowAsync(this, "保存配置失败", "错误");
            return;
        }

        if (StartupService.IsSupported && !StartupService.SetEnabled(config.General.RunAtStartup))
        {
            await DialogHelper.ShowAsync(this,
                "配置已保存，但无法更新开机自启动设置。请检查是否有权限修改注册表。",
                "警告");
        }

        SetStatus("设置已保存");
    }

    private async void OnHotkeyPressed()
    {
        var text = await TryGetClipboardTextAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            ToggleWindow();
            return;
        }

        Show();
        WindowState = WindowState.Normal;
        Activate();
        SelectMainTab(0);
        await FillWebSiteAsync(text);
    }

    private async Task<string?> TryGetClipboardTextAsync()
    {
        var clipboard = GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
            return null;

        try
        {
            var text = await clipboard.TryGetTextAsync();
            return text?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private async Task FillWebSiteAsync(string text)
    {
        if (CurrentWebSite is not { } site)
        {
            SetStatus("未选择网页翻译站点", isWarning: true);
            return;
        }

        if (!_webReady || WebView.Source is not { } source || !SamePage(source, site.HomeUrl))
        {
            SetStatus($"正在打开 {site.Name}…");
            WebView.Source = site.HomeUrl;
            _webInitialized = true;
            if (!await WaitWebReadyAsync(TimeSpan.FromSeconds(25)))
            {
                SetStatus("网页加载超时，请稍后重试", isWarning: true);
                return;
            }
        }

        // 部分站点为 SPA，页面看似加载完成但输入框尚未渲染，重试几次
        Exception? lastError = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                var resultJson = await WebView.InvokeScript(WebFillAutomation.BuildFillScript(site.Id, text));
                if (TryParseFillResult(resultJson, out var ok, out var detail))
                {
                    if (ok)
                    {
                        SetStatus($"已把剪贴板内容填入 {site.Name}");
                        return;
                    }

                    lastError = new InvalidOperationException(detail);
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
                break;
            }

            await Task.Delay(400);
        }

        SetStatus(
            $"未能自动填入 {site.Name}（{lastError?.Message ?? "未知原因"}），可手动 Ctrl+V 粘贴",
            isWarning: true);
    }

    private static bool TryParseFillResult(string? json, out bool ok, out string detail)
    {
        ok = false;
        detail = "未知原因";
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            // 个别平台可能对返回值再做一次 JSON 编码，先尝试解开
            if (json.Length >= 2 && json[0] == '"' && json[^1] == '"')
                json = JsonSerializer.Deserialize<string>(json) ?? json;

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True)
                ok = true;
            if (doc.RootElement.TryGetProperty("detail", out var detailEl))
                detail = detailEl.GetString() ?? detail;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool SamePage(Uri a, Uri b) =>
        string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase)
        && a.Port == b.Port
        && string.Equals(a.AbsolutePath, b.AbsolutePath, StringComparison.OrdinalIgnoreCase);

    private async Task<bool> WaitWebReadyAsync(TimeSpan timeout)
    {
        if (_webReady)
            return true;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _webReadyTcs = tcs;

        // 赋值后再次检查，避免导航在赋值前已完成导致一直等待
        if (_webReady)
        {
            _webReadyTcs = null;
            return true;
        }

        var winner = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
        return ReferenceEquals(winner, tcs.Task) && tcs.Task.Result;
    }

    public void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        SelectMainTab(0);
    }

    public void ToggleWindow()
    {
        if (IsVisible && IsActive)
            Hide();
        else
            ShowAndActivate();
    }

    private void ShutdownApp()
    {
        _forceClose = true;
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void SetStatus(string message, bool isWarning = false, bool isError = false)
    {
        StatusText.Text = message;
        var brushKey = isError ? "DangerBrush" : isWarning ? "WarningBrush" : "TextMutedBrush";
        if (TryGetResource(brushKey, ActualThemeVariant, out var resource) && resource is IBrush brush)
            StatusText.Foreground = brush;
        else
            StatusText.Foreground = isError ? Brushes.IndianRed : isWarning ? Brushes.DarkOrange : Brushes.Gray;
    }
}
