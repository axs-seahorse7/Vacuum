using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

using VacuumPump.Communication.Models;
using VacuumPump.Communication.Services;

using VacuumPumpSystem.Services;

namespace VacuumPumpSystem.Views.Admin;

public partial class DeviceDiscoveryView : UserControl
{

    private readonly LineService _lineService = new();
    private int? _loraChannel;
    private ModbusReceiver? _receiver;
    // private  ModbusReceiver _receiver;

    private readonly MachineService _machineService;

    private readonly ObservableCollection<ModbusDeviceData> _devices = new();

    private bool _started;

    private readonly int _lineId;

    public DeviceDiscoveryView(int lineId)
    {
        InitializeComponent();

        _lineId = lineId;

        _machineService = new MachineService();

        var devicesGrid = FindName("DevicesGrid") as DataGrid;

        if (devicesGrid != null)
        {
            devicesGrid.ItemsSource = _devices;
        }
    }


    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (_started)
            return;

        _started = true;

        await InitializeAsync();
    }


    private async Task InitializeAsync()
    {
        try
        {
            await LoadRegisteredMachinesAsync();

            // Load the selected production line and its assigned channel.
            var lines = await _lineService.GetLinesAsync();
            var line = lines.FirstOrDefault(x => x.Id == _lineId);

            if (line == null)
            {
                MessageBox.Show("Production line not found.");
                return;
            }

            if (line.LoraChannel == null)
            {
                MessageBox.Show(
                    $"No LoRa channel assigned to {line.LineName}.\n\n" +
                    "Open Admin → Lines and assign a channel first.",
                    "Channel Not Configured",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            _loraChannel = line.LoraChannel;

            var resolver = new ReceiverResolver();

            string? selectedPort = resolver.ResolvePort(Window.GetWindow(this)!, _lineId);

            if (selectedPort == null)
                return;

            // Do not start ModbusReceiver until the physical
            // LoRa receiver has been configured and verified.
            
            var configurator = new LoRaConfigurationService();

            bool configured = await configurator.ConfigureChannelAsync(
                selectedPort,
                _loraChannel.Value);

            if (!configured)
                return;

            // The configurator has closed the COM port.
            // ModbusReceiver can now open it.
            _receiver = new ModbusReceiver(selectedPort);

            _receiver.DeviceDataReceived += OnDeviceDataReceived;

            // Discover valid Modbus responses on the configured LoRa channel.
            _receiver.Start();

            MessageBox.Show(
                $"Receiver {selectedPort} configured successfully to CH{_loraChannel}.\n\n" +
                "Machine Discovery is now listening.",
                "Discovery Started",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // Start the receiver only after we implement and verify
            // the LoRa configuration step.
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to initialize Device Discovery.\n\n{ex.Message}",
                "Initialization Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnDeviceDataReceived(ModbusDeviceData data)
    {
        Dispatcher.Invoke(() =>
        {
            var existing =
                _devices.FirstOrDefault(
                    x => x.ModbusAddress == data.ModbusAddress);

            if (existing != null)
            {
                /*
                 * Device already exists.
                 *
                 * Preserve registration information:
                 * DummyCode
                 * MachineName
                 * IsRegistered
                 *
                 * Update only live information:
                 * Pressure
                 * RawValue
                 * LastSeen
                 * Status
                 */

                var updated =
                    new ModbusDeviceData
                    {
                        ModbusAddress =
                            data.ModbusAddress,

                        RawValue =
                            data.RawValue,

                        Pressure =
                            data.Pressure,

                        LastSeen =
                            data.LastSeen,

                        Status =
                            "ONLINE",

                        IsRegistered =
                            existing.IsRegistered,

                        DummyCode =
                            existing.DummyCode,

                        MachineName =
                            existing.MachineName
                    };

                int index =
                    _devices.IndexOf(existing);

                _devices[index] = updated;
            }
            else
            {
                /*
                 * New Modbus device.
                 *
                 * It has not been registered yet.
                 */

                _devices.Add(
                    new ModbusDeviceData
                    {
                        ModbusAddress =
                            data.ModbusAddress,

                        RawValue =
                            data.RawValue,

                        Pressure =
                            data.Pressure,

                        LastSeen =
                            data.LastSeen,

                        Status =
                            "ONLINE",

                        IsRegistered = false,

                        DummyCode = null,

                        MachineName = null
                    });
            }
        });
    }

  private async void Register_Click(object sender, RoutedEventArgs e)
{
    if (sender is not Button button)
        return;

    if (button.DataContext is not ModbusDeviceData device)
        return;

    // Prevent registration if already registered.
    if (device.IsRegistered)
    {
        MessageBox.Show(
            $"Modbus address {device.ModbusAddress} is already registered.",
            "Already Registered",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        return;
    }

    try
    {
        bool alreadyRegistered =
            await _machineService
                .IsModbusAddressRegisteredAsync(
                    _lineId,
                    device.ModbusAddress);

        if (alreadyRegistered)
        {
            MessageBox.Show(
                $"Modbus address {device.ModbusAddress} is already registered.",
                "Already Registered",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var dialog =
            new RegisterMachineWindow(
                device.ModbusAddress);

        dialog.Owner =
            Window.GetWindow(this);

        bool? result =
            dialog.ShowDialog();

        if (result != true)
            return;

        string dummyCode = await _machineService.RegisterMachineAsync(
                _lineId,
                device.ModbusAddress,
                dialog.MachineName);

        // Update current UI row.
        var existing =
            _devices.FirstOrDefault(
                x => x.ModbusAddress == device.ModbusAddress);

        if (existing != null)
        {
            int index =
                _devices.IndexOf(existing);

            _devices[index] =
                new ModbusDeviceData
                {
                    ModbusAddress =
                        existing.ModbusAddress,

                    RawValue =
                        existing.RawValue,

                    Pressure =
                        existing.Pressure,

                    LastSeen =
                        existing.LastSeen,

                    Status =
                        existing.Status,

                    IsRegistered = true,

                    DummyCode =
                        dummyCode,

                    MachineName =
                        dialog.MachineName
                };
        }

        MessageBox.Show(
            $"Machine {dummyCode} registered successfully.",
            "Registration Successful",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
    catch (Exception ex)
    {
        MessageBox.Show(
            $"Registration failed.\n\n{ex.Message}",
            "Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}

    private async Task LoadRegisteredMachinesAsync()
    {
        try
        {
            var machines = await _machineService.GetRegisteredMachinesAsync(_lineId);

            foreach (var machine in machines)
            {
                // Don't add duplicate devices.
                if (_devices.Any(
                    x => x.ModbusAddress ==
                         machine.ModbusAddress))
                {
                    continue;
                }

                _devices.Add(
                    new ModbusDeviceData
                    {
                        ModbusAddress = machine.ModbusAddress,

                        RawValue =machine.CurrentPressure.HasValue? (uint)(machine.CurrentPressure.Value * 1000) : 0,

                        Pressure = machine.CurrentPressure.HasValue? (double) machine.CurrentPressure.Value : 0,

                        LastSeen =
                            machine.LastSeen
                            ?? DateTime.MinValue,

                        Status =
                            machine.IsOnline
                                ? "ONLINE"
                                : "OFFLINE",

                        IsRegistered = true,

                        DummyCode =
                            machine.DummyCode,

                        MachineName =
                            machine.MachineName
                    });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to load registered machines.\n\n{ex.Message}",
                "Database Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }


   private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_receiver == null)
            return;

        _receiver.DeviceDataReceived -=
            OnDeviceDataReceived;

        _receiver.Dispose();

        _receiver = null;
    }
}