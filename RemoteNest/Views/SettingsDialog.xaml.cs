using System.Windows;
using System.Windows.Controls;
using RemoteNest.Localization;
using RemoteNest.Services;

namespace RemoteNest.Views;

public partial class SettingsDialog
{
    private readonly Func<Task<List<string>>>? _hostProvider;
    private bool _isLoading;

    /// <param name="hostProvider">
    /// Supplies the saved connection hosts, so the RDP warning patch can also pre-trust the
    /// per-host "local devices" prompts. Optional — the dialog works standalone.
    /// </param>
    public SettingsDialog(Func<Task<List<string>>>? hostProvider = null)
    {
        InitializeComponent();
        _hostProvider = hostProvider;
        AcrylicHelper.Track(this);

        // SizeToContent can exceed a short display's work area — keep the Close button on screen.
        var maxUsable = SystemParameters.WorkArea.Height - 20;
        if (maxUsable > 0 && MaxHeight > maxUsable)
            MaxHeight = Math.Max(MinHeight, maxUsable);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoading = true;
        InitializeLanguageComboBox();
        InitializeThemeComboBox();
        TransparencySlider.Value = AcrylicHelper.Level;
        UpdateTransparencyText();
        AutoStartToggle.IsOn = AutoStartService.IsEnabled();
        RefreshRdpWarningsState();
        _isLoading = false;
    }

    private void InitializeLanguageComboBox()
    {
        LanguageComboBox.Items.Clear();
        foreach (var name in LanguageManager.SupportedLanguageNames)
            LanguageComboBox.Items.Add(name);

        var idx = Array.IndexOf(LanguageManager.SupportedLanguages, LanguageManager.GetCurrentLanguage());
        LanguageComboBox.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void InitializeThemeComboBox()
    {
        ThemeComboBox.Items.Clear();
        foreach (var theme in Services.ThemeManager.Supported)
            ThemeComboBox.Items.Add(GetThemeDisplayName(theme));

        var idx = Array.IndexOf(Services.ThemeManager.Supported, Services.ThemeManager.CurrentTheme);
        ThemeComboBox.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private static string GetThemeDisplayName(AppTheme theme) => theme switch
    {
        AppTheme.System => TranslationSource.Get("ThemeSystem"),
        AppTheme.Light => TranslationSource.Get("ThemeLight"),
        AppTheme.DarkBlue => TranslationSource.Get("ThemeDarkBlue"),
        AppTheme.Dark => TranslationSource.Get("ThemeDark"),
        _ => theme.ToString()
    };

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || LanguageComboBox.SelectedIndex < 0) return;
        LanguageManager.SetLanguage(LanguageManager.SupportedLanguages[LanguageComboBox.SelectedIndex]);

        // Combo items and the status line are plain strings, not {loc:Str} bindings —
        // rebuild them so the new language shows immediately.
        _isLoading = true;
        InitializeThemeComboBox();
        RefreshRdpWarningsState();
        UpdateTransparencyText();
        _isLoading = false;
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || ThemeComboBox.SelectedIndex < 0) return;
        Services.ThemeManager.Apply(Services.ThemeManager.Supported[ThemeComboBox.SelectedIndex]);
    }

    private void TransparencySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading) return;
        AcrylicHelper.SetLevel((int)e.NewValue);
        UpdateTransparencyText();
    }

    private void UpdateTransparencyText() =>
        TransparencyValueText.Text = AcrylicHelper.Level == 0
            ? TranslationSource.Get("TransparencyOff")
            : $"{AcrylicHelper.Level}%";

    private void AutoStartToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        if (AutoStartToggle.IsOn)
            AutoStartService.Enable();
        else
            AutoStartService.Disable();
    }

    private async void RdpWarningsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        try
        {
            RdpWarningsToggle.IsEnabled = false;
            bool completed;
            if (RdpWarningsToggle.IsOn)
            {
                var hosts = _hostProvider is null ? new List<string>() : await _hostProvider();
                completed = await RdpWarningPolicyService.Default.ApplyAsync(hosts);
            }
            else
            {
                completed = await RdpWarningPolicyService.Default.RevertAsync();
            }

            if (!completed)
            {
                MessageBox.Show(
                    TranslationSource.Get("RdpWarningsElevationDeclined"),
                    TranslationSource.Get("RdpWarningsSection"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            Log.Error("RDP warning policy change failed", ex);
            MessageBox.Show(ex.Message, TranslationSource.Get("ErrorOccurred"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RdpWarningsToggle.IsEnabled = true;
            RefreshRdpWarningsState();
        }
    }

    private void RefreshRdpWarningsState()
    {
        var status = RdpWarningPolicyService.Default.GetStatus();

        var wasLoading = _isLoading;
        _isLoading = true; // reflect actual state without re-triggering the handler
        RdpWarningsToggle.IsOn = status != RdpWarningPolicyStatus.Default;
        _isLoading = wasLoading;

        RdpWarningsStatusText.Text = status switch
        {
            RdpWarningPolicyStatus.Patched => TranslationSource.Get("RdpWarningsStatusPatched"),
            RdpWarningPolicyStatus.Partial => TranslationSource.Get("RdpWarningsStatusPartial"),
            _ => TranslationSource.Get("RdpWarningsStatusDefault")
        };
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
