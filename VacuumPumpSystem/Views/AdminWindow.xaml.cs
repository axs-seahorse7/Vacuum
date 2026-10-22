using System.Windows;
using System.Windows.Controls;
using VacuumPumpSystem.Services;
using VacuumPumpSystem.Views.Admin;
namespace VacuumPumpSystem.Views;

public partial class AdminWindow : Window
{
    private readonly LineService _lineService;
    private int? _selectedLineId;
    public AdminWindow()
    {
        InitializeComponent();

        _lineService = new LineService();

        ShowDashboard();

        Loaded += AdminWindow_Loaded;
    }


    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        string? page = button.Tag?.ToString();

        switch (page)
        {
            case "Dashboard":
                ShowDashboard();
                break;

            case "Machines":
                ShowMachines();
                break;

            case "DeviceDiscovery":
                ShowDeviceDiscovery();
                break;

            case "Lines":
                ShowLines();
                break;

            case "Products":
                ShowProducts();
                break;

            case "Models":
                ShowModels();
                break;

            case "Mappings":
                ShowMappings();
                break;

            case "Users":
                ShowUsers();
                break;

            case "Settings":
                ShowSettings();
                break;
        }
    }


    private void ShowDashboard()
    {
        MainContent.Content = new DashboardView();
    }


    private void ShowMachines()
    {   
        if (_selectedLineId == null)
        {
            MessageBox.Show(
                "Please select a production line first.",
                "Line Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        MainContent.Content = new MachinesView(_selectedLineId.Value);
    }


    private void ShowDeviceDiscovery()
{   
    if (_selectedLineId == null)
    {
        MessageBox.Show(
            "Please select a production line first.",
            "Line Required",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        return;
    }

    MainContent.Content =
        new DeviceDiscoveryView(
            _selectedLineId.Value);
}

    private void ShowLines()
    {
        MainContent.Content = new LinesView();
    }


    private void ShowProducts()
    {
        MainContent.Content =
            new ProductsView();
    }

    private void ShowModels()
    {
        MainContent.Content = new ModelsView();
    }

    private void ShowMappings()
    {
        MainContent.Content =
            new MappingsView();
    }


    private void ShowUsers()
    {
        MainContent.Content =
            new UsersView();
    }


    private void ShowSettings()
    {
        MainContent.Content =
            new SettingsView();
    }


    private void Logout_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
    
    private async void AdminWindow_Loaded(
    object sender,
    RoutedEventArgs e)
    {   
        await LoadProductionLinesAsync();
    }

    private async Task LoadProductionLinesAsync()
    {
        try
        {
            var lines =
                await _lineService.GetLinesAsync();

            var activeLines =
                lines
                    .Where(x => x.IsActive)
                    .ToList();

            LineSelector.ItemsSource = activeLines;

            if (activeLines.Count > 0)
            {
                LineSelector.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to load production lines.\n\n{ex.Message}",
                "Line Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

   private void LineSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LineSelector.SelectedItem
            is not ProductionLine line)
        {
            return;
        }

        _selectedLineId = line.Id;

        // If Device Discovery is currently open,
        // reload it for the newly selected line.
        if (MainContent.Content is DeviceDiscoveryView)
        {
            MainContent.Content =
                new DeviceDiscoveryView(line.Id);
        }
        else if (MainContent.Content is MachinesView)
        {
            MainContent.Content =
                new MachinesView(line.Id);
        }
    }
}