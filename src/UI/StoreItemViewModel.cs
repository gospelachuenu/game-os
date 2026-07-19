using System.Windows.Media;

namespace UI;

/// <summary>
/// A browsable-but-not-installed game shown in the dashboard's Store row. This is
/// deliberately NOT backed by ConsoleLibrary.GameEntry/GameLibraryDatabase — those
/// model the user's actual owned/installed library (per plan.md §4.1's schema), and
/// a not-installed storefront listing is a different concept the real backend
/// doesn't have a table for yet. Kept UI-only until a real Store data source exists.
/// </summary>
public sealed class StoreItemViewModel
{
    public required string Name { get; init; }
    public required string PriceLabel { get; init; }
    public required Brush ArtBrush { get; init; }

    public static StoreItemViewModel Create(string name, string priceLabel) => new()
    {
        Name = name,
        PriceLabel = priceLabel,
        ArtBrush = TileArtGenerator.GenerateGradient(name),
    };
}
