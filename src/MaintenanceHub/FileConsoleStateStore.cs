using System.Text;
using System.Text.Json;

namespace MaintenanceHub;

/// <summary>
/// File-backed console state, written to a directory that the Unified Write Filter has
/// been told to leave alone (`uwfmgr file add-exclusion`).
///
/// Format is a flat JSON object. Deliberately not the registry: UWF protects the
/// registry too and would need a separate exclusion, and a single visible file is far
/// easier to inspect, back up, or delete when something goes wrong on a machine with
/// no desktop.
///
/// DURABILITY
///
/// Writes go to a temporary file which then REPLACES the real one, rather than being
/// written over it in place. A console can lose power at any moment — mid-update is
/// precisely when it is most likely, since that is when the machine reboots — and a
/// half-written state file would take out the parent PIN and the setup marker
/// together. Replace is atomic, so the file on disk is always either the complete old
/// version or the complete new one, never a torn mixture.
/// </summary>
public sealed class FileConsoleStateStore : IConsoleStateStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, string> _values = new();
    private bool _loaded;

    /// <summary>
    /// Where console state lives on the real machine. This exact directory is the one
    /// registered with the write filter; changing it without also changing the
    /// exclusion will produce state that silently vanishes on every reboot.
    /// </summary>
    public const string DefaultDirectory = @"C:\GamingOS\State";

    private const string FileName = "console-state.json";

    public FileConsoleStateStore(string? directory = null)
    {
        _path = Path.Combine(directory ?? DefaultDirectory, FileName);
    }

    public bool IsAvailable
    {
        get
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                return dir is not null && (Directory.Exists(dir) || Directory.CreateDirectory(dir).Exists);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public string? Get(string key)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _values.TryGetValue(key, out var value) ? value : null;
        }
    }

    public void Set(string key, string value)
    {
        lock (_gate)
        {
            EnsureLoaded();
            _values[key] = value;
            Save();
        }
    }

    public void Remove(string key)
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (_values.Remove(key))
            {
                Save();
            }
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var json = File.ReadAllText(_path, Encoding.UTF8);
            _values = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or unreadable state file must not take the console down. Start
            // empty: the cost is that setup runs again and the PIN needs re-setting,
            // which is recoverable. Refusing to boot is not.
            _values = new();
        }
    }

    private void Save()
    {
        var dir = Path.GetDirectoryName(_path);

        try
        {
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true });

            // Write-then-replace, so a power cut cannot leave a half-written file.
            var temp = _path + ".tmp";
            File.WriteAllText(temp, json, Encoding.UTF8);

            if (File.Exists(_path))
            {
                File.Replace(temp, _path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temp, _path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Losing a write is bad but not fatal — the in-memory value still applies
            // for this session. Throwing here would crash the console over a setting.
        }
    }
}

/// <summary>
/// In-memory state for tests and for the windowed dev build, where writing to
/// C:\GamingOS\State is neither possible nor desirable.
/// </summary>
public sealed class InMemoryConsoleStateStore : IConsoleStateStore
{
    private readonly Dictionary<string, string> _values = new();

    public bool IsAvailable => true;

    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;

    public void Set(string key, string value) => _values[key] = value;

    public void Remove(string key) => _values.Remove(key);
}
