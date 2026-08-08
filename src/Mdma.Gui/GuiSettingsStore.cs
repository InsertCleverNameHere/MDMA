using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mdma.Core;

namespace Mdma.Gui;

public sealed record GuiSettings(string? WorkingRootOverride = null);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(GuiSettings))]
public partial class GuiJsonContext : JsonSerializerContext { }

public interface IGuiSettingsStore
{
    GuiSettings LoadSettings(WorkingRoot workingRoot);
    void SaveSettings(WorkingRoot workingRoot, GuiSettings settings);
}

public sealed class GuiSettingsStore : IGuiSettingsStore
{
    private const string SettingsFileName = "gui-settings.json";

    public GuiSettings LoadSettings(WorkingRoot workingRoot)
    {
        var path = Path.Combine(workingRoot.Path, SettingsFileName);
        if (!File.Exists(path))
            return new GuiSettings();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, GuiJsonContext.Default.GuiSettings)
                ?? new GuiSettings();
        }
        catch
        {
            return new GuiSettings();
        }
    }

    public void SaveSettings(WorkingRoot workingRoot, GuiSettings settings)
    {
        try
        {
            var path = Path.Combine(workingRoot.Path, SettingsFileName);
            var json = JsonSerializer.Serialize(settings, GuiJsonContext.Default.GuiSettings);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Best-effort write
        }
    }
}
