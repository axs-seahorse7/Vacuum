
using System.Windows;
using System.Windows.Controls;
using VacuumPumpSystem.Services;

namespace VacuumPumpSystem.Views.Admin;

public partial class MappingsView : UserControl
{
    private readonly ProductMappingService _mappingService = new();

    public MappingsView()
    {
        InitializeComponent();

        MachineNameTextBox.KeyDown += MachineNameTextBox_KeyDown;
        ProductCodeTextBox.KeyDown += ProductCodeTextBox_KeyDown;
    }

    private async void MachineNameTextBox_KeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter)
            return;

        e.Handled = true;
        await LoadCurrentMappingAsync();
    }

    private async void ProductCodeTextBox_KeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter)
            return;

        e.Handled = true;
        await MapProductAsync();
    }

    private async Task LoadCurrentMappingAsync()
    {
        string machineName = MachineNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(machineName))
        {
            MachineInfoText.Text = "Please scan a machine name QR.";
            return;
        }

        try
        {
            var mapping =
                await _mappingService.GetActiveMappingByMachineNameAsync(
                    machineName);

            MachineInfoText.Text = $"Machine: {machineName}";

            CurrentMappingText.Text = mapping == null
                ? "No active product mapping exists for this machine."
                : $"Machine Name: {mapping.MachineName}\n" +
                  $"Product Serial: {mapping.ProductCode}\n" +
                  $"Mapped At: {mapping.MappedAt:g}";

            StatusText.Text = "Machine lookup completed.";
        }
        catch (Exception ex)
        {
            MachineInfoText.Text = "Unable to load machine mapping.";
            StatusText.Text = ex.Message;
        }
    }

    private async void MapButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await MapProductAsync();
    }

    private async Task MapProductAsync()
    {
        string machineName = MachineNameTextBox.Text.Trim();
        string productCode = ProductCodeTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(machineName) ||
            string.IsNullOrWhiteSpace(productCode))
        {
            StatusText.Text =
                "Scan both the machine name QR and product serial QR.";
            return;
        }

        MapButton.IsEnabled = false;

        try
        {
            await _mappingService.MapProductAsync(
                machineName,
                productCode);

            StatusText.Text =
                $"Successfully mapped {productCode} to {machineName}.";

            ProductCodeTextBox.Clear();

            await LoadCurrentMappingAsync();

            ProductCodeTextBox.Focus();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Mapping failed: {ex.Message}";
        }
        finally
        {
            MapButton.IsEnabled = true;
        }
    }
}
