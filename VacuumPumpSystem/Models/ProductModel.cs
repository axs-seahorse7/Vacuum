
namespace VacuumPumpSystem.Models;

public sealed class ProductModel
{
    public int Id { get; set; }

    public string ModelName { get; set; } = string.Empty;

    public string SerialPrefix { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public override string ToString() => ModelName;
}
