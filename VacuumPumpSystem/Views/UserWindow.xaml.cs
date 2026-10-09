using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

using VacuumPump.Communication.Models;
using VacuumPump.Communication.Services;

using VacuumPumpSystem.Services;

namespace VacuumPumpSystem.Views;

public partial class UserWindow : Window
{
    private readonly AuthenticatedUser _user;
    private readonly List<ProductionLine> _lines;

    private readonly MachineService _machineService;
    private readonly ReceiverResolver _receiverResolver;

    private ModbusReceiver? _receiver;

    private readonly ObservableCollection<ModbusDeviceData> _devices = new();
    private readonly HashSet<byte> _registeredModbusAddresses = new();

    private int _selectedLineId;

    public UserWindow(AuthenticatedUser user, List<ProductionLine> lines)
    {
        InitializeComponent();

        _user = user;
        _lines = lines;

        _machineService = new MachineService();
        _receiverResolver = new ReceiverResolver();

        MachinesGrid.ItemsSource = _devices;
        UserInfoText.Text = $"User: {_user.Username} | Role: {_user.Role}";
        LineSelector.ItemsSource = _lines;

        if (_lines.Count > 0)
        {
            LineSelector.SelectedIndex = 0;
        }
    }

    private async void LineSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LineSelector.SelectedItem is not ProductionLine line)
        {
            return;
        }

        _selectedLineId = line.Id;
        await LoadRegisteredMachinesAsync();
    }

    private async Task LoadRegisteredMachinesAsync()
    {
        try
        {
            StopReceiver();

            _devices.Clear();
            _registeredModbusAddresses.Clear();

            var machines = await _machineService.GetRegisteredMachinesAsync(_selectedLineId);

            foreach (var machine in machines)
            {
                _registeredModbusAddresses.Add(machine.ModbusAddress);

                _devices.Add(new ModbusDeviceData
                {
                    ModbusAddress = machine.ModbusAddress,
                    RawValue = machine.CurrentPressure.HasValue ? (uint)(machine.CurrentPressure.Value * 1000) : 0,
                    Pressure = machine.CurrentPressure.HasValue ? (double)machine.CurrentPressure.Value : 0,
                    LastSeen = machine.LastSeen ?? DateTime.MinValue,
                    Status = machine.IsOnline? "ONLINE" : "OFFLINE",

                    IsRegistered = true,
                    DummyCode = machine.DummyCode,
                    MachineName = machine.MachineName
                });
            }

            ConnectionStatusText.Text = $"Registered machines: {machines.Count}";

            StartReceiver();

            if (_receiver != null)
            {
                ConnectionStatusText.Text += " | Receiver Connected";
            }
        }
        catch (Exception ex)
        {
            ConnectionStatusText.Text = "Connection Error";

            MessageBox.Show(
                $"Unable to load registered machines.\n\n{ex.Message}",
                "Vacuum Pump",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void StartReceiver()
    {
        string? port = _receiverResolver.ResolvePort(this, _selectedLineId);

        if (string.IsNullOrWhiteSpace(port))
        {
            ConnectionStatusText.Text = "Receiver Not Selected";
            return;
        }

        _receiver = new ModbusReceiver(port);
        _receiver.DeviceDataReceived += OnDeviceDataReceived;
        _receiver.SetAllowedAddresses(_registeredModbusAddresses);

        _receiver.Start();
    }

    private void StopReceiver()
    {
        if (_receiver == null)
        {
            return;
        }

        _receiver.DeviceDataReceived -= OnDeviceDataReceived;
        _receiver.Dispose();

        _receiver = null;
    }

    private void OnDeviceDataReceived(ModbusDeviceData data)
    {
        // Accept data only from Modbus addresses
        // registered on the currently selected line.
        if (!_registeredModbusAddresses.Contains(data.ModbusAddress))
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            var existing = _devices.FirstOrDefault(x => x.ModbusAddress == data.ModbusAddress);

            if (existing == null)
            {
                return;
            }

            int index = _devices.IndexOf(existing);

            _devices[index] = new ModbusDeviceData
            {
                ModbusAddress = existing.ModbusAddress,
                RawValue = data.RawValue,
                Pressure = data.Pressure,
                LastSeen = data.LastSeen,
                Status = "ONLINE",
                IsRegistered = true,
                DummyCode = existing.DummyCode,
                MachineName = existing.MachineName
            };
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        StopReceiver();
        base.OnClosed(e);
    }
}