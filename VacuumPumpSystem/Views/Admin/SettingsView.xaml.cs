using System;
using System.Linq;
using System.Threading.Tasks;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using VacuumPumpSystem.Services;

namespace VacuumPumpSystem.Views.Admin;

public partial class SettingsView : UserControl
{
    private readonly LineService _lineService = new();
    private readonly ScanPolicyService _scanPolicyService = new();
    private int _policyLoadVersion;
    private bool _isLoadingLines;

    public SettingsView()
    {
        InitializeComponent();
        Loaded += SettingsView_Loaded;
    }

    private async void SettingsView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= SettingsView_Loaded;
        await LoadLinesAsync();
    }

    private void ClearPolicyFields()
    {
        ScanCountTextBox.Clear();
        IntervalTextBox.Clear();
        PressureTextBox.Clear();

        PolicyActiveCheckBox.IsChecked = false;
        PolicySummaryText.Text = "";
    }

    private async Task LoadLinesAsync()
    {
        try
        {
            _isLoadingLines = true;
            SaveButton.IsEnabled = false;
            ClearPolicyFields();

            StatusText.Text = "Loading production lines...";

            var lines = await _lineService.GetLinesAsync();

            LineSelector.ItemsSource = lines
                .Where(line => line.IsActive)
                .ToList();

            if (LineSelector.Items.Count == 0)
            {
                StatusText.Text = "No active production lines were found.";
                return;
            }

            // Automatically select the first line.
            LineSelector.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not load production lines: {ex.Message}";
        }
        finally
        {
            _isLoadingLines = false;
        }

        // SelectionChanged may fire while loading is suppressed,
        // so explicitly load the first line's policy here.
        if (LineSelector.SelectedItem is ProductionLine line)
        {
            await LoadPolicyAsync(line);
        }
    }
   private async void LineSelector_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if (_isLoadingLines ||
            LineSelector.SelectedItem is not ProductionLine line)
        {
            return;
        }

        await LoadPolicyAsync(line);
    }

    private async Task LoadPolicyAsync(ProductionLine line)
    {
        // Identify this request so an older, slower request
        // cannot overwrite the newly selected line's data.
        int requestVersion = ++_policyLoadVersion;

        // Clear immediately when switching lines.
        ClearPolicyFields();

        SaveButton.IsEnabled = false;
        StatusText.Text = $"Loading settings for {line.LineName}...";

        try
        {
            var policy = await _scanPolicyService.GetByLineAsync(line.Id);

            // Ignore results for a line that is no longer selected.
            if (requestVersion != _policyLoadVersion)
                return;

            if (policy == null)
            {
                // New line: keep all fields empty.
                ClearPolicyFields();

                StatusText.Text =
                    $"No saved policy for {line.LineName}. Enter new values and save.";

                SaveButton.IsEnabled = true;
                return;
            }

            // Defensive check: never display a policy for another line.
            if (policy.LineId != line.Id)
            {
                ClearPolicyFields();
                StatusText.Text = "Policy line mismatch. Please reload Settings.";
                return;
            }

            ScanCountTextBox.Text =
                policy.RequiredScanCount.ToString(CultureInfo.InvariantCulture);

            IntervalTextBox.Text =
                policy.MinimumIntervalMinutes.ToString(CultureInfo.InvariantCulture);

            PressureTextBox.Text =
                policy.MaximumPressure.ToString(CultureInfo.InvariantCulture);

            PolicyActiveCheckBox.IsChecked = policy.IsActive;

            StatusText.Text = policy.IsActive
                ? $"Saved policy loaded for {line.LineName}."
                : $"Policy for {line.LineName} is inactive.";

            PolicySummaryText.Text =
                $"Last updated: {policy.UpdatedAt:yyyy-MM-dd HH:mm:ss}";

            SaveButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            if (requestVersion != _policyLoadVersion)
                return;

            ClearPolicyFields();
            SaveButton.IsEnabled = false;

            StatusText.Text =
                $"Could not load policy for {line.LineName}: {ex.Message}";
        }
    }    
    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (LineSelector.SelectedItem is not ProductionLine line)
        {
            StatusText.Text = "Select a production line first.";
            return;
        }

        if (!int.TryParse(ScanCountTextBox.Text, out int scanCount) ||
            !int.TryParse(IntervalTextBox.Text, out int intervalMinutes) ||
            !decimal.TryParse(PressureTextBox.Text, NumberStyles.Number,
                CultureInfo.InvariantCulture, out decimal maximumPressure))
        {
            StatusText.Text = "Enter valid whole numbers for scan count and interval, and a valid pressure value (use a dot for decimals).";
            return;
        }

        var policy = new ScanPolicy
        {
            LineId = line.Id,
            RequiredScanCount = scanCount,
            MinimumIntervalMinutes = intervalMinutes,
            MaximumPressure = maximumPressure,
            IsActive = PolicyActiveCheckBox.IsChecked == true
        };

        try
        {
            SaveButton.IsEnabled = false;
            StatusText.Text = "Saving configuration...";
            await _scanPolicyService.SaveAsync(policy);
            StatusText.Text = $"Scan policy saved for {line.LineName}.";
            PolicySummaryText.Text =
                $"{scanCount} scan(s) · minimum interval {intervalMinutes} minute(s) · pressure must be below {maximumPressure.ToString(CultureInfo.InvariantCulture)}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not save scan policy: {ex.Message}";
        }
        finally
        {
            SaveButton.IsEnabled = LineSelector.SelectedItem is ProductionLine;
        }
    }
}
