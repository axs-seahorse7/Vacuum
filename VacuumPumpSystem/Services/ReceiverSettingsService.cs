using System.IO;
using System.Text.Json;



namespace VacuumPumpSystem.Services;

public class ReceiverSettingsService
{
    private readonly string _settingsDirectory;
    private readonly string _settingsFile;

    public ReceiverSettingsService()
    {
        _settingsDirectory = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "VacuumPumpSystem");

        _settingsFile = Path.Combine(
            _settingsDirectory,
            "receiver-settings.json");
    }

    public string? GetSavedPort(int lineId)
    {
        var settings = Load();

        return settings.LinePorts.TryGetValue(
            lineId,
            out var port)
            ? port
            : null;
    }

    public void SavePort(int lineId, string portName)
    {
        var settings = Load();

        settings.LinePorts[lineId] = portName;

        Directory.CreateDirectory(_settingsDirectory);

        File.WriteAllText(
            _settingsFile,
            JsonSerializer.Serialize(
                settings,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
    }

    private ReceiverSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsFile))
                return new ReceiverSettings();

            string json =
                File.ReadAllText(_settingsFile);

            return JsonSerializer.Deserialize<ReceiverSettings>(
                       json)
                   ?? new ReceiverSettings();
        }
        catch
        {
            return new ReceiverSettings();
        }
    }
}

public class ReceiverSettings
{
    public Dictionary<int, string> LinePorts { get; set; } = new();
}