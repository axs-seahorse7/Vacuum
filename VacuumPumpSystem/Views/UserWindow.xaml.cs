using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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

    private readonly PressureTestService _pressureTestService = new();
    private readonly LoRaConfigurationService _loRaConfigurationService = new();

    private ModbusReceiver? _receiver;

    private readonly ObservableCollection<ModbusDeviceData> _devices = new();
    private readonly HashSet<byte> _registeredModbusAddresses = new();
    private readonly Dictionary<byte, int> _machineIdsByAddress = new();
    private int _selectedLineId;

    // Prevents a double Enter (common with barcode scanners) from saving twice.
    private bool _isSaving;

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
            return;

        _selectedLineId = line.Id;

        await LoadRegisteredMachinesAsync();
        await LoadRecentScansAsync();
    }

    
    private async Task LoadRegisteredMachinesAsync()
    {
        try
        {
            StopReceiver();

            _devices.Clear();
            _registeredModbusAddresses.Clear();
            _machineIdsByAddress.Clear();

            RecordScanButton.IsEnabled = false;
            ConnectionStatusText.Text = "Loading production line...";

            if (LineSelector.SelectedItem is not ProductionLine line)
                return;

            if (!line.LoraChannel.HasValue)
            {
                ConnectionStatusText.Text =
                    $"Line {line.LineName}: LoRa channel is not configured.";
                return;
            }

            var machines = await _machineService
                .GetRegisteredMachinesAsync(_selectedLineId);

            foreach (var machine in machines)
            {
                _registeredModbusAddresses.Add(machine.ModbusAddress);
                _machineIdsByAddress[machine.ModbusAddress] = machine.Id;

                _devices.Add(new ModbusDeviceData
                {
                    ModbusAddress = machine.ModbusAddress,
                    RawValue = machine.CurrentPressure.HasValue
                        ? (uint)(machine.CurrentPressure.Value * 1000)
                        : 0,
                    Pressure = machine.CurrentPressure.HasValue
                        ? (double)machine.CurrentPressure.Value
                        : 0,
                    LastSeen = machine.LastSeen ?? DateTime.MinValue,
                    Status = machine.IsOnline ? "ONLINE" : "OFFLINE",
                    IsRegistered = true,
                    DummyCode = machine.DummyCode,
                    MachineName = machine.MachineName
                });
            }

            string? port = _receiverResolver.ResolvePort(this, _selectedLineId);

            if (string.IsNullOrWhiteSpace(port))
            {
                ConnectionStatusText.Text = "Receiver port not selected.";
                return;
            }

            ConnectionStatusText.Text =
                $"Configuring receiver on {port} for CH{line.LoraChannel.Value}...";

            bool configured = await _loRaConfigurationService
                .ConfigureChannelAsync(port, line.LoraChannel.Value);

            if (!configured)
            {
                ConnectionStatusText.Text =
                    $"Receiver channel verification failed for CH{line.LoraChannel.Value}.";
                return;
            }

            _receiver = new ModbusReceiver(port);
            _receiver.DeviceDataReceived += OnDeviceDataReceived;
            _receiver.SetAllowedAddresses(_registeredModbusAddresses);
            _receiver.Start();

            ConnectionStatusText.Text =
                $"Line: {line.LineName} | Port: {port} | " +
                $"CH{line.LoraChannel.Value} verified | Receiver connected";

            RecordScanButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            StopReceiver();
            ConnectionStatusText.Text = "Receiver initialization failed.";

            MessageBox.Show(
                $"Unable to initialize the receiver.\n\n{ex.Message}",
                "Vacuum Pump",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            if (_receiver == null)
                RecordScanButton.IsEnabled = false;
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

        ConnectionStatusText.Text =
            $"Line ID: {_selectedLineId} | Port: {port} | " +
            "Receiver started — physical LoRa channel not verified";
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

        // BeginInvoke (not Invoke): this runs on the serial port's thread.
        // Blocking it with Invoke can deadlock when the UI thread calls
        // SerialPort.Close() (line change / window close).
        Dispatcher.BeginInvoke(() =>
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

    private async void RecordScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isSaving)
            return;

        string dummyCode = ScanDummyTextBox.Text.Trim();
        string serialNo = ScanSerialTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(dummyCode) ||
            string.IsNullOrWhiteSpace(serialNo))
        {
            ScanStatusText.Text = "Scan both the Dummy QR and product serial number.";
            return;
        }

        var machine = _devices.FirstOrDefault(
            x => string.Equals(
                x.DummyCode,
                dummyCode,
                StringComparison.OrdinalIgnoreCase));

        if (machine == null ||
            !_machineIdsByAddress.TryGetValue(machine.ModbusAddress, out int machineId))
        {
            ScanStatusText.Text = $"Machine '{dummyCode}' is not registered on the selected line.";

            // The Dummy QR is the likely mistake: send the operator back to it.
            ScanDummyTextBox.Focus();
            ScanDummyTextBox.SelectAll();
            return;
        }

        // Reject stale readings instead of recording old pressure as a new test.
        // Inputs are kept, so pressing Enter again retries once data arrives.
        if (!string.Equals(machine.Status, "ONLINE", StringComparison.OrdinalIgnoreCase) ||
            machine.LastSeen == DateTime.MinValue ||
            DateTime.Now - machine.LastSeen > TimeSpan.FromSeconds(10))
        {
            ScanStatusText.Text = "No fresh pressure reading is available for this machine.";
            return;
        }

        _isSaving = true;
        RecordScanButton.IsEnabled = false;

        try
        {
            await _pressureTestService.SaveTestAsync(
                _selectedLineId,
                machineId,
                machine.DummyCode ?? dummyCode,
                serialNo,
                machine.Pressure);

            ScanStatusText.Text =
                $"Saved: {serialNo} | {dummyCode} | {machine.Pressure:F3} mbar";

            ScanSerialTextBox.Clear();
            ScanDummyTextBox.Clear();

            await LoadRecentScansAsync();

            ScanDummyTextBox.Focus();
        }
        catch (Exception ex)
        {
            ScanStatusText.Text = $"Unable to save test: {ex.Message}";
        }
        finally
        {
            _isSaving = false;
            RecordScanButton.IsEnabled = true;
        }
    }

    private async Task LoadRecentScansAsync()
    {
        if (_selectedLineId <= 0)
            return;

        try
        {
            var records = await _pressureTestService.GetRecentTestsAsync(
                _selectedLineId,
                10);

            RecentScansGrid.ItemsSource = records;
        }
        catch (Exception ex)
        {
            ScanStatusText.Text = $"Unable to load scan history: {ex.Message}";
        }
    }

    // Enter in the Dummy QR box: move to the product serial box.
    private void ScanDummyTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;

        if (string.IsNullOrWhiteSpace(ScanDummyTextBox.Text)) return;

        ScanSerialTextBox.Focus();
        ScanSerialTextBox.SelectAll();
    }

    // Enter in the product serial box: submit.
    private void ScanSerialTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;

        if (string.IsNullOrWhiteSpace(ScanSerialTextBox.Text)) return;

        if (string.IsNullOrWhiteSpace(ScanDummyTextBox.Text))
        {
            ScanDummyTextBox.Focus();   // dummy QR missing, go back to it
            return;
        }

        RecordScanButton_Click(sender, e);
    }
}