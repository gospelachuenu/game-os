using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace UI;

/// <summary>
/// An application on the console — YouTube today, a store or media app later.
///
/// Deliberately separate from a game rather than folded into the library: apps are not
/// installed, uninstalled, updated or parent-gated the way games are, and they open a
/// console-owned screen rather than launching an external process. Sharing the type
/// would mean every game code path growing an "unless it is really an app" branch.
/// </summary>
public sealed class ConsoleApp : INotifyPropertyChanged
{
    /// <summary>What opening this app actually does.</summary>
    public enum AppKind
    {
        /// <summary>Opens the built-in browser at a fixed address.</summary>
        Web,
    }

    public required string Name { get; init; }
    public required AppKind Kind { get; init; }

    /// <summary>For <see cref="AppKind.Web"/>: the address to open.</summary>
    public string? Url { get; init; }

    /// <summary>Short label shown under the tile while loading, e.g. "Videos".</summary>
    public required string Category { get; init; }

    /// <summary>The tile's background — brand colour, so apps are recognisable at a glance.</summary>
    public required Brush Art { get; init; }

    /// <summary>Foreground used for the glyph and name on top of <see cref="Art"/>.</summary>
    public required Brush Foreground { get; init; }

    /// <summary>Drawn on the tile. A path rather than an emoji so it stays crisp when scaled.</summary>
    public required Geometry Glyph { get; init; }

    private bool _isFocused;

    /// <summary>True while the controller highlight is on this tile.</summary>
    public bool IsFocused
    {
        get => _isFocused;
        set
        {
            if (_isFocused != value)
            {
                _isFocused = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>
    /// <summary>
    /// TEMPORARY: the browser and YouTube are disabled for now, on purpose.
    ///
    /// This is the "before" state for testing the over-the-air update: the first published
    /// update flips this to false, and the browser tile/rail button reappearing is the
    /// visible proof that the update actually applied. Flip this and re-publish to complete
    /// that test; then this flag can go.
    /// </summary>
    public const bool BrowserAndYouTubeDisabled = true;

    /// <summary>
    /// The apps this console ships with.
    ///
    /// YouTube is first and, for now, alone — the drawer exists so that adding the next
    /// one is a list entry rather than a layout change. Empty while
    /// <see cref="BrowserAndYouTubeDisabled"/> is set.
    /// </summary>
    public static IReadOnlyList<ConsoleApp> BuiltIn =>
        BrowserAndYouTubeDisabled ? Array.Empty<ConsoleApp>() : AllApps;

    private static readonly IReadOnlyList<ConsoleApp> AllApps = new[]
    {
        new ConsoleApp
        {
            Name = "YouTube",
            Kind = AppKind.Web,
            Url = "https://www.youtube.com/tv",
            Category = "Videos",

            // YouTube red, darkened slightly so white text on it stays comfortable on a
            // bright TV rather than glaring.
            Art = new LinearGradientBrush(
                Color.FromRgb(0xE6, 0x2C, 0x2C),
                Color.FromRgb(0x9B, 0x12, 0x12),
                new System.Windows.Point(0, 0),
                new System.Windows.Point(1, 1)),
            Foreground = Brushes.White,

            // Rounded rectangle with a play triangle knocked out of it. The leading "F0"
            // selects even-odd filling, which is what punches the triangle through
            // instead of filling it solid.
            Glyph = Geometry.Parse(
                "F0 M 8,0 H 40 A 8,8 0 0 1 48,8 V 26 A 8,8 0 0 1 40,34 H 8 " +
                "A 8,8 0 0 1 0,26 V 8 A 8,8 0 0 1 8,0 Z " +
                "M 19,9 V 25 L 33,17 Z"),
        },

    };
}
