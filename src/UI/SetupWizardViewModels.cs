using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace UI;

/// <summary>Shared change-notification plumbing for the wizard's row view models.</summary>
public abstract class SetupRowBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(name);
        return true;
    }

    private bool _isFocused;

    /// <summary>True when the controller highlight is on this row.</summary>
    public bool IsFocused
    {
        get => _isFocused;
        set => Set(ref _isFocused, value);
    }
}

public sealed class LanguageRowViewModel : SetupRowBase
{
    public required string Label { get; init; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}

public sealed class NetworkRowViewModel : SetupRowBase
{
    private static readonly Brush LitBrush =
        new SolidColorBrush(Color.FromRgb(0xA8, 0xE0, 0x3B));

    private static readonly Brush DimBrush =
        new SolidColorBrush(Color.FromRgb(0x5A, 0x60, 0x6A));

    static NetworkRowViewModel()
    {
        LitBrush.Freeze();
        DimBrush.Freeze();
    }

    public required WirelessNetwork Network { get; init; }

    public string Ssid => Network.Ssid;

    /// <summary>
    /// "Connected" wins over "Saved" wins over "Secured" — the most useful thing to
    /// know about a row, not a list of every attribute.
    /// </summary>
    public string StatusLabel =>
        IsConnected ? "Connected"
        : Network.IsKnown ? "Saved"
        : Network.IsSecured ? "Secured"
        : "Open";

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (Set(ref _isConnected, value))
            {
                Raise(nameof(StatusLabel));
            }
        }
    }

    // Bound individually rather than through a converter so the template stays
    // declarative — four bars is few enough that this is simpler than the alternative.
    public Brush Bar1Brush => Network.SignalBars >= 1 ? LitBrush : DimBrush;
    public Brush Bar2Brush => Network.SignalBars >= 2 ? LitBrush : DimBrush;
    public Brush Bar3Brush => Network.SignalBars >= 3 ? LitBrush : DimBrush;
    public Brush Bar4Brush => Network.SignalBars >= 4 ? LitBrush : DimBrush;
}

public sealed class ControllerRowViewModel : SetupRowBase
{
    public required int SlotNumber { get; init; }

    public string DisplayName => $"Controller {SlotNumber + 1}";

    private ControllerSlotState _state = ControllerSlotState.NotConnected;
    public ControllerSlotState State
    {
        get => _state;
        set
        {
            if (Set(ref _state, value))
            {
                Raise(nameof(StatusLabel));
                Raise(nameof(IsConfirmed));
            }
        }
    }

    public bool IsConfirmed => State == ControllerSlotState.Confirmed;

    public string StatusLabel => State switch
    {
        ControllerSlotState.Confirmed => "✓ Ready",
        ControllerSlotState.Connected => "Press any button",
        _ => "Searching…",
    };
}

public sealed class PinKeyViewModel : SetupRowBase
{
    public required string Label { get; init; }

    /// <summary>The digit this key enters; null for Delete and for the spacer.</summary>
    public required int? Digit { get; init; }

    /// <summary>
    /// True for the empty cell left of 0. It occupies a grid position so the keypad
    /// keeps its 3-wide shape, but it draws nothing and D-pad focus skips past it.
    /// </summary>
    public bool IsSpacer { get; init; }
}
