using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UI;

/// <summary>
/// Backs the Settings screen. The theme section is real and functional (drives
/// ThemeManager.Apply, the same system every accent-colored element in the dashboard
/// already reads from). Display/audio sections are visual-only mock sections for
/// this test build, matching how the Guide Menu's volume/backlight sliders are
/// visual-only — there is no real DDC-CI/audio backend wired up yet.
/// </summary>
public sealed class SettingsScreenViewModel : INotifyPropertyChanged
{
    public ObservableCollection<ThemeOptionViewModel> ThemeOptions { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public SettingsScreenViewModel()
    {
        foreach (var theme in DashboardTheme.All)
        {
            ThemeOptions.Add(new ThemeOptionViewModel(theme, ReferenceEquals(theme, ThemeManager.Current)));
        }

        ThemeManager.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(DashboardTheme theme)
    {
        foreach (var option in ThemeOptions)
        {
            option.IsSelected = ReferenceEquals(option.Theme, theme);
        }
    }

    public void SelectTheme(ThemeOptionViewModel option) => ThemeManager.Apply(option.Theme);
}

public sealed class ThemeOptionViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public DashboardTheme Theme { get; }
    public string Name => Theme.Name;
    public System.Windows.Media.Color AccentColor => Theme.AccentPrimary;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public ThemeOptionViewModel(DashboardTheme theme, bool isSelected)
    {
        Theme = theme;
        _isSelected = isSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
