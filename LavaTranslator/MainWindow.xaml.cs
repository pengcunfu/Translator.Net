using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LavaTranslator.Infrastructure;
using LavaTranslator.Models;
using LavaTranslator.Services;
using LavaTranslator.Views;

namespace LavaTranslator;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService;
    private readonly TranslationService _translationService;
    private readonly TrayIconService _tray;
    private GlobalHotkey? _hotkey;
    private readonly List<System.Windows.Controls.MenuItem> _aiMenuItems = [];
    private string _currentTranslator = "百度翻译";
    private CancellationTokenSource? _translateCts;

    public MainWindow(
        ConfigService configService,
        TranslationService translationService,
        TrayIconService tray)
    {
        InitializeComponent();
        _configService = configService;
        _translationService = translationService;
        _tray = tray;

        _configService.ConfigChanged += (_, _) => RefreshTranslatorMenu();

        InitLanguageSelectors();
        RefreshTranslatorMenu();
        RestoreLastTranslator();

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

    private void RefreshTranslatorMenu()
    {
        var translators = _translationService.AvailableTranslators;

        while (TranslatorEngineMenu.Items.Count > 1)
            TranslatorEngineMenu.Items.RemoveAt(1);
        _aiMenuItems.Clear();

        var aiNames = translators.Where(n => n != "百度翻译").ToList();
        if (aiNames.Count == 0)
        {
            var hint = new System.Windows.Controls.MenuItem
            {
                Header = "（未启用 AI：设置中勾选「启用此配置」并填写 API Key）",
                IsEnabled = false
            };
            TranslatorEngineMenu.Items.Add(hint);
            _aiMenuItems.Add(hint);
        }
        else
        {
            TranslatorEngineMenu.Items.Add(new Separator());
            foreach (var name in aiNames)
            {
                var item = new System.Windows.Controls.MenuItem
                {
                    Header = name,
                    IsCheckable = true,
                    Tag = name
                };
                item.Click += OnTranslatorMenuClick;
                TranslatorEngineMenu.Items.Add(item);
                _aiMenuItems.Add(item);
            }
        }

        if (!translators.Contains(_currentTranslator))
        {
            _currentTranslator = translators.FirstOrDefault() ?? "百度翻译";
            SetCheckedTranslator(_currentTranslator);
        }
        else
        {
            SetCheckedTranslator(_currentTranslator);
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
        var engine = _currentTranslator;
        EngineLabel.Text = $"引擎: {engine} | {from} → {to}";
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

        SetCheckedTranslator(_currentTranslator);
    }

    private void SetCheckedTranslator(string name)
    {
        _currentTranslator = name;
        BaiduMenuItem.IsChecked = name == "百度翻译";
        foreach (var item in _aiMenuItems)
        {
            if (item.Tag is string tag)
                item.IsChecked = tag == name;
        }
        UpdateLanguageStatus();
    }

    private void OnTranslatorMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { Tag: string name })
            SetCheckedTranslator(name);
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

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_configService, this);
        if (dialog.ShowDialog() == true)
        {
            RefreshTranslatorMenu();
            var aiEngines = _translationService.AvailableTranslators
                .Where(n => n != "百度翻译").ToList();
            if (aiEngines.Count > 0 && _currentTranslator == "百度翻译")
            {
                // 刚配置好 AI 时自动切换到第一个可用引擎
                var last = _configService.Current.General.LastTranslator;
                if (string.IsNullOrWhiteSpace(last) || last == "百度翻译" || !aiEngines.Contains(last))
                    SetCheckedTranslator(aiEngines[0]);
                else
                    SetCheckedTranslator(last);
            }
            SetStatus("设置已保存");
        }
    }

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "熔岩翻译助手 v1.0.0\n\n" +
            "基于 .NET 10 WPF\n" +
            "支持百度翻译 API 与 OpenAI 兼容 AI 翻译\n\n" +
            "快捷键: Alt+Space",
            "关于",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

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
    }
}
