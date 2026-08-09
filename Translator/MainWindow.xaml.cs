using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LavaTranslator.Infrastructure;
using LavaTranslator.Models;
using LavaTranslator.Services;

namespace LavaTranslator;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService;
    private readonly TranslationService _translationService;
    private readonly TrayIconService _tray;
    private GlobalHotkey? _hotkey;
    private readonly ObservableCollection<OpenAiProviderConfig> _providers = [];
    private string _currentTranslator = "百度翻译";
    private CancellationTokenSource? _translateCts;
    private bool _suppressEngineSync;
    private bool _suppressProviderSync;

    public MainWindow(
        ConfigService configService,
        TranslationService translationService,
        TrayIconService tray)
    {
        InitializeComponent();
        _configService = configService;
        _translationService = translationService;
        _tray = tray;

        _configService.ConfigChanged += (_, _) => Dispatcher.Invoke(RefreshEngineList);

        InitLanguageSelectors();
        LoadSettingsFields();
        RestoreLastTranslator();
        RefreshEngineList();

        InputText.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                _ = TranslateAsync();
            }
        };

        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
    }

    public void AttachHotkey(GlobalHotkey hotkey)
    {
        _hotkey = hotkey;
        _hotkey.HotkeyPressed += (_, _) => Dispatcher.Invoke(ToggleWindow);
    }

    public void InitializeTrayHandlers()
    {
        _tray.ShowWindowRequested += (_, _) => Dispatcher.Invoke(ShowAndActivate);
        _tray.QuickTranslateRequested += (_, _) => Dispatcher.Invoke(QuickTranslateFromClipboard);
        _tray.QuitRequested += (_, _) => Dispatcher.Invoke(ShutdownApp);
    }

    public void ShowStartupNotification()
    {
        _tray.ShowBalloon(
            "熔岩翻译助手",
            "程序已启动\n• 双击托盘图标打开窗口\n• Alt+Space 显示/隐藏\n• 右键托盘查看更多功能");
    }

    private void OnMainTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MainTabs))
            return;

        TranslateButton.IsDefault = MainTabs.SelectedIndex == 0;
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
        BaiduSecretBox.Password = config.Baidu.SecretKey;
        RunAtStartupBox.IsChecked = config.General.RunAtStartup;
        RememberTranslatorBox.IsChecked = config.General.RememberLastTranslator;

        _providers.Clear();
        foreach (var p in config.OpenAiProviders)
            _providers.Add(CloneProvider(p));

        ProvidersList.ItemsSource = _providers;
        if (_providers.Count > 0)
            ProvidersList.SelectedIndex = 0;
    }

    private static void SelectLanguage(System.Windows.Controls.ComboBox combo, string code, string fallback)
    {
        var match = LanguageCatalog.FindByCode(code) ?? LanguageCatalog.FindByCode(fallback);
        if (match is not null)
            combo.SelectedValue = match.Code;
    }

    private TranslationOptions GetTranslationOptions() => new()
    {
        FromCode = SourceLanguageCombo.SelectedValue as string ?? "auto",
        ToCode = TargetLanguageCombo.SelectedValue as string ?? "en"
    };

    private void SaveLanguagePreferences()
    {
        var config = _configService.Current;
        config.General.SourceLanguage = SourceLanguageCombo.SelectedValue as string ?? "auto";
        config.General.TargetLanguage = TargetLanguageCombo.SelectedValue as string ?? "en";
        _configService.Save();
        UpdateLanguageStatus();
    }

    private void UpdateLanguageStatus()
    {
        var from = LanguageCatalog.GetDisplayName(SourceLanguageCombo.SelectedValue as string ?? "auto");
        var to = LanguageCatalog.GetDisplayName(TargetLanguageCombo.SelectedValue as string ?? "en");
        EngineLabel.Text = $"引擎: {_currentTranslator} | {from} → {to}";
    }

    private void OnSwapLanguages(object sender, RoutedEventArgs e)
    {
        var from = SourceLanguageCombo.SelectedValue as string ?? "auto";
        var to = TargetLanguageCombo.SelectedValue as string ?? "en";

        if (from == "auto")
        {
            SetStatus("原文为自动检测时，请手动选择原文语言后再交换", isWarning: true);
            return;
        }

        SourceLanguageCombo.SelectedValue = to;
        TargetLanguageCombo.SelectedValue = from;
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
        if (EngineCombo.Items.Contains(name))
            EngineCombo.SelectedItem = name;
        _suppressEngineSync = false;
        UpdateLanguageStatus();
    }

    private void OnEngineSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEngineSync)
            return;

        if (EngineCombo.SelectedItem is string name)
        {
            _currentTranslator = name;
            UpdateLanguageStatus();
        }
    }

    private async void OnTranslate(object sender, RoutedEventArgs e) => await TranslateAsync();

    private async Task TranslateAsync()
    {
        var text = InputText.Text.Trim();
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

    private void OnClear(object sender, RoutedEventArgs e)
    {
        InputText.Clear();
        OutputText.Clear();
        InputText.Focus();
        SetStatus("内容已清空");
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(OutputText.Text))
        {
            SetStatus("没有可复制的翻译结果", isWarning: true);
            return;
        }

        Clipboard.SetText(OutputText.Text);
        SetStatus("翻译结果已复制到剪贴板");
    }

    private void OnSwap(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(OutputText.Text))
        {
            SetStatus("没有可交换的翻译结果", isWarning: true);
            return;
        }

        InputText.Text = OutputText.Text;
        OutputText.Clear();
        OnSwapLanguages(sender, e);
        InputText.Focus();
        SetStatus("内容与语言方向已交换");
    }

    private void OnAddProvider(object sender, RoutedEventArgs e)
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

    private void OnRemoveProvider(object sender, RoutedEventArgs e)
    {
        if (ProvidersList.SelectedItem is not OpenAiProviderConfig selected)
            return;

        _providers.Remove(selected);
        ProviderEditor.IsEnabled = _providers.Count > 0;
        if (_providers.Count > 0)
            ProvidersList.SelectedIndex = 0;
    }

    private void OnDuplicateProvider(object sender, RoutedEventArgs e)
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

    private void OnProviderSelectionChanged(object sender, SelectionChangedEventArgs e)
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
        ProviderApiKeyBox.Password = provider.ApiKey;
        ProviderBaseUrlBox.Text = provider.BaseUrl;
        ProviderModelBox.Text = provider.Model;
        ProviderTemperatureBox.Text = provider.Temperature.ToString(CultureInfo.InvariantCulture);
        ProviderMaxTokensBox.Text = provider.MaxTokens.ToString();
        _suppressProviderSync = false;
    }

    private void SaveEditorToProvider(OpenAiProviderConfig provider)
    {
        provider.Name = ProviderNameBox.Text.Trim();
        provider.ApiKey = ProviderApiKeyBox.Password;
        provider.Enabled = ProviderEnabledBox.IsChecked == true
            || !string.IsNullOrWhiteSpace(provider.ApiKey);
        provider.BaseUrl = ProviderBaseUrlBox.Text.Trim();
        provider.Model = ProviderModelBox.Text.Trim();

        if (double.TryParse(ProviderTemperatureBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var temp))
            provider.Temperature = Math.Clamp(temp, 0, 2);

        if (int.TryParse(ProviderMaxTokensBox.Text, out var maxTokens))
            provider.MaxTokens = Math.Clamp(maxTokens, 100, 128000);
    }

    private void OnSaveSettings(object sender, RoutedEventArgs e)
    {
        if (ProvidersList.SelectedItem is OpenAiProviderConfig selected)
            SaveEditorToProvider(selected);

        var config = _configService.Current;
        config.Baidu = new BaiduConfig
        {
            AppId = BaiduAppIdBox.Text.Trim(),
            SecretKey = BaiduSecretBox.Password
        };
        config.OpenAiProviders = _providers.Select(CloneProvider).ToList();
        config.General.RunAtStartup = RunAtStartupBox.IsChecked == true;
        config.General.RememberLastTranslator = RememberTranslatorBox.IsChecked == true;

        if (!_configService.Save(config))
        {
            MessageBox.Show(this, "保存配置失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!StartupService.SetEnabled(config.General.RunAtStartup))
        {
            MessageBox.Show(this,
                "配置已保存，但无法更新开机自启动设置。请检查是否有权限修改注册表。",
                "警告",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
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

    private void QuickTranslateFromClipboard()
    {
        if (!Clipboard.ContainsText())
        {
            _tray.ShowBalloon("熔岩翻译助手", "剪贴板中没有文本", System.Windows.Forms.ToolTipIcon.Warning);
            return;
        }

        var text = Clipboard.GetText().Trim();
        if (string.IsNullOrEmpty(text))
        {
            _tray.ShowBalloon("熔岩翻译助手", "剪贴板中没有文本", System.Windows.Forms.ToolTipIcon.Warning);
            return;
        }

        ShowAndActivate();
        InputText.Text = text;
        _ = TranslateAsync();
    }

    public void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        MainTabs.SelectedIndex = 0;
        InputText.Focus();
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
        System.Windows.Application.Current.Shutdown();
    }

    private void SetStatus(string message, bool isWarning = false, bool isError = false)
    {
        StatusText.Text = message;
        var brushKey = isError ? "DangerBrush" : isWarning ? "WarningBrush" : "TextMutedBrush";
        StatusText.Foreground = TryFindResource(brushKey) as System.Windows.Media.Brush
            ?? (isError
                ? System.Windows.Media.Brushes.IndianRed
                : isWarning
                    ? System.Windows.Media.Brushes.DarkOrange
                    : System.Windows.Media.Brushes.Gray);
    }
}
