namespace VacuumPump.Communication.Models;

public class ModbusDeviceData
{
    public byte ModbusAddress { get; init; }

    public uint RawValue { get; init; }

    public double Pressure { get; init; }

    public DateTime LastSeen { get; init; }

    public string Status { get; init; } = "OFFLINE";

    public bool IsRegistered { get; init; }

    public string? DummyCode { get; init; }

    public string? MachineName { get; init; }

}