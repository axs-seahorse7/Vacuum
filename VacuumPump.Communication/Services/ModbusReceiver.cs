using System.IO;
using System.Windows;
using System.IO.Ports;
using VacuumPump.Communication.Models;

namespace VacuumPump.Communication.Services;

public class ModbusReceiver : IDisposable
{
    private readonly SerialPort _port;
    private readonly List<byte> _buffer = new();

    private readonly HashSet<byte> _allowedAddresses = new();

    private readonly object _allowedAddressesLock = new();

    public event Action<ModbusDeviceData>? DeviceDataReceived;

    public bool IsOpen => _port.IsOpen;

    public ModbusReceiver(string portName, int baudRate = 115200)
    {
        _port = new SerialPort(
            portName,
            baudRate,
            Parity.None,
            8,
            StopBits.One
        );

        _port.DataReceived += OnDataReceived;
    }

    // ============================================================
    // REGISTERED MODBUS FILTER
    // ============================================================

    public void SetAllowedAddresses(IEnumerable<byte> addresses)
    {
        lock (_allowedAddressesLock)
        {
            _allowedAddresses.Clear();

            foreach (byte address in addresses)
            {
                _allowedAddresses.Add(address);
            }
        }
    }

   private bool IsAddressAllowed(byte address)
    {
        lock (_allowedAddressesLock)
        {
            // Empty list means NO FILTER.
            // This is required for Device Discovery.
            if (_allowedAddresses.Count == 0)
                return true;

            return _allowedAddresses.Contains(address);
        }
    }

    // ============================================================
    // SERIAL PORT
    // ============================================================

    public void Start()
    {
        Log($"Start requested. Port={_port.PortName}");

        try
        {
            if (!_port.IsOpen)
                _port.Open();

            Log($"SUCCESS: Port {_port.PortName} opened. IsOpen={_port.IsOpen}");
        }
        catch (Exception ex)
        {
            Log($"START ERROR: {ex}");
            throw;
        }
    }

    public void Stop()
    {
        if (_port.IsOpen)
        {
            _port.Close();
        }
    }

    // ============================================================
    // RECEIVE DATA
    // ============================================================

    
   private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        Log($"DATA EVENT FIRED on {_port.PortName}");

        try
        {
            int count = _port.BytesToRead;
            Log($"Bytes available: {count}");

            if (count <= 0)
                return;

            byte[] data = new byte[count];
            int bytesRead = _port.Read(data, 0, count);

            Log($"RAW RX ({bytesRead} bytes): " +
                $"{BitConverter.ToString(data, 0, bytesRead)}");

            lock (_buffer)
            {
                _buffer.AddRange(data);
                ProcessBuffer();
            }
        }
        catch (Exception ex)
        {
            Log($"RECEIVE ERROR: {ex}");
        }
    }
    // ============================================================
    // MODBUS FRAME PARSER
    // ============================================================

    private void ProcessBuffer()
    {
        while (_buffer.Count >= 9)
        {
            int start = FindResponseStart();

            if (start < 0)
            {
                if (_buffer.Count > 8)
                {
                    _buffer.RemoveRange(
                        0,
                        _buffer.Count - 8
                    );
                }

                return;
            }

            if (start > 0)
            {
                _buffer.RemoveRange(0, start);
            }

            if (_buffer.Count < 9)
                return;

            byte slaveId = _buffer[0];

            byte function = _buffer[1];

            byte byteCount = _buffer[2];

            if (function != 0x03 ||
                byteCount != 0x04)
            {
                _buffer.RemoveAt(0);
                continue;
            }

            byte[] frame = _buffer
                .Take(9)
                .ToArray();

            if (!ValidateCrc(frame))
            {
                _buffer.RemoveAt(0);
                continue;
            }else
            {
                Log($"VALID FRAME: Address={slaveId}, Bytes={BitConverter.ToString(frame)}");
            }

            // ====================================================
            // IMPORTANT:
            // Frame is valid.
            // Now check whether this Modbus address is registered.
            // ====================================================

            if (!IsAddressAllowed(slaveId))
            {
                // Valid Modbus frame, but not registered.
                // Ignore it completely.
                _buffer.RemoveRange(0, 9);

                continue;
            }

            uint rawValue =
                ((uint)frame[3] << 24) |
                ((uint)frame[4] << 16) |
                ((uint)frame[5] << 8) |
                frame[6];

            double pressure =
                rawValue / 1000.0;

            var deviceData =
                new ModbusDeviceData
                {
                    ModbusAddress = slaveId,

                    RawValue = rawValue,

                    Pressure = pressure,

                    LastSeen = DateTime.Now
                };

            DeviceDataReceived?.Invoke(deviceData);

            _buffer.RemoveRange(0, 9);
        }
    }

    // ============================================================
    // FIND FRAME
    // ============================================================

    private int FindResponseStart()
    {
        for (
            int i = 0;
            i <= _buffer.Count - 3;
            i++)
        {
            byte slaveId = _buffer[i];

            byte function = _buffer[i + 1];

            byte byteCount = _buffer[i + 2];

            if (slaveId >= 1 &&
                slaveId <= 247 &&
                function == 0x03 &&
                byteCount == 0x04)
            {
                return i;
            }
        }

        return -1;
    }

    // ============================================================
    // CRC
    // ============================================================

    private static bool ValidateCrc(byte[] frame)
    {
        ushort calculated =
            CalculateModbusCrc(
                frame,
                0,
                frame.Length - 2
            );

        ushort received =
            (ushort)(
                frame[^2] |
                (frame[^1] << 8)
            );

        return calculated == received;
    }

    private static ushort CalculateModbusCrc(
        byte[] data,
        int offset,
        int length)
    {
        ushort crc = 0xFFFF;

        for (
            int i = offset;
            i < offset + length;
            i++)
        {
            crc ^= data[i];

            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x0001) != 0)
                {
                    crc =
                        (ushort)(
                            (crc >> 1) ^ 0xA001
                        );
                }
                else
                {
                    crc >>= 1;
                }
            }
        }

        return crc;
    }

    // ============================================================
    // DISPOSE
    // ============================================================

    public void Dispose()
    {
        Stop();

        _port.DataReceived -= OnDataReceived;

        _port.Dispose();
    }


    
    private readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        "VacuumPumpSystem",
        "modbus-rx.log");

    private void Log(string message)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_logPath);

            if (directory != null)
                Directory.CreateDirectory(directory);

            File.AppendAllText(
                _logPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} " +
                $"{message}{Environment.NewLine}");
        }
        catch
        {
            // Prevent diagnostic logging errors from breaking reception.
        }
    }


    
    public async Task RunReceiveDiagnosticAsync(CancellationToken cancellationToken = default)
    {
        Log($"POLL TEST STARTED on {_port.PortName}");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!_port.IsOpen)
                {
                    Log("ERROR: Serial port is closed.");
                    return;
                }

                int count = _port.BytesToRead;

                if (count > 0)
                {
                    byte[] data = new byte[count];
                    int bytesRead = _port.Read(data, 0, count);

                    Log($"POLL RX ({bytesRead} bytes): " +
                        BitConverter.ToString(data, 0, bytesRead));
                }
            }
            catch (Exception ex)
            {
                Log($"POLL ERROR: {ex}");
                return;
            }

            await Task.Delay(200, cancellationToken);
        }
    }

}