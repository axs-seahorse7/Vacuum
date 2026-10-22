using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VacuumPump.Communication.Models;
using VacuumPump.Communication.Services;
using VacuumPumpSystem.Services;
using VacuumPumpSystem.Models;
using System.Threading;

namespace VacuumPumpSystem.Views;

public partial class UserWindow : Window
{
    private readonly AuthenticatedUser _user;
    private readonly List<ProductionLine> _lines;
    private readonly MachineService _machineService;
    private readonly ReceiverResolver _receiverResolver;
    private readonly PressureTestService _pressureTestService = new();
    private readonly ScanPolicyService _scanPolicyService = new();
    private readonly SerialScanService _serialScanService = new();
    private readonly LoRaConfigurationService _loRaConfigurationService = new();
    private CancellationTokenSource? _scanResultCts;

    private ProductModel? _selectedModel;

    private ModbusReceiver? _receiver;
    private readonly ObservableCollection<ModbusDeviceData> _devices = new();
    private readonly HashSet<byte> _registeredModbusAddresses = new();
    private readonly Dictionary<byte, int> _machineIdsByAddress = new();

    private int _selectedLineId;
    private int _lineSelectionVersion;
    private bool _hasActiveScanPolicy;
    private decimal? _maximumPressure;
    private bool _isSaving;

    public UserWindow(AuthenticatedUser user, List<ProductionLine> lines)
    {
        InitializeComponent();
        Loaded += UserWindow_Loaded;

        _user = user;
        _lines = lines;
        _machineService = new MachineService();
        _receiverResolver = new ReceiverResolver();

        MachinesGrid.ItemsSource = _devices;
        UserInfoText.Text = $"User: {_user.Username} | Role: {_user.Role}";
        LineSelector.ItemsSource = _lines;
        RecordScanButton.IsEnabled = false;

        if (_lines.Count > 0)
            LineSelector.SelectedIndex = 0;
    }
    private async void ShowScanResult(bool accepted, string message)
    {
        _scanResultCts?.Cancel();
        _scanResultCts?.Dispose();

        var cts = new CancellationTokenSource();
        _scanResultCts = cts;

        ScanResultText.Text = accepted ? "OK" : "NG";

        ScanResultBorder.Background = accepted
            ? System.Windows.Media.Brushes.LightGreen
            : System.Windows.Media.Brushes.LightCoral;

        ScanResultBorder.BorderBrush = accepted
            ? System.Windows.Media.Brushes.Green
            : System.Windows.Media.Brushes.Red;

        ScanResultText.Foreground = accepted
            ? System.Windows.Media.Brushes.DarkGreen
            : System.Windows.Media.Brushes.DarkRed;

        ScanStatusText.Text = message;
        ScanStatusText.Foreground = accepted
            ? System.Windows.Media.Brushes.DarkGreen
            : System.Windows.Media.Brushes.DarkRed;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);

            if (!cts.IsCancellationRequested)
            {
                ScanResultText.Text = "";
                ScanResultBorder.Background =
                    System.Windows.Media.Brushes.Transparent;
                ScanResultBorder.BorderBrush =
                    System.Windows.Media.Brushes.Transparent;
                ScanStatusText.Text = "";
            }
        }
        catch (OperationCanceledException)
        {
            // A newer scan result has replaced this one.
        }
    }

    private void ShowScanError(string message)
    {
        _scanResultCts?.Cancel();

        ScanResultText.Text = "";
        ScanResultBorder.Background =
            System.Windows.Media.Brushes.Transparent;
        ScanResultBorder.BorderBrush =
            System.Windows.Media.Brushes.Transparent;

        ScanStatusText.Text = message;
        ScanStatusText.Foreground =
            System.Windows.Media.Brushes.DarkRed;
    }

    private async void LineSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LineSelector.SelectedItem is not ProductionLine line)
            return;

        int version = ++_lineSelectionVersion;
        _selectedLineId = line.Id;
        _hasActiveScanPolicy = false;
        RecordScanButton.IsEnabled = false;
        ScanStatusText.Text = $"Loading scan policy for {line.LineName}...";
        _devices.Clear();
        _registeredModbusAddresses.Clear();
        _machineIdsByAddress.Clear();

        await LoadScanPolicyAsync(line, version);
        if (version != _lineSelectionVersion) return;

        await LoadRegisteredMachinesAsync(version);
        if (version != _lineSelectionVersion) return;

        await LoadRecentScansAsync(_selectedLineId);
    }

    private async Task LoadScanPolicyAsync(ProductionLine line, int version)
    {
        try
        {
            var policy = await _scanPolicyService.GetByLineAsync(line.Id);
            if (version != _lineSelectionVersion) return;

            _hasActiveScanPolicy = policy is { IsActive: true };

            _maximumPressure = _hasActiveScanPolicy
                ? policy!.MaximumPressure
                : null;

            if (policy == null)
                ScanStatusText.Text = $"Scanning disabled: no saved policy exists for {line.LineName}. Ask an admin to configure one.";
            else if (!policy.IsActive)
                ScanStatusText.Text = $"Scanning disabled: the policy for {line.LineName} is inactive.";
            else
                ScanStatusText.Text = $"Policy active: {policy.RequiredScanCount} scan(s), " +
                    $"{policy.MinimumIntervalMinutes}-minute minimum gap, pressure below {policy.MaximumPressure:0.######}.";
        }
        catch (Exception ex)
        {
            if (version != _lineSelectionVersion) return;
            _hasActiveScanPolicy = false;
            _maximumPressure = null;
            ScanStatusText.Text = $"Scanning disabled: unable to load this line's policy. {ex.Message}";
        }

        UpdateRecordScanButtonState();
    }

    private async Task LoadRegisteredMachinesAsync(int version)
    {
        try
        {
            StopReceiver();
            _devices.Clear();
            _registeredModbusAddresses.Clear();
            _machineIdsByAddress.Clear();
            UpdateRecordScanButtonState();

            if (LineSelector.SelectedItem is not ProductionLine line || line.Id != _selectedLineId)
                return;

            if (!line.LoraChannel.HasValue)
            {
                ConnectionStatusText.Text = $"Line {line.LineName}: LoRa channel is not configured.";
                return;
            }

            var machines = await _machineService.GetRegisteredMachinesAsync(_selectedLineId);
            if (version != _lineSelectionVersion) return;

            foreach (var machine in machines)
            {
                _registeredModbusAddresses.Add(machine.ModbusAddress);
                _machineIdsByAddress[machine.ModbusAddress] = machine.Id;
                _devices.Add(new ModbusDeviceData
                {
                    ModbusAddress = machine.ModbusAddress,
                    RawValue = machine.CurrentPressure.HasValue ? (uint)(machine.CurrentPressure.Value * 1000) : 0,
                    Pressure = machine.CurrentPressure.HasValue ? (double)machine.CurrentPressure.Value : 0,
                    LastSeen = machine.LastSeen ?? DateTime.MinValue,
                    Status = machine.IsOnline ? "ONLINE" : "OFFLINE",
                    IsRegistered = true,
                    DummyCode = machine.DummyCode,
                    MachineName = machine.MachineName,
                    IsPressureWithinLimit = _hasActiveScanPolicy && _maximumPressure.HasValue &&
                    machine.CurrentPressure.HasValue &&
                    machine.IsOnline &&
                    machine.LastSeen.HasValue &&
                    machine.LastSeen.Value != DateTime.MinValue &&
                    DateTime.Now - machine.LastSeen.Value <= TimeSpan.FromSeconds(10) ? (double)machine.CurrentPressure.Value < (double)_maximumPressure.Value : null,
                });
                
            }

            string? port = _receiverResolver.ResolvePort(this, _selectedLineId);
            if (string.IsNullOrWhiteSpace(port))
            {
                ConnectionStatusText.Text = "Receiver port not selected.";
                return;
            }

            ConnectionStatusText.Text = $"Configuring receiver on {port} for CH{line.LoraChannel.Value}...";
            bool configured = await _loRaConfigurationService.ConfigureChannelAsync(port, line.LoraChannel.Value);
            if (version != _lineSelectionVersion) return;

            if (!configured)
            {
                ConnectionStatusText.Text = $"Receiver channel verification failed for CH{line.LoraChannel.Value}.";
                return;
            }

            _receiver = new ModbusReceiver(port);
            _receiver.DeviceDataReceived += OnDeviceDataReceived;
            _receiver.SetAllowedAddresses(_registeredModbusAddresses);
            _receiver.Start();

            ConnectionStatusText.Text = $"Line: {line.LineName} | Port: {port} | CH{line.LoraChannel.Value} verified | Receiver connected";
        }
        catch (Exception ex)
        {
            StopReceiver();
            ConnectionStatusText.Text = "Receiver initialization failed.";
            MessageBox.Show($"Unable to initialize the receiver.\n\n{ex.Message}",
                "Vacuum Pump", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            UpdateRecordScanButtonState();
        }
    }

    private void UpdateRecordScanButtonState()
    {
        RecordScanButton.IsEnabled =
            _hasActiveScanPolicy &&
            _receiver != null &&
            !_isSaving;
    }

    private void StopReceiver()
    {
        if (_receiver == null) return;
        _receiver.DeviceDataReceived -= OnDeviceDataReceived;
        _receiver.Dispose();
        _receiver = null;
        UpdateRecordScanButtonState();
    }

    private void OnDeviceDataReceived(ModbusDeviceData data)
    {
        if (!_registeredModbusAddresses.Contains(data.ModbusAddress)) return;

        Dispatcher.BeginInvoke(() =>
        {
            var existing = _devices.FirstOrDefault(x => x.ModbusAddress == data.ModbusAddress);
            if (existing == null) return;

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
                MachineName = existing.MachineName,
                IsPressureWithinLimit =
                _hasActiveScanPolicy && _maximumPressure.HasValue
                    ? data.Pressure < (double)_maximumPressure.Value
                    : null,
            };
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        StopReceiver();
        base.OnClosed(e);
    }

    private async Task RecordScanAsync()    
    {
        if (_isSaving) return;

        ScanResultText.Text = "";
        ScanResultBorder.Visibility = Visibility.Visible;
        ScanResultBorder.Background = System.Windows.Media.Brushes.Transparent;
        ScanResultBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(156, 163, 175));
        ScanStatusText.Text = "";

        if (!_hasActiveScanPolicy)
        {
            ShowScanError("Scanning is disabled because this line has no active policy.");
            return;
        }

        if (LineSelector.SelectedItem is not ProductionLine selectedLine || selectedLine.Id != _selectedLineId)
        {
            ShowScanError("Select a production line first.");
            return;
        }

        string dummyCode = ScanDummyTextBox.Text.Trim();
        string serialNo = ScanSerialTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(dummyCode) || string.IsNullOrWhiteSpace(serialNo))
        {
            ShowScanError( "Scan both the Dummy QR and product serial number.");
            return;
        }

        if (_selectedModel == null)
        {
            ShowScanError( "Please select a product model before scanning.");

            return;
        }

        if (!serialNo.StartsWith(_selectedModel.SerialPrefix, StringComparison.OrdinalIgnoreCase))
        {
            ShowScanError(
                $"NG: Serial prefix does not match model " +
                $"{_selectedModel.ModelName}. Expected prefix: " +
                $"{_selectedModel.SerialPrefix}.");

            ScanSerialTextBox.Clear();
            ScanSerialTextBox.Focus();
            ScanSerialTextBox.SelectAll();

            return;
        }

        string machineName = ScanDummyTextBox.Text.Trim();
        var machine = _devices.FirstOrDefault(x => string.Equals(x.MachineName?.Trim(),machineName, StringComparison.OrdinalIgnoreCase));

        if (machine == null || !_machineIdsByAddress.TryGetValue(machine.ModbusAddress, out int machineId))
        {
           ShowScanError( $"Machine '{machineName}' is not registered on the selected line.");

            ScanDummyTextBox.Clear();
            ScanSerialTextBox.Clear();
            FocusDummyInput();
            return;
        }

        if (!string.Equals(machine.Status, "ONLINE", StringComparison.OrdinalIgnoreCase) || machine.LastSeen == DateTime.MinValue || DateTime.Now - machine.LastSeen > TimeSpan.FromSeconds(10))
        {
                ShowScanError("No fresh pressure reading is available for this machine.");            
                    return;
        }

        int versionAtStart = _lineSelectionVersion;
        _isSaving = true;
        UpdateRecordScanButtonState();

        try
        {
            var result = await _serialScanService.RecordScanAsync(
                _selectedLineId, machineId, machine.DummyCode ?? dummyCode,
                serialNo, Convert.ToDecimal(machine.Pressure));

            if (versionAtStart != _lineSelectionVersion) return;

            
            ShowScanResult(
                true,
                result.IsCompleted
                    ? $"COMPLETE: {serialNo} | " +
                    $"{result.AcceptedScanCount}/{result.RequiredScanCount} scans | " +
                    $"{result.Pressure:0.###} mbar"
                    : $"Accepted scan {result.AcceptedScanCount}/" +
                    $"{result.RequiredScanCount}: {serialNo} | " +
                    $"Pressure {result.Pressure:0.###} mbar.");

            ScanSerialTextBox.Clear();
            ScanDummyTextBox.Clear();
            await LoadRecentScansAsync(_selectedLineId);
            ScanDummyTextBox.Focus();
        }
        catch (Exception ex)
        {
            if (versionAtStart == _lineSelectionVersion)
            {
                ShowScanResult(false, $"Scan rejected: {ex.Message}");
            }
        }
        finally
        {
            _isSaving = false;
            UpdateRecordScanButtonState();

            // Reset only if the user is still on the same production line.
            if (versionAtStart == _lineSelectionVersion)
            {
                ScanDummyTextBox.Clear();
                ScanSerialTextBox.Clear();
                FocusDummyInput();
            }
        }
    }

    private async Task LoadRecentScansAsync(int lineId)
    {
        if (lineId <= 0) return;
        try
        {
            var records = await _pressureTestService.GetRecentTestsAsync(lineId, 10);
            if (lineId == _selectedLineId)
                RecentScansGrid.ItemsSource = records;
        }
        catch (Exception ex)
        {
            ScanStatusText.Text = $"Unable to load scan history: {ex.Message}";
        }
    }

    private void ScanDummyTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Tab)
            return;

        // Prevent Tab from moving focus before we handle it.
        e.Handled = true;

        if (string.IsNullOrWhiteSpace(ScanDummyTextBox.Text))
        {
            ScanDummyTextBox.Focus();
            return;
        }

        ScanSerialTextBox.Focus();
        ScanSerialTextBox.SelectAll();
    }

    private void ScanSerialTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Tab)
            return;

        e.Handled = true;

        if (_isSaving)
            return;

        if (string.IsNullOrWhiteSpace(ScanDummyTextBox.Text) ||
            string.IsNullOrWhiteSpace(ScanSerialTextBox.Text))
        {
            ScanStatusText.Text =
                "Scan both the Dummy QR and Product Serial QR.";
            FocusDummyInput();
            return;
        }

        _ = SubmitScannedPairAsync();
    }

    private async Task SubmitScannedPairAsync()
    {
        await RecordScanAsync();
    }
    
    private void UserWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= UserWindow_Loaded;
        FocusDummyInput();
    }

    private void FocusDummyInput()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsLoaded)
                return;

            ScanDummyTextBox.Focus();
            ScanDummyTextBox.SelectAll();
        }));
    }

    private void ModelSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ScanResultText.Text = "";
        ScanResultBorder.Background =
            System.Windows.Media.Brushes.Transparent;
        ScanResultBorder.BorderBrush =
            System.Windows.Media.Brushes.Transparent;
        ScanStatusText.Text = "";

        ScanSerialTextBox.Clear();
        ScanDummyTextBox.Clear();

        FocusDummyInput();
    }

    private void SelectModel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ModelSelectionWindow(_selectedModel)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true ||
            dialog.SelectedModel == null)
        {
            return;
        }

        // This assignment is essential.
        _selectedModel = dialog.SelectedModel;

        SelectedModelText.Text = $"{_selectedModel.ModelName} | Prefix: {_selectedModel.SerialPrefix}";

        ScanSerialTextBox.Clear();
        ScanDummyTextBox.Clear();

        ScanResultText.Text = "";
        ScanResultBorder.Background = System.Windows.Media.Brushes.Transparent;
        ScanResultBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
        ScanStatusText.Text = "";

        FocusDummyInput();
    }

}
