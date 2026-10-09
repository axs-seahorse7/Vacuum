using System.Windows;
using System.Windows.Controls;
using VacuumPumpSystem.Services;

namespace VacuumPumpSystem.Views.Admin;

public partial class LinesView : UserControl
{
    private readonly LineService _lineService;

    private bool _loaded;

    public LinesView()
    {
        InitializeComponent();

        _lineService = new LineService();
    }

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
            return;

        _loaded = true;

        await LoadLinesAsync();
    }

    private async Task LoadLinesAsync()
    {
        try
        {
            var lines =
                await _lineService.GetLinesAsync();

            LinesGrid.ItemsSource = lines;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to load production lines.\n\n{ex.Message}",
                "Database Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void AddLine_Click(object sender, RoutedEventArgs e)
    {
        var dialog =
            new AddLineWindow();

        dialog.Owner =
            Window.GetWindow(this);

        bool? result =
            dialog.ShowDialog();

        if (result != true)
            return;

        try
        {
            await _lineService.CreateLineAsync(
                dialog.LineCode,
                dialog.LineName,
                dialog.Description);

            await LoadLinesAsync();

            MessageBox.Show(
                "Production line created successfully.",
                "Success",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to create production line.\n\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ToggleLine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.DataContext is not ProductionLine line)
        {
            return;
        }

        try
        {
            await _lineService.SetLineActiveAsync(
                line.Id,
                !line.IsActive);

            await LoadLinesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to update line.\n\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void EditLine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.DataContext is not ProductionLine line)
        {
            return;
        }

        MessageBox.Show(
            "Edit functionality will be connected next.",
            "Coming Next",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void SetChannel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.DataContext is not ProductionLine line)
        {
            return;
        }

        var dialog = new SetLineChannelWindow(line)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true)
            return;

        int? channel = dialog.SelectedChannel;

        try
        {
            var lines = await _lineService.GetLinesAsync();

            var sharedLines = channel.HasValue
                ? lines.Where(x =>
                    x.Id != line.Id &&
                    x.LoraChannel == channel.Value).ToList()
                : new List<ProductionLine>();

            if (sharedLines.Count > 0)
            {
                string lineNames = string.Join(
                    ", ",
                    sharedLines.Select(x => x.LineName));

                var result = MessageBox.Show(
                    $"Channel {channel} is already assigned to: {lineNames}.\n\n" +
                    "Using the same channel may cause radio interference. " +
                    "Confirm that the lines have been appropriately assessed " +
                    "for interference before continuing.\n\n" +
                    "Do you want to save this shared-channel assignment?",
                    "Shared LoRa Channel",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            await _lineService.UpdateLineChannelAsync(line.Id, channel);
            await LoadLinesAsync();

            MessageBox.Show(
                channel.HasValue
                    ? $"LoRa channel {channel} saved for {line.LineName}."
                    : $"LoRa channel cleared for {line.LineName}.",
                "Channel Updated",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to update the LoRa channel.\n\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

}