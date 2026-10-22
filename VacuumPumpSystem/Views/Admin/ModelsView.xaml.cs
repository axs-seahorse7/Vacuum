
using System.Windows;
using System.Windows.Controls;
using VacuumPumpSystem.Models;
using VacuumPumpSystem.Services;

namespace VacuumPumpSystem.Views.Admin;

public partial class ModelsView : UserControl
{
    private readonly ProductModelService _service = new();
    private List<ProductModel> _models = new();
    private bool _loading;

    public ModelsView()
    {
        InitializeComponent();
        Loaded += ModelsView_Loaded;
    }

    private async void ModelsView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= ModelsView_Loaded;
        await LoadModelsAsync();
    }

    private async Task LoadModelsAsync()
    {
        try
        {
            _loading = true;
            _models = await _service.GetAllAsync();
            ModelsGrid.ItemsSource = null;
            ModelsGrid.ItemsSource = _models;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to load models.\n\n{ex.Message}",
                "Models",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = ModelsGrid.SelectedItem as ProductModel;

        var model = new ProductModel
        {
            Id = selected?.Id ?? 0,
            ModelName = ModelNameTextBox.Text,
            SerialPrefix = SerialPrefixTextBox.Text,
            IsActive = selected?.IsActive ?? true
        };

        try
        {
            await _service.SaveAsync(model);
            await LoadModelsAsync();
            ClearForm();

            MessageBox.Show(
                "Model saved successfully.",
                "Models",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to save model.\n\n{ex.Message}",
                "Models",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ModelsGrid_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ModelsGrid.SelectedItem is not ProductModel model)
            return;

        ModelNameTextBox.Text = model.ModelName;
        SerialPrefixTextBox.Text = model.SerialPrefix;
        SaveButton.Content = "UPDATE MODEL";
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        ClearForm();
    }

    private void ClearForm()
    {
        ModelsGrid.SelectedItem = null;
        ModelNameTextBox.Clear();
        SerialPrefixTextBox.Clear();
        SaveButton.Content = "SAVE MODEL";
    }
}
