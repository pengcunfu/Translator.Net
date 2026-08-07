using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LavaTranslator.Infrastructure;
using LavaTranslator.Models;
using LavaTranslator.Services;

namespace LavaTranslator.Views;

public partial class SettingsWindow : Window
{
    private readonly ConfigService _configService;
    private readonly ObservableCollection<OpenAiProviderConfig> _providers = [];
    private bool _suppressProviderSync;

    public SettingsWindow(ConfigService configService, Window owner)
    {
        InitializeComponent();
        _configService = configService;
        Owner = owner;

        var config = _configService.Current;
        BaiduAppIdBox.Text = config.Baidu.AppId;
        BaiduSecretBox.Password = config.Baidu.SecretKey;
        RunAtStartupBox.IsChecked = config.General.RunAtStartup;
        RememberTranslatorBox.IsChecked = config.General.RememberLastTranslator;

        foreach (var p in config.OpenAiProviders)
            _providers.Add(CloneProvider(p));

        ProvidersList.ItemsSource = _providers;
        if (_providers.Count > 0)
            ProvidersList.SelectedIndex = 0;
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
        // 已填写 API Key 时默认启用，避免忘记勾选导致菜单中看不到
        provider.Enabled = ProviderEnabledBox.IsChecked == true
            || !string.IsNullOrWhiteSpace(provider.ApiKey);
        provider.BaseUrl = ProviderBaseUrlBox.Text.Trim();
        provider.Model = ProviderModelBox.Text.Trim();

        if (double.TryParse(ProviderTemperatureBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var temp))
            provider.Temperature = Math.Clamp(temp, 0, 2);

        if (int.TryParse(ProviderMaxTokensBox.Text, out var maxTokens))
            provider.MaxTokens = Math.Clamp(maxTokens, 100, 128000);
    }

    private void OnSave(object sender, RoutedEventArgs e)
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

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
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
}
