using System.IO.Ports;

namespace VacuumPumpSystem.Services;

public class SerialPortService
{
    public List<string> GetAvailablePorts()
    {
        return SerialPort.GetPortNames()
            .OrderBy(GetPortNumber)
            .ToList();
    }

    private static int GetPortNumber(string port)
    {
        if (port.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(port[3..], out int number))
        {
            return number;
        }

        return int.MaxValue;
    }
}