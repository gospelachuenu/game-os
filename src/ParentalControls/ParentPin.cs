using System.Security.Cryptography;
using MaintenanceHub;

namespace ParentalControls;

/// <summary>
/// The parent PIN — set once, checked to unlock parental actions, and (via a grace
/// period) not re-demanded for every action in a short session.
///
/// HASHING. A four-digit PIN has only 10,000 possibilities, so a fast hash (a plain
/// SHA-256, as the setup wizard used as a placeholder) is brute-forced instantly.
/// This uses PBKDF2 with a high iteration count and a per-console random salt, so even
/// an attacker with the stored hash has to spend real time per guess. It will never be
/// as strong as a real password — the keyspace is tiny — but it stops the trivial
/// "hash all 10,000 and match" attack, which is the realistic threat on a device a
/// child has physical access to.
///
/// The salt and hash persist through the console's UWF-excluded state store, so a
/// reboot does not wipe the PIN.
/// </summary>
public sealed class ParentPin
{
    private const string HashKey = ConsoleStateKeys.ParentPinHash;
    private const string SaltKey = ConsoleStateKeys.ParentPinSalt;

    // Deliberately high — the PIN is checked rarely (unlocking an action), so a slow
    // hash costs the parent nothing perceptible but costs an attacker dearly per guess.
    private const int Iterations = 200_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    private readonly IConsoleStateStore _state;

    /// <summary>
    /// After a correct entry, further actions are allowed without re-entering the PIN
    /// for this long — so a parent setting several things up isn't asked four times in
    /// a row. Standard console behaviour.
    /// </summary>
    public static readonly TimeSpan GracePeriod = TimeSpan.FromMinutes(5);

    private DateTime _unlockedUntilUtc = DateTime.MinValue;

    public ParentPin(IConsoleStateStore state) => _state = state;

    /// <summary>True once a PIN has been set (setup complete or changed later).</summary>
    public bool IsSet => !string.IsNullOrEmpty(_state.Get(HashKey));

    /// <summary>True while inside the grace period after a correct entry.</summary>
    public bool IsUnlocked => DateTime.UtcNow < _unlockedUntilUtc;

    /// <summary>Sets or replaces the PIN. Generates a fresh salt each time.</summary>
    public void Set(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(pin, salt);

        _state.Set(SaltKey, Convert.ToHexString(salt));
        _state.Set(HashKey, Convert.ToHexString(hash));
    }

    /// <summary>
    /// Verifies a PIN. On success, opens the grace period. Uses a fixed-time comparison
    /// so a wrong PIN cannot be narrowed down by how long the check took.
    /// </summary>
    public bool Verify(string pin)
    {
        var storedHashHex = _state.Get(HashKey);
        var storedSaltHex = _state.Get(SaltKey);

        if (string.IsNullOrEmpty(storedHashHex) || string.IsNullOrEmpty(storedSaltHex))
        {
            return false;
        }

        byte[] storedHash, salt;
        try
        {
            storedHash = Convert.FromHexString(storedHashHex);
            salt = Convert.FromHexString(storedSaltHex);
        }
        catch (FormatException)
        {
            return false;
        }

        var candidate = Derive(pin, salt);
        var ok = CryptographicOperations.FixedTimeEquals(candidate, storedHash);

        if (ok)
        {
            _unlockedUntilUtc = DateTime.UtcNow + GracePeriod;
        }

        return ok;
    }

    /// <summary>Ends the grace period early — e.g. when the parent leaves a settings screen.</summary>
    public void Lock() => _unlockedUntilUtc = DateTime.MinValue;

    private static byte[] Derive(string pin, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
}
