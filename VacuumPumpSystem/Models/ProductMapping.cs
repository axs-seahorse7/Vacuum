
namespace VacuumPumpSystem.Models;

public class ProductMapping
{
    public long Id { get; set; }

    public int MachineId { get; set; }
    public string MachineName { get; set; } = string.Empty;

    public string DummyCode { get; set; } = string.Empty;

    public string ProductCode { get; set; } = string.Empty;

    public DateTime MappedAt { get; set; }

    public DateTime? UnmappedAt { get; set; }
}
