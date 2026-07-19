namespace UI;

/// <summary>
/// A single icon in the Terminal-concept left nav rail. Only "Library" has a real
/// destination in this test build; the others are placeholders representing where
/// Store/Friends/Settings sections would live in the full console UI.
/// </summary>
public sealed class NavRailItemViewModel
{
    public required string Glyph { get; init; }
    public required bool IsActive { get; init; }
}
