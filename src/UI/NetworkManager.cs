namespace UI;

/// <summary>A wireless network as shown in the setup wizard's picker.</summary>
public sealed class WirelessNetwork
{
    public required string Ssid { get; init; }

    /// <summary>0-4, matching the four bars drawn in the UI.</summary>
    public required int SignalBars { get; init; }

    public required bool IsSecured { get; init; }

    /// <summary>True when Windows already holds a saved profile, so no password is needed.</summary>
    public bool IsKnown { get; init; }
}

/// <summary>
/// Wi-Fi scanning and connection for the setup wizard.
///
/// The real implementation will sit on the Windows WLAN API (wlanapi.dll, the same
/// one the Settings app calls) either via P/Invoke or ManagedNativeWifi — which means
/// the console connects to wireless networks WITHOUT ever showing a Windows UI. That
/// is the whole point: no Settings window, no explorer.exe, nothing that breaks the
/// appliance illusion.
///
/// It is behind this seam for two reasons. It keeps the wizard testable with no
/// wireless hardware involved, and — per the project's standing constraint — wlanapi
/// calls are system-level code that must not run on the development laptop. The real
/// implementation gets written and verified on the target hardware.
/// </summary>
public interface INetworkManager
{
    /// <summary>Currently connected SSID, or null when offline.</summary>
    string? ConnectedSsid { get; }

    /// <summary>Scans for networks in range, strongest first.</summary>
    Task<IReadOnlyList<WirelessNetwork>> ScanAsync(CancellationToken ct = default);

    /// <summary>
    /// Joins a network, saving a profile so later boots reconnect on their own.
    /// Returns false when the password is rejected or the network is out of range.
    /// </summary>
    Task<bool> ConnectAsync(WirelessNetwork network, string? password, CancellationToken ct = default);
}

/// <summary>
/// Stand-in used by the windowed test build: a fixed list of networks and a short
/// artificial delay so the wizard's scanning and connecting states are actually
/// visible rather than completing instantly.
///
/// The password check is deliberately crude (any non-empty string succeeds, except
/// one SSID that always fails). It exists to exercise the failure path in the UI,
/// not to model authentication.
/// </summary>
public sealed class SimulatedNetworkManager : INetworkManager
{
    /// <summary>Connecting to this SSID always fails, so the wizard's error state can be seen.</summary>
    private const string AlwaysFailsSsid = "VM8827194";

    private static readonly WirelessNetwork[] Networks =
    {
        new() { Ssid = "BT-HH7X2K", SignalBars = 4, IsSecured = true, IsKnown = true },
        new() { Ssid = "BT-HH7X2K-5G", SignalBars = 4, IsSecured = true },
        new() { Ssid = "VM8827194", SignalBars = 3, IsSecured = true },
        new() { Ssid = "SKY-A4C2", SignalBars = 2, IsSecured = true },
        new() { Ssid = "BT-WiFi-X", SignalBars = 1, IsSecured = false },
    };

    public string? ConnectedSsid { get; private set; }

    public async Task<IReadOnlyList<WirelessNetwork>> ScanAsync(CancellationToken ct = default)
    {
        await Task.Delay(900, ct);
        return Networks;
    }

    public async Task<bool> ConnectAsync(WirelessNetwork network, string? password, CancellationToken ct = default)
    {
        await Task.Delay(1400, ct);

        if (network.Ssid == AlwaysFailsSsid)
        {
            return false;
        }

        if (network.IsSecured && !network.IsKnown && string.IsNullOrEmpty(password))
        {
            return false;
        }

        ConnectedSsid = network.Ssid;
        return true;
    }
}
