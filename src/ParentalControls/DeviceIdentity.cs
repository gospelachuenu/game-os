using System.Security.Cryptography;
using MaintenanceHub;

namespace ParentalControls;

/// <summary>
/// How a console is being used. Set at pairing/setup and rarely changed.
///
/// This exists because the console is distributed to SEVERAL different people, not all
/// of them locked-down children. Parental control is therefore a MODE the device is in,
/// not a baked-in feature — an unrestricted console has no gating, no PIN, no approval
/// queue, and the whole parental layer stays dormant.
/// </summary>
public enum ControlMode
{
    /// <summary>
    /// A normal, open console — the DEFAULT for a fresh device. Every game launches,
    /// no PIN, no restrictions. What an adult recipient gets.
    /// </summary>
    Unrestricted,

    /// <summary>
    /// Set up as a child's console. The full parental layer is active: the launch gate,
    /// time controls, the PIN, the approval queue, and (once paired) portal control.
    /// </summary>
    ChildDevice,
}

/// <summary>
/// A console's stable identity: a random id generated once and persisted, plus the
/// control mode. The id is what a web portal keys on, so that when several consoles are
/// distributed each is distinguishable and its policy belongs to that unit alone.
///
/// The id is NOT derived from hardware (MAC, disk serial) on purpose: hardware ids can
/// change (a swapped part, a re-image) and can be spoofed, and tying identity to them
/// makes a console un-recoverable if the part changes. A stored random id is stable
/// across everything except a deliberate factory reset.
/// </summary>
public sealed class DeviceIdentity
{
    private const string IdKey = "device.id";
    private const string ModeKey = "device.control_mode";

    private readonly IConsoleStateStore _state;

    public DeviceIdentity(IConsoleStateStore state) => _state = state;

    /// <summary>
    /// The console's stable id, generated and stored on first access. Formatted as a
    /// short, human-shareable code (e.g. for reading out during support) rather than a
    /// raw GUID.
    /// </summary>
    public string Id
    {
        get
        {
            var existing = _state.Get(IdKey);
            if (!string.IsNullOrEmpty(existing))
            {
                return existing;
            }

            var id = GenerateId();
            _state.Set(IdKey, id);
            return id;
        }
    }

    /// <summary>The current control mode, defaulting to Unrestricted on a fresh console.</summary>
    public ControlMode Mode
    {
        get => _state.Get(ModeKey) == nameof(ControlMode.ChildDevice)
            ? ControlMode.ChildDevice
            : ControlMode.Unrestricted;
        set => _state.Set(ModeKey, value.ToString());
    }

    public bool IsChildDevice => Mode == ControlMode.ChildDevice;

    /// <summary>
    /// A 12-character id from a crypto RNG, grouped for readability (e.g. "K7QM-3F9X-P2WD").
    /// Uses an unambiguous alphabet (no 0/O, 1/I) so it can be read aloud without
    /// confusion.
    /// </summary>
    private static string GenerateId()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<byte> bytes = stackalloc byte[12];
        RandomNumberGenerator.Fill(bytes);

        var chars = new char[14]; // 12 chars + 2 dashes
        var pos = 0;
        for (var i = 0; i < 12; i++)
        {
            if (i > 0 && i % 4 == 0)
            {
                chars[pos++] = '-';
            }
            chars[pos++] = alphabet[bytes[i] % alphabet.Length];
        }

        return new string(chars, 0, pos);
    }
}
