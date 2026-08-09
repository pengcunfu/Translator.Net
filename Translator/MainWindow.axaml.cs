using System.Collections.ObjectModel;
using System.Globalization;
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
    private readonly ObservableCollection<OpenAiProviderConfig> _providers = [];
    private string _currentTranslator = "百度翻译";
    private CancellationTokenSource? _translateCts;
    private bool _suppressEngineSync;
    private bool _suppressProviderSync;
    private bool _suppressWebSiteSync;
    private bool _forceClose;
    private bool _webInitialized;
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
                NavigateSelectedWebSite(withInputText: false);
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
        _hotkey.HotkeyPressed += (_, _) => Dispatcher.UIThread.Post(ToggleWindow);
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
            "程序已启动 · 托盘图标打开窗口 · Alt+Space 显示/隐藏");
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
            NavigateSelectedWebSite(withInputText: false);
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
        NavigateSelectedWebSite(withInputText: false);
    }

    private void NavigateSelectedWebSite(bool withInputText)
    {
        if (CurrentWebSite is not { } site)
            return;

        var text = withInputText ? InputText.Text : null;
        var url = site.ResolveUrl(text);
        try
        {
            WebView.Source = url;
            WebUrlBox.Text = url.ToString();
            _webInitialized = true;
            SetStatus(withInputText && site.BuildUrlWithText is not null
                ? $"已在 {site.Name} 打开原文"
                : $"已加载 {site.Name}");
        }
        catch (Exception ex)
        {
            SetStatus($"无法打开网页：{ex.Message}", isError: true);
        }
    }

    private void OnWebBack(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (WebView.CanGoBack)
                WebView.GoBack();
        }
        catch (Exception ex)
        {
            SetStatus($"后退失败：{ex.Message}", isWarning: true);
        }
    }

    private void OnWebForward(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (WebView.CanGoForward)
                WebView.GoForward();
        }
        catch (Exception ex)
        {
            SetStatus($"前进失败：{ex.Message}", isWarning: true);
        }
    }

    private void OnWebReload(object? sender, RoutedEventArgs e)
    {
        try
        {
            WebView.Refresh();
            SetStatus("正在刷新网页...");
        }
        catch (Exception ex)
        {
            SetStatus($"刷新失败：{ex.Message}", isWarning: true);
        }
    }

    private void OnWebHome(object? sender, RoutedEventArgs e) =>
        NavigateSelectedWebSite(withInputText: false);

    private void OnWebOpenWithInput(object? sender, RoutedEventArgs e)
    {
        var text = InputText.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            SetStatus("请先在「翻译」页填写原文", isWarning: true);
            return;
        }

        if (CurrentWebSite is { BuildUrlWithText: null } site)
        {
            // 站点无直达链接时，至少复制原文方便粘贴
            _ = CopyTextToClipboardAsync(text);
            SetStatus($"{site.Name} 不支持链接传参，原文已复制，请在网页中粘贴", isWarning: true);
        }

        SelectMainTab(0);
        NavigateSelectedWebSite(withInputText: true);
    }

    private void OnWebNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        if (WebView.Source is { } uri)
            WebUrlBox.Text = uri.ToString();

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

    private async Task CopyTextToClipboardAsync(string text)
    {
        var clipboard = GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(text);
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

        _providers.Clear();
        foreach (var p in config.OpenAiProviders)
            _providers.Add(CloneProvider(p));

        ProvidersList.ItemsSource = _providers;
        if (_providers.Count > 0)
            ProvidersList.SelectedIndex = 0;
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

    private void SetCurrentTranslator(string name)
    {
        _currentTranslator = name;
        _suppressEngineSync = true;
        EngineCombo.SelectedItem = name;
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

    private void OnAddProvider(object? sender, RoutedEventArgs e)
    {
        var provider = new OpenAiProviderConfig
        {
            Name = $"AI翻译 {_providers.Count + 1}",
            BaseUrl = "https://api.openai.com/v1/",
            Model = "gpt-4o-mini",
            Enabled = true
        };
        _providers.Add(provider);
        ProvidersList.SelectedItem = provider;
    }

    private void OnRemoveProvider(object? sender, RoutedEventArgs e)
    {
        if (ProvidersList.SelectedItem is not OpenAiProviderConfig selected)
            return;

        _providers.Remove(selected);
        ProviderEditor.IsEnabled = _providers.Count > 0;
        if (_providers.Count > 0)
            ProvidersList.SelectedIndex = 0;
    }

    private void OnDuplicateProvider(object? sender, RoutedEventArgs e)
    {
        if (ProvidersList.SelectedItem is not OpenAiProviderConfig selected)
            return;

        SaveEditorToProvider(selected);
        var copy = CloneProvider(selected);
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = selected.Name + " (副本)";
        _providers.Add(copy);
        ProvidersList.SelectedItem = copy;
    }

    private void OnProviderSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressProviderSync)
            return;

        if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is OpenAiProviderConfig oldProvider)
            SaveEditorToProvider(oldProvider);

        if (ProvidersList.SelectedItem is OpenAiProviderConfig provider)
            LoadProviderToEditor(provider);

        ProviderEditor.IsEnabled = ProvidersList.SelectedItem is not null;
    }

    private void LoadProviderToEditor(OpenAiProviderConfig provider)
    {
        _suppressProviderSync = true;
        ProviderEnabledBox.IsChecked = provider.Enabled;
        ProviderNameBox.Text = provider.Name;
        ProviderApiKeyBox.Text = provider.ApiKey;
        ProviderBaseUrlBox.Text = provider.BaseUrl;
        ProviderModelBox.Text = provider.Model;
        ProviderTemperatureBox.Text = provider.Temperature.ToString(CultureInfo.InvariantCulture);
        ProviderMaxTokensBox.Text = provider.MaxTokens.ToString();
        _suppressProviderSync = false;
    }

    private void SaveEditorToProvider(OpenAiProviderConfig provider)
    {
        provider.Name = ProviderNameBox.Text?.Trim() ?? "";
        provider.ApiKey = ProviderApiKeyBox.Text ?? "";
        provider.Enabled = ProviderEnabledBox.IsChecked == true
            || !string.IsNullOrWhiteSpace(provider.ApiKey);
        provider.BaseUrl = ProviderBaseUrlBox.Text?.Trim() ?? "";
        provider.Model = ProviderModelBox.Text?.Trim() ?? "";

        if (double.TryParse(ProviderTemperatureBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var temp))
            provider.Temperature = Math.Clamp(temp, 0, 2);

        if (int.TryParse(ProviderMaxTokensBox.Text, out var maxTokens))
            provider.MaxTokens = Math.Clamp(maxTokens, 100, 128000);
    }

    private async void OnSaveSettings(object? sender, RoutedEventArgs e)
    {
        if (ProvidersList.SelectedItem is OpenAiProviderConfig selected)
            SaveEditorToProvider(selected);

        var config = _configService.Current;
        config.Baidu = new BaiduConfig
        {
            AppId = BaiduAppIdBox.Text?.Trim() ?? "",
            SecretKey = BaiduSecretBox.Text ?? ""
        };
        config.OpenAiProviders = _providers.Select(CloneProvider).ToList();
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

        var aiEngines = _translationService.AvailableTranslators
            .Where(n => n != "百度翻译").ToList();
        if (aiEngines.Count > 0 && _currentTranslator == "百度翻译")
        {
            var last = _configService.Current.General.LastTranslator;
            if (string.IsNullOrWhiteSpace(last) || last == "百度翻译" || !aiEngines.Contains(last))
                SetCurrentTranslator(aiEngines[0]);
            else
                SetCurrentTranslator(last);
        }

        SetStatus("设置已保存");
    }

    private static OpenAiProviderConfig CloneProvider(OpenAiProviderConfig source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        ApiKey = source.ApiKey,
        BaseUrl = source.BaseUrl,
        Model = source.Model,
        Temperature = source.Temperature,
        MaxTokens = source.MaxTokens,
        Enabled = source.Enabled
    };

    private async Task QuickTranslateFromClipboardAsync()
    {
        var clipboard = GetTopLevel(this)?.Clipboard;
        var text = clipboard is null ? null : await clipboard.TryGetTextAsync();
        text = text?.Trim();
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
