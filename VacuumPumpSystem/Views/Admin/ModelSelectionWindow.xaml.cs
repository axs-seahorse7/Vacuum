
using System.Windows;
using VacuumPumpSystem.Models;
using VacuumPumpSystem.Services;

namespace VacuumPumpSystem.Views;

public partial class ModelSelectionWindow : Window
{
    private readonly ProductModelService _modelService = new();

    public ProductModel? SelectedModel { get; private set; }

    public ModelSelectionWindow(ProductModel? currentModel = null)
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadModelsAsync(currentModel);
    }

    private async Task LoadModelsAsync(ProductModel? currentModel)
    {
        try
        {
            var models = await _modelService.GetAllAsync(activeOnly: true);
            ModelsListBox.ItemsSource = models;

            if (currentModel != null)
            {
                ModelsListBox.SelectedItem =
                    models.FirstOrDefault(m => m.Id == currentModel.Id);
            }

            if (ModelsListBox.SelectedItem == null &&
                models.Count > 0)
            {
                ModelsListBox.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to load product models.\n\n{ex.Message}",
                "Select Model",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            DialogResult = false;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ModelsListBox.SelectedItem is not ProductModel model)
        {
            MessageBox.Show(
                "Please select a model.",
                "Select Model",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SelectedModel = model;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
