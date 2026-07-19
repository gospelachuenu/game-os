using System.Collections.ObjectModel;

namespace UI;

/// <summary>
/// A single navigable dashboard row (Recently Played / Downloads / Library). Each row
/// owns its own GameTileViewModel instances so focus state never leaks between rows
/// showing the same underlying game.
/// </summary>
public sealed class DashboardRowViewModel
{
    public string Title { get; }
    public bool IsNavigable { get; }
    public ObservableCollection<GameTileViewModel> Tiles { get; } = new();

    public DashboardRowViewModel(string title, bool isNavigable)
    {
        Title = title;
        IsNavigable = isNavigable;
    }
}
