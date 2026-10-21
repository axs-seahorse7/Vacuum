
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using VacuumPump.Communication.Models;

namespace VacuumPump.Communication.Services;

public class ModbusReceiver : IDisposable
{
    private readonly SerialPort _port;
    private readonly List<byte> _buffer = new();
    private readonly object _bufferLock = new();
    private readonly object _allowedAddressesLock = new();
    private readonly HashSet<byte> _allowedAddresses = new();

    private readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        "VacuumPumpSystem",
        "modbus-rx.log");

    // Keep the existing pressure scaling for comparison.
    // Confirm this against the production register documentation.
    public double PressureScaleDivisor { get; set; } = 1000.0;

    public event Action<ModbusDeviceData>? DeviceDataReceived;

    public bool IsOpen => _port.IsOpen;

    public ModbusReceiver(string portName, int baudRate = 115200)
    {
        _port = new SerialPort(
            portName,
            baudRate,
            Parity.None,
            8,
            StopBits.One)
        {
            ReadTimeout = 1000
        };

        _port.DataReceived += OnDataReceived;
    }

    public void SetAllowedAddresses(IEnumerable<byte> addresses)
    {
        lock (_allowedAddressesLock)
        {
            _allowedAddresses.Clear();

            foreach (byte address in addresses)
            {
                if (address is >= 1 and <= 247)
                    _allowedAddresses.Add(address);
            }
        }
    }

    private bool IsAddressAllowed(byte address)
    {
        lock (_allowedAddressesLock)
        {
            // Empty filter allows all addresses during discovery.
            return _allowedAddresses.Count == 0 ||
                   _allowedAddresses.Contains(address);
        }
    }

    public void Start()
    {
        Log($"START requested. Port={_port.PortName}");

        if (_port.IsOpen)
            return;

        _port.Open();
        _port.DiscardInBuffer();

        lock (_bufferLock)
            _buffer.Clear();

        Log($"PORT OPEN. Port={_port.PortName}, " +
            $"Baud={_port.BaudRate}, Format=8N1");
    }

    public void Stop()
    {
        if (_port.IsOpen)
            _port.Close();

        lock (_bufferLock)
            _buffer.Clear();

        Log("PORT STOPPED.");
    }

    private void OnDataReceived(
        object? sender,
        SerialDataReceivedEventArgs e)
    {
        try
        {
            while (_port.IsOpen && _port.BytesToRead > 0)
            {
                int count = _port.BytesToRead;

                if (count <= 0)
                    break;

                byte[] data = new byte[count];
                int bytesRead = _port.Read(data, 0, count);

                if (bytesRead <= 0)
                    break;

                lock (_bufferLock)
                {
                    _buffer.AddRange(data.Take(bytesRead));
                    ProcessBuffer();
                }
            }
        }
        catch (Exception ex)
        {
            Log($"RX ERROR: {ex.Message}");
        }
    }

    private void ProcessBuffer()
    {
        while (_buffer.Count >= 5)
        {
            int start = FindValidFrameStart();

            if (start < 0)
            {
                // Retain enough trailing bytes for a fragmented frame.
                if (_buffer.Count > 8)
                    _buffer.RemoveRange(0, _buffer.Count - 8);

                return;
            }

            if (start > 0)
                _buffer.RemoveRange(0, start);

            if (_buffer.Count < 2)
                return;

            byte slaveId = _buffer[0];
            byte function = _buffer[1];

            // Modbus exception response: address, function|0x80,
            // exception code, CRC low, CRC high.
            if (function == 0x83)
            {
                if (_buffer.Count < 5)
                    return;

                byte[] exceptionFrame = _buffer.Take(5).ToArray();

                if (!ValidateCrc(exceptionFrame))
                {
                    _buffer.RemoveAt(0);
                    continue;
                }

                Log($"MODBUS EXCEPTION: Slave={slaveId}, " +
                    $"Code=0x{exceptionFrame[2]:X2}, " +
                    $"Frame={Hex(exceptionFrame)}");

                _buffer.RemoveRange(0, 5);
                continue;
            }

            if (function != 0x03)
            {
                _buffer.RemoveAt(0);
                continue;
            }

            if (_buffer.Count < 3)
                return;

            byte byteCount = _buffer[2];

            // Current observed production samples contain 2 data bytes.
            // Preserve support for the previous 4-byte response format.
            if (byteCount is not (2 or 4))
            {
                _buffer.RemoveAt(0);
                continue;
            }

            // Address + function + byte count + data + CRC.
            int frameLength = 3 + byteCount + 2;

            if (_buffer.Count < frameLength)
                return;

            byte[] frame = _buffer.Take(frameLength).ToArray();

            if (!ValidateCrc(frame))
            {
                Log($"CRC INVALID: {Hex(frame)}");
                _buffer.RemoveAt(0);
                continue;
            }

            _buffer.RemoveRange(0, frameLength);

            uint rawValue = 0;

            for (int i = 0; i < byteCount; i++)
                rawValue = (rawValue << 8) | frame[3 + i];

            Log($"VALID MODBUS: Time={DateTime.Now:HH:mm:ss.fff}, " +
                $"Slave={slaveId}, Function=03, " +
                $"ByteCount={byteCount}, " +
                $"RegisterBytes={Hex(frame.Skip(3).Take(byteCount).ToArray())}, " +
                $"RawValue={rawValue}, " +
                $"Frame={Hex(frame)}");

            if (!IsAddressAllowed(slaveId))
            {
                Log($"IGNORED: Slave {slaveId} is not registered.");
                continue;
            }

            if (PressureScaleDivisor <= 0)
            {
                Log("ERROR: PressureScaleDivisor must be positive.");
                continue;
            }

            double pressure = rawValue / PressureScaleDivisor;

            var deviceData = new ModbusDeviceData
            {
                ModbusAddress = slaveId,
                RawValue = rawValue,
                Pressure = pressure,
                LastSeen = DateTime.Now
            };

            try
            {
                DeviceDataReceived?.Invoke(deviceData);
            }
            catch (Exception ex)
            {
                Log($"DATA EVENT ERROR: {ex.Message}");
            }
        }
    }

    private int FindValidFrameStart()
    {
        for (int i = 0; i <= _buffer.Count - 5; i++)
        {
            byte slaveId = _buffer[i];
            byte function = _buffer[i + 1];

            if (slaveId is < 1 or > 247)
                continue;

            if (function == 0x83)
            {
                if (_buffer.Count - i < 5)
                    continue;

                byte[] exceptionFrame = _buffer
                    .Skip(i)
                    .Take(5)
                    .ToArray();

                if (ValidateCrc(exceptionFrame))
                    return i;

                continue;
            }

            if (function != 0x03)
                continue;

            byte byteCount = _buffer[i + 2];

            if (byteCount is not (2 or 4))
                continue;

            int frameLength = 3 + byteCount + 2;

            if (_buffer.Count - i < frameLength)
                continue;

            byte[] candidate = _buffer
                .Skip(i)
                .Take(frameLength)
                .ToArray();

            if (ValidateCrc(candidate))
                return i;
        }

        return -1;
    }

    private static bool ValidateCrc(byte[] frame)
    {
        if (frame.Length < 5)
            return false;

        ushort calculated = CalculateModbusCrc(
            frame, 0, frame.Length - 2);

        ushort received = (ushort)(
            frame[^2] | (frame[^1] << 8));

        return calculated == received;
    }

    private static ushort CalculateModbusCrc(
        byte[] data, int offset, int length)
    {
        ushort crc = 0xFFFF;

        for (int i = offset; i < offset + length; i++)
        {
            crc ^= data[i];

            for (int j = 0; j < 8; j++)
            {
                crc = (crc & 1) != 0
                    ? (ushort)((crc >> 1) ^ 0xA001)
                    : (ushort)(crc >> 1);
            }
        }

        return crc;
    }

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
            // Diagnostic logging must not interrupt serial reception.
        }
    }

    private static string Hex(byte[] bytes) =>
        BitConverter.ToString(bytes);

    public void Dispose()
    {
        _port.DataReceived -= OnDataReceived;

        if (_port.IsOpen)
            _port.Close();

        _port.Dispose();

        lock (_bufferLock)
            _buffer.Clear();
    }
}
