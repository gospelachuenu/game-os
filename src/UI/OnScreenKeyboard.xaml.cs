using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace UI;

/// <summary>
/// One key on the on-screen keyboard.
/// </summary>
public sealed class KeyboardKey : INotifyPropertyChanged
{
    /// <summary>What pressing this key does.</summary>
    public enum KeyAction
    {
        /// <summary>Types <see cref="Character"/>.</summary>
        Character,
        Backspace,
        Shift,
        Space,
        Done,
        Cancel,
    }

    public required KeyAction Action { get; init; }

    /// <summary>The character typed, for <see cref="KeyAction.Character"/> keys.</summary>
    public string Character { get; set; } = string.Empty;

    private string _label = string.Empty;

    /// <summary>What is drawn on the key cap.</summary>
    public string Label
    {
        get => _label;
        set
        {
            _label = value;
            OnPropertyChanged(nameof(Label));
        }
    }

    /// <summary>Key width. Wider for the likes of Space and Done.</summary>
    public double Width { get; init; } = 52;

    private bool _isFocused;

    public bool IsFocused
    {
        get => _isFocused;
        set
        {
            _isFocused = value;
            OnPropertyChanged(nameof(IsFocused));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Controller-driven text entry, for the places the console needs text it cannot get any
/// other way — a web address, a wifi password, a search term.
///
/// A fixed grid navigated with the D-pad rather than a phone-style predictive keyboard:
/// from a sofa, a layout you can learn the shape of beats one that rearranges itself.
///
/// Deliberately generic. The browser's address bar is its first user, but wifi passwords
/// need exactly the same thing, so it takes a prompt and returns a string rather than
/// knowing anything about who asked.
/// </summary>
public partial class OnScreenKeyboard : UserControl
{
    /// <summary>Raised when the user finishes. Carries the typed text.</summary>
    public event EventHandler<string>? Accepted;

    /// <summary>Raised when the user backs out without finishing.</summary>
    public event EventHandler? Cancelled;

    /// <summary>The rows of keys, as the grid renders them.</summary>
    private readonly ObservableCollection<ObservableCollection<KeyboardKey>> _rows = new();

    private string _entry = string.Empty;
    private bool _shifted;
    private int _row;
    private int _column;

    /// <summary>True while the keyboard is up and owns controller input.</summary>
    public bool IsOpen => Visibility == Visibility.Visible;

    public OnScreenKeyboard()
    {
        InitializeComponent();
        BuildKeys();
        KeyRows.ItemsSource = _rows;
    }

    /// <summary>
    /// Shows the keyboard.
    /// </summary>
    /// <param name="prompt">What is being asked for, e.g. "Enter address".</param>
    /// <param name="initialText">Pre-filled text, so editing an existing value does not start from nothing.</param>
    public void Show(string prompt, string initialText = "")
    {
        PromptText.Text = prompt.ToUpperInvariant();
        _entry = initialText;
        _shifted = false;
        ApplyShift();
        UpdateEntry();

        // Always start on a letter rather than wherever it was left — the top-left of the
        // grid is the one position a user can predict.
        _row = 0;
        _column = 0;
        RefreshFocus();

        Visibility = Visibility.Visible;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.2)));
    }

    public void Hide()
    {
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromSeconds(0.18));
        fade.Completed += (_, _) => Visibility = Visibility.Collapsed;
        BeginAnimation(OpacityProperty, fade);
    }

    // ---------------- navigation ----------------

    public void MoveVertical(int delta)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        _row = Math.Clamp(_row + delta, 0, _rows.Count - 1);

        // Rows differ in length, so keep the column in range rather than losing the
        // position entirely when moving onto a shorter row.
        _column = Math.Clamp(_column, 0, _rows[_row].Count - 1);

        RefreshFocus();
    }

    public void MoveHorizontal(int delta)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        var row = _rows[_row];
        _column = Math.Clamp(_column + delta, 0, row.Count - 1);
        RefreshFocus();
    }

    /// <summary>Presses the focused key.</summary>
    public void Activate()
    {
        if (_rows.Count == 0)
        {
            return;
        }

        Press(_rows[_row][_column]);
    }

    /// <summary>
    /// Appends text typed on a REAL keyboard.
    ///
    /// The on-screen keyboard is for consoles without one; where a keyboard exists it
    /// would be perverse to make someone hunt keys with a D-pad instead.
    /// </summary>
    public void Type(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        _entry += text;
        UpdateEntry();
    }

    /// <summary>Deletes the last character. Bound to X, so it needs no key press.</summary>
    public void Backspace()
    {
        if (_entry.Length > 0)
        {
            _entry = _entry[..^1];
            UpdateEntry();
        }
    }

    /// <summary>Toggles capitals. Bound to Y.</summary>
    public void ToggleShift()
    {
        _shifted = !_shifted;
        ApplyShift();
    }

    /// <summary>Finishes and hands back the text.</summary>
    public void Accept()
    {
        Hide();
        Accepted?.Invoke(this, _entry);
    }

    public void Cancel()
    {
        Hide();
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void Press(KeyboardKey key)
    {
        switch (key.Action)
        {
            case KeyboardKey.KeyAction.Character:
                _entry += key.Character;
                UpdateEntry();

                // Shift is one-shot, as on a real keyboard: it applies to the next letter
                // and then releases, rather than latching on and surprising the user.
                if (_shifted)
                {
                    _shifted = false;
                    ApplyShift();
                }

                break;

            case KeyboardKey.KeyAction.Backspace:
                Backspace();
                break;

            case KeyboardKey.KeyAction.Shift:
                ToggleShift();
                break;

            case KeyboardKey.KeyAction.Space:
                _entry += " ";
                UpdateEntry();
                break;

            case KeyboardKey.KeyAction.Done:
                Accept();
                break;

            case KeyboardKey.KeyAction.Cancel:
                Cancel();
                break;
        }
    }

    private void UpdateEntry() => EntryText.Text = _entry;

    private void RefreshFocus()
    {
        for (var r = 0; r < _rows.Count; r++)
        {
            for (var c = 0; c < _rows[r].Count; c++)
            {
                _rows[r][c].IsFocused = r == _row && c == _column;
            }
        }
    }

    /// <summary>Redraws the letter keys in the current case.</summary>
    private void ApplyShift()
    {
        foreach (var row in _rows)
        {
            foreach (var key in row)
            {
                if (key.Action != KeyboardKey.KeyAction.Character)
                {
                    continue;
                }

                if (key.Character.Length == 1 && char.IsLetter(key.Character[0]))
                {
                    key.Character = _shifted
                        ? key.Character.ToUpperInvariant()
                        : key.Character.ToLowerInvariant();
                    key.Label = key.Character;
                }
            }
        }
    }

    /// <summary>
    /// Builds the layout.
    ///
    /// The bottom row carries the punctuation a web address actually needs — dot, slash,
    /// dash, colon — because hunting for those on a generic keyboard is the slowest part
    /// of typing a URL with a controller.
    /// </summary>
    private void BuildKeys()
    {
        AddRow("1234567890");
        AddRow("qwertyuiop");
        AddRow("asdfghjkl");
        AddRow("zxcvbnm");
        AddRow(".-_/:@");

        _rows.Add(new ObservableCollection<KeyboardKey>
        {
            new() { Action = KeyboardKey.KeyAction.Shift, Label = "Shift", Width = 92 },
            new() { Action = KeyboardKey.KeyAction.Space, Label = "Space", Width = 220 },
            new() { Action = KeyboardKey.KeyAction.Backspace, Label = "Delete", Width = 92 },
            new() { Action = KeyboardKey.KeyAction.Cancel, Label = "Cancel", Width = 92 },
            new() { Action = KeyboardKey.KeyAction.Done, Label = "Go", Width = 92 },
        });
    }

    private void AddRow(string characters)
    {
        var row = new ObservableCollection<KeyboardKey>();

        foreach (var c in characters)
        {
            var text = c.ToString();
            row.Add(new KeyboardKey
            {
                Action = KeyboardKey.KeyAction.Character,
                Character = text,
                Label = text,
            });
        }

        _rows.Add(row);
    }

    private void Key_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: KeyboardKey key })
        {
            // Clicking also moves focus, so the highlight follows the mouse rather than
            // being left behind wherever the controller last was.
            for (var r = 0; r < _rows.Count; r++)
            {
                var c = _rows[r].IndexOf(key);
                if (c >= 0)
                {
                    _row = r;
                    _column = c;
                    RefreshFocus();
                    break;
                }
            }

            Press(key);
        }
    }
}
