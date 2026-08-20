using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
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
    private readonly TranslationService _translationService;
    private readonly TrayIconService _tray;
    private IGlobalHotkey? _hotkey;
    private string _currentTranslator = "百度翻译";
    private CancellationTokenSource? _translateCts;
    private bool _suppressEngineSync;
    private bool _suppressWebSiteSync;
    private bool _forceClose;
    private bool _webInitialized;
    private bool _webReady;
    private TaskCompletionSource<bool>? _webReadyTcs;
    private int _mainTabIndex;

    /// <summary>Design-time / XAML loader entry point.</summary>
    public MainWindow()
        : this(new ConfigService())
    {
    }

    private MainWindow(ConfigService configService)
        : this(configService, new TranslationService(configService), new TrayIconService())
    {
    }

    public MainWindow(
        ConfigService configService,
        TranslationService translationService,
        TrayIconService tray)
    {
        InitializeComponent();
        Icon = AppIcon.CreateWindowIcon();

        _configService = configService;
        _translationService = translationService;
        _tray = tray;

        _configService.ConfigChanged += (_, _) => Dispatcher.UIThread.Post(RefreshEngineList);

        InitLanguageSelectors();
        InitWebSites();
        LoadSettingsFields();
        RestoreLastTranslator();
        RefreshEngineList();
        ApplyPlatformUi();

        SelectMainTab(0);

        // 默认「网页」：窗口打开后再加载站点（WebView2 就绪）
        Opened += (_, _) =>
        {
            if (!_webInitialized)
                NavigateSelectedWebSite();
        };

        InputText.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                e.Handled = true;
                _ = TranslateAsync();
            }
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
        _tray.QuickTranslateRequested += (_, _) => Dispatcher.UIThread.Post(() => _ = QuickTranslateFromClipboardAsync());
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
        TranslatePage.IsVisible = index == 1;
        SettingsPage.IsVisible = index == 2;
        AboutPage.IsVisible = index == 3;

        WebToolbar.IsVisible = index == 0;
        TranslateToolbar.IsVisible = index == 1;

        SetNavTabSelected(TabWebButton, index == 0);
        SetNavTabSelected(TabTranslateButton, index == 1);
        SetNavTabSelected(TabSettingsButton, index == 2);
        SetNavTabSelected(TabAboutButton, index == 3);

        TranslateButton.IsDefault = index == 1;

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

    private void InitLanguageSelectors()
    {
        SourceLanguageCombo.ItemsSource = LanguageCatalog.SourceLanguages;
        TargetLanguageCombo.ItemsSource = LanguageCatalog.TargetLanguages;

        var general = _configService.Current.General;
        SelectLanguage(SourceLanguageCombo, general.SourceLanguage, "auto");
        SelectLanguage(TargetLanguageCombo, general.TargetLanguage, "en");

        SourceLanguageCombo.SelectionChanged += (_, _) => SaveLanguagePreferences();
        TargetLanguageCombo.SelectionChanged += (_, _) => SaveLanguagePreferences();
        UpdateLanguageStatus();
    }

    private void LoadSettingsFields()
    {
        var config = _configService.Current;
        BaiduAppIdBox.Text = config.Baidu.AppId;
        BaiduSecretBox.Text = config.Baidu.SecretKey;
        RunAtStartupBox.IsChecked = config.General.RunAtStartup && StartupService.IsSupported;
        RememberTranslatorBox.IsChecked = config.General.RememberLastTranslator;
    }

    private static void SelectLanguage(ComboBox combo, string code, string fallback)
    {
        var match = LanguageCatalog.FindByCode(code) ?? LanguageCatalog.FindByCode(fallback);
        if (match is not null)
            combo.SelectedItem = match;
    }

    private static string GetLanguageCode(ComboBox combo, string fallback) =>
        (combo.SelectedItem as LanguageOption)?.Code ?? fallback;

    private TranslationOptions GetTranslationOptions() => new()
    {
        FromCode = GetLanguageCode(SourceLanguageCombo, "auto"),
        ToCode = GetLanguageCode(TargetLanguageCombo, "en")
    };

    private void SaveLanguagePreferences()
    {
        var config = _configService.Current;
        config.General.SourceLanguage = GetLanguageCode(SourceLanguageCombo, "auto");
        config.General.TargetLanguage = GetLanguageCode(TargetLanguageCombo, "en");
        _configService.Save();
        UpdateLanguageStatus();
    }

    private void UpdateLanguageStatus()
    {
        var from = LanguageCatalog.GetDisplayName(GetLanguageCode(SourceLanguageCombo, "auto"));
        var to = LanguageCatalog.GetDisplayName(GetLanguageCode(TargetLanguageCombo, "en"));
        EngineLabel.Text = $"引擎: {_currentTranslator} | {from} → {to}";
    }

    private void OnSwapLanguages(object? sender, RoutedEventArgs e)
    {
        var from = GetLanguageCode(SourceLanguageCombo, "auto");
        var to = GetLanguageCode(TargetLanguageCombo, "en");

        if (from == "auto")
        {
            SetStatus("原文为自动检测时，请手动选择原文语言后再交换", isWarning: true);
            return;
        }

        SelectLanguage(SourceLanguageCombo, to, "en");
        SelectLanguage(TargetLanguageCombo, from, "zh");
        SaveLanguagePreferences();
        SetStatus("已交换原文与目标语言");
    }

    private void RestoreLastTranslator()
    {
        var general = _configService.Current.General;
        if (general.RememberLastTranslator && !string.IsNullOrWhiteSpace(general.LastTranslator))
            _currentTranslator = general.LastTranslator;
    }

    private void RefreshEngineList()
    {
        var translators = _translationService.AvailableTranslators.ToList();
        if (translators.Count == 0)
            translators.Add("百度翻译");

        if (!translators.Contains(_currentTranslator))
            _currentTranslator = translators[0];

        _suppressEngineSync = true;
        EngineCombo.ItemsSource = translators;
        EngineCombo.SelectedItem = _currentTranslator;
        _suppressEngineSync = false;

        UpdateLanguageStatus();
    }

    private void OnEngineSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEngineSync)
            return;

        if (EngineCombo.SelectedItem is string name)
        {
            _currentTranslator = name;
            UpdateLanguageStatus();
        }
    }

    private async void OnTranslate(object? sender, RoutedEventArgs e) => await TranslateAsync();

    private async Task TranslateAsync()
    {
        var text = InputText.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(text))
        {
            SetStatus("请输入要翻译的文本", isWarning: true);
            return;
        }

        _translateCts?.Cancel();
        _translateCts = new CancellationTokenSource();
        var token = _translateCts.Token;

        TranslateButton.IsEnabled = false;
        TranslateButton.Content = "翻译中...";
        SetStatus("正在翻译，请稍候...");

        try
        {
            var options = GetTranslationOptions();
            var result = await _translationService.TranslateAsync(text, _currentTranslator, options, token);
            if (token.IsCancellationRequested)
                return;

            if (result.Success)
            {
                OutputText.Text = result.TranslatedText;
                var from = LanguageCatalog.GetDisplayName(options.FromCode);
                var to = LanguageCatalog.GetDisplayName(options.ToCode);
                SetStatus($"翻译完成 ({from} → {to})");
                SaveLastTranslator();
            }
            else
            {
                OutputText.Text = $"翻译失败: {result.ErrorMessage}";
                SetStatus("翻译失败", isError: true);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("翻译已取消");
        }
        finally
        {
            TranslateButton.IsEnabled = true;
            TranslateButton.Content = "翻译";
        }
    }

    private void SaveLastTranslator()
    {
        var config = _configService.Current;
        if (config.General.RememberLastTranslator)
        {
            config.General.LastTranslator = _currentTranslator;
            _configService.Save();
        }
    }

    private void OnClear(object? sender, RoutedEventArgs e)
    {
        InputText.Text = "";
        OutputText.Text = "";
        InputText.Focus();
        SetStatus("内容已清空");
    }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(OutputText.Text))
        {
            SetStatus("没有可复制的翻译结果", isWarning: true);
            return;
        }

        var clipboard = GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            SetStatus("无法访问剪贴板", isError: true);
            return;
        }

        await clipboard.SetTextAsync(OutputText.Text);
        SetStatus("翻译结果已复制到剪贴板");
    }

    private void OnSwap(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(OutputText.Text))
        {
            SetStatus("没有可交换的翻译结果", isWarning: true);
            return;
        }

        InputText.Text = OutputText.Text;
        OutputText.Text = "";
        OnSwapLanguages(sender, e);
        InputText.Focus();
        SetStatus("内容与语言方向已交换");
    }

    private async void OnSaveSettings(object? sender, RoutedEventArgs e)
    {
        var config = _configService.Current;
        config.Baidu = new BaiduConfig
        {
            AppId = BaiduAppIdBox.Text?.Trim() ?? "",
            SecretKey = BaiduSecretBox.Text ?? ""
        };
        config.General.RunAtStartup = StartupService.IsSupported && RunAtStartupBox.IsChecked == true;
        config.General.RememberLastTranslator = RememberTranslatorBox.IsChecked == true;

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

        RefreshEngineList();

        SetStatus("设置已保存");
    }

    private async Task QuickTranslateFromClipboardAsync()
    {
        var text = await TryGetClipboardTextAsync();
        if (string.IsNullOrEmpty(text))
        {
            _tray.ShowBalloon("熔岩翻译助手", "剪贴板中没有文本");
            return;
        }

        ShowAndActivate();
        SelectMainTab(1);
        InputText.Text = text;
        InputText.Focus();
        await TranslateAsync();
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

        if (_mainTabIndex == 1)
        {
            InputText.Text = text;
            InputText.Focus();
            await TranslateAsync();
        }
        else
        {
            SelectMainTab(0);
            await FillWebSiteAsync(text);
        }
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
