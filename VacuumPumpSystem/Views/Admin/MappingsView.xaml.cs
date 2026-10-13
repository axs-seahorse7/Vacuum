
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

        DummyCodeTextBox.KeyDown += DummyCodeTextBox_KeyDown;
        ProductCodeTextBox.KeyDown += ProductCodeTextBox_KeyDown;
    }

    private async void DummyCodeTextBox_KeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter)
            return;

        e.Handled = true;
        await LoadCurrentMappingAsync();
    }

    private void ProductCodeTextBox_KeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter)
            return;

        e.Handled = true;
        MapButton_Click(sender, new RoutedEventArgs());
    }

    private async Task LoadCurrentMappingAsync()
    {
        string dummyCode = DummyCodeTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(dummyCode))
        {
            MachineInfoText.Text = "Please scan a Dummy code.";
            return;
        }

        try
        {
            var mapping =
                await _mappingService.GetActiveMappingByDummyAsync(dummyCode);

            MachineInfoText.Text = $"Machine: {dummyCode}";

            CurrentMappingText.Text = mapping == null
                ? "No active product mapping exists for this machine."
                : $"Dummy: {mapping.DummyCode}\n" +
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
        string dummyCode = DummyCodeTextBox.Text.Trim();
        string productCode = ProductCodeTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(dummyCode) ||
            string.IsNullOrWhiteSpace(productCode))
        {
            StatusText.Text =
                "Scan both the Dummy code and product serial number.";
            return;
        }

        MapButton.IsEnabled = false;

        try
        {
            await _mappingService.MapProductAsync(
                dummyCode,
                productCode);

            StatusText.Text =
                $"Successfully mapped {productCode} to {dummyCode}.";

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
