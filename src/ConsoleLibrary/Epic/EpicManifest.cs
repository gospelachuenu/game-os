using System.Text.Json.Serialization;

namespace ConsoleLibrary.Epic;

/// <summary>
/// Subset of the fields present in Epic Games Launcher .item manifest JSON files
/// (C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests\*.item).
/// </summary>
public sealed class EpicManifest
{
    [JsonPropertyName("DisplayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("CatalogItemId")]
    public string? CatalogItemId { get; init; }

    [JsonPropertyName("AppName")]
    public string? AppName { get; init; }

    [JsonPropertyName("InstallLocation")]
    public string? InstallLocation { get; init; }

    [JsonPropertyName("bIsIncompleteInstall")]
    public bool IsIncompleteInstall { get; init; }
}
