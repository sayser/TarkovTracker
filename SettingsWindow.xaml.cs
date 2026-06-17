using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TarkovTracker.Models;
using TarkovTracker.Services;

namespace TarkovTracker;

public partial class SettingsWindow : Window
{
    private readonly MainWindow _owner;
    private bool _suppressResolutionRefresh;
    private bool _suppressOverlayOpacityRefresh;
    private bool _isLoadingSettings;

    public SettingsWindow(MainWindow owner)
    {
        _owner = owner;
        Owner = owner;
        _isLoadingSettings = true;
        _suppressOverlayOpacityRefresh = true;
        _suppressResolutionRefresh = true;
        InitializeComponent();
        LoadCurrentValues();
        _isLoadingSettings = false;
    }

    private void LoadCurrentValues()
    {
        ScreenshotFolderText.Text = string.IsNullOrWhiteSpace(_owner.ScreenshotFolderPath)
            ? "Not set"
            : _owner.ScreenshotFolderPath;

        ScreenshotParsingCheckBox.IsChecked = _owner.IsScreenshotParsingEnabled;

        _suppressResolutionRefresh = true;

        string preset = _owner.CurrentGameResolutionPreset;
        bool matched = false;

        foreach (ComboBoxItem item in GameResolutionComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, preset, StringComparison.OrdinalIgnoreCase))
            {
                GameResolutionComboBox.SelectedItem = item;
                matched = true;
                break;
            }
        }

        if (!matched)
            GameResolutionComboBox.SelectedIndex = 0;

        UpdateResolutionTextBoxesFromPreset(preset);
        _suppressResolutionRefresh = false;

        _suppressOverlayOpacityRefresh = true;
        OverlayOpacitySlider.Value = _owner.OverlayDefaultOpacityPercent;
        OverlayOpacityValueText.Text = $"{_owner.OverlayDefaultOpacityPercent:0}%";
        _suppressOverlayOpacityRefresh = false;

        AboutProductText.Text = AppInfo.ProductName;
        AboutVersionText.Text = $"Version {AppInfo.VersionLabel}";
        AboutDescriptionText.Text = AppInfo.AboutDescription;
        AboutDataCreditText.Text = AppInfo.DataCredit;
        AboutDisclaimerText.Text = AppInfo.Disclaimer;
    }

    private void UpdateResolutionTextBoxesFromPreset(string preset)
    {
        (int width, int height) = ScreenshotResolutionHelper.ParsePreset(preset);
        ResolutionWidthTextBox.Text = width > 0 ? width.ToString(CultureInfo.InvariantCulture) : string.Empty;
        ResolutionHeightTextBox.Text = height > 0 ? height.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    private void BrowseScreenshotFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_owner.PromptForScreenshotFolder())
            ScreenshotFolderText.Text = _owner.ScreenshotFolderPath;
    }

    private void OpenScreenshotFolder_Click(object sender, RoutedEventArgs e)
    {
        _owner.OpenScreenshotFolder();
    }

    private void ScreenshotParsingCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
            return;

        _owner.ApplyScreenshotParsingEnabled(ScreenshotParsingCheckBox.IsChecked == true);
    }

    private void GameResolutionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressResolutionRefresh)
            return;

        if (GameResolutionComboBox.SelectedItem is not ComboBoxItem item)
            return;

        string preset = item.Tag as string ?? "auto";
        _owner.ApplyGameResolutionPreset(preset);
        UpdateResolutionTextBoxesFromPreset(preset);
    }

    private void SetCustomResolution_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ResolutionWidthTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int width) ||
            !int.TryParse(ResolutionHeightTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int height) ||
            width < 640 || height < 480)
        {
            MessageBox.Show(
                this,
                "Enter a valid resolution (minimum 640 x 480).",
                "Invalid Resolution",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        string preset = $"{width}x{height}";
        _owner.ApplyGameResolutionPreset(preset);

        _suppressResolutionRefresh = true;

        bool matched = false;
        foreach (ComboBoxItem item in GameResolutionComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, preset, StringComparison.OrdinalIgnoreCase))
            {
                GameResolutionComboBox.SelectedItem = item;
                matched = true;
                break;
            }
        }

        if (!matched)
            GameResolutionComboBox.SelectedIndex = -1;

        _suppressResolutionRefresh = false;

        MessageBox.Show(
            this,
            $"Game resolution set to {width} x {height}.",
            "Resolution Updated",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OverlayOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressOverlayOpacityRefresh || _isLoadingSettings || OverlayOpacityValueText == null)
            return;

        double percent = Math.Round(e.NewValue);
        OverlayOpacityValueText.Text = $"{percent:0}%";
        _owner.ApplyOverlayDefaultOpacityPercent(percent);
    }

    private void ClearRaidExfilHighlights_Click(object sender, RoutedEventArgs e)
    {
        _owner.ClearRaidExfilHighlights();
        MessageBox.Show(
            this,
            "Raid exfil highlights cleared for the current map.",
            "Exfil Highlights",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void DeleteAllScreenshots_Click(object sender, RoutedEventArgs e)
    {
        string folder = _owner.ScreenshotFolderPath;

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            MessageBox.Show(
                this,
                "Screenshot folder is not set or does not exist.",
                "Delete Screenshots",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        string[] screenshots = Directory.GetFiles(folder, "*.png");
        if (screenshots.Length == 0)
        {
            MessageBox.Show(
                this,
                "No PNG screenshots found in the folder.",
                "Delete Screenshots",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        MessageBoxResult confirm = MessageBox.Show(
            this,
            $"Permanently delete {screenshots.Length} screenshot(s) in:\n{folder}",
            "Delete All Screenshots",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes)
            return;

        int deleted = _owner.DeleteAllScreenshots();
        MessageBox.Show(
            this,
            $"Deleted {deleted} screenshot(s).",
            "Delete Screenshots",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
