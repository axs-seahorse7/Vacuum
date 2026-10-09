using System.Windows;
using System.Windows.Controls;

using VacuumPumpSystem.Services;

namespace VacuumPumpSystem.Views.Admin;

public partial class MachinesView : UserControl
{
    private readonly MachineService _machineService;

    private bool _loaded;


    private readonly int _lineId;

    public MachinesView(int lineId)
    {
        InitializeComponent();

        _lineId = lineId;

        _machineService = new MachineService();
    }


    private async void UserControl_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_loaded)
            return;

        _loaded = true;

        await LoadMachinesAsync();
    }


    private async void Refresh_Click(
        object sender,
        RoutedEventArgs e)
    {
        await LoadMachinesAsync();
    }


    private async Task LoadMachinesAsync()
    {
        try
        {
            var machines = await _machineService.GetRegisteredMachinesAsync(_lineId);

            MachinesGrid.ItemsSource = machines;

            UpdateSummary(machines);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to load machines.\n\n{ex.Message}",
                "Database Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }


    private void UpdateSummary(IReadOnlyList<dynamic> machines)
    {
        int total =
            machines.Count;

        int online =
            machines.Count(x => x.IsOnline);

        int offline =
            machines.Count(x => !x.IsOnline);

        TotalMachinesText.Text =
            total.ToString();

        OnlineMachinesText.Text =
            online.ToString();

        OfflineMachinesText.Text =
            offline.ToString();
    }
}