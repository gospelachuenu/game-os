using System.Windows;
using InputDaemon;

namespace UI;

/// <summary>
/// Translates a BatteryUiState into the visibility of 4 discrete fill segments for a
/// physical battery icon (outline + notch + segmented bars), replacing plain
/// "Battery: X" text per plan.md §5.4's "multi-segmented status icon" spec.
/// </summary>
public sealed class BatteryIconViewModel
{
    public Visibility Segment0 { get; }
    public Visibility Segment1 { get; }
    public Visibility Segment2 { get; }
    public Visibility Segment3 { get; }
    public string Label { get; }

    public BatteryIconViewModel(BatteryUiState state)
    {
        var litCount = state switch
        {
            BatteryUiState.Full => 4,
            BatteryUiState.Medium => 3,
            BatteryUiState.Low => 2,
            BatteryUiState.Critical => 1,
            BatteryUiState.Charging => 4,
            _ => 0,
        };

        Segment0 = litCount >= 1 ? Visibility.Visible : Visibility.Collapsed;
        Segment1 = litCount >= 2 ? Visibility.Visible : Visibility.Collapsed;
        Segment2 = litCount >= 3 ? Visibility.Visible : Visibility.Collapsed;
        Segment3 = litCount >= 4 ? Visibility.Visible : Visibility.Collapsed;

        Label = state switch
        {
            BatteryUiState.Charging => "Charging",
            BatteryUiState.Critical => "Low",
            BatteryUiState.Unknown => "No controller",
            _ => state.ToString(),
        };
    }
}
