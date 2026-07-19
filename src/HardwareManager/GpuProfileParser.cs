using System.Text.Json;

namespace HardwareManager;

public static class GpuProfileParser
{
    public static GpuProfile? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<GpuProfile>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static GpuProfile Load(string path)
    {
        var json = File.ReadAllText(path);
        var profile = Parse(json);
        return profile ?? throw new FormatException($"Failed to parse GPU profile at '{path}'.");
    }
}
