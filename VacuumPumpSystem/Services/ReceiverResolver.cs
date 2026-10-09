using System.IO.Ports;
using System.Windows;

namespace VacuumPumpSystem.Services;

public class ReceiverResolver
{
    private readonly SerialPortService _portService = new();
    private readonly ReceiverSettingsService _settingsService = new();

    public string? ResolvePort(Window owner, int lineId)
    {
        var ports = _portService.GetAvailablePorts();

        if (ports.Count == 0)
        {
            MessageBox.Show
            (
                "No serial communication receiver was detected.\n\n" +
                "Please connect the RS232/RS485 to LoRa converter and try again.",
                "Receiver Not Found",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );

            return null;
        }

        // Check previously selected port for this line.
        string? savedPort = _settingsService.GetSavedPort(lineId);

        if (!string.IsNullOrWhiteSpace(savedPort) && ports.Contains(savedPort, StringComparer.OrdinalIgnoreCase))
        {
            return savedPort;
        }

        // Only one receiver detected.
        if (ports.Count == 1)
        {
            string port = ports[0];
            _settingsService.SavePort(lineId, port);

            return port;
        }

        // Multiple receivers detected.
        var window = new Views.ReceiverSelectionWindow(ports)
        {
            Owner = owner
        };

        bool? result = window.ShowDialog();

        if (result != true || string.IsNullOrWhiteSpace(window.SelectedPort))
        {
            return null;
        }

        _settingsService.SavePort(lineId, window.SelectedPort);

        return window.SelectedPort;
    }
}