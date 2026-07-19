using InputDaemon;

Console.WriteLine("Input daemon starting...");

var reader = new XInputGamepadReader();
var killChordDetector = new EmergencyKillChordDetector();

killChordDetector.ChordTriggered += () =>
{
    Console.WriteLine("[Input] Guide+Start chord detected. Killing active game process.");
    // TODO: IPC call to ConsoleSupervisor to Process.Kill() the tracked game handle (§5.2).
};

const int pollIntervalMs = 16; // ~60Hz polling for responsive stick/button reads
const int batteryPollIntervalMs = 30_000; // §5.4: battery checked once per 30s

var lastBatteryPoll = DateTime.MinValue;

var exitSignal = new ManualResetEventSlim(false);
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    exitSignal.Set();
};

Console.WriteLine("Input daemon running. Press Ctrl+C to exit.");

while (!exitSignal.IsSet)
{
    var snapshot = reader.GetState(userIndex: 0);
    killChordDetector.Observe(snapshot);

    if (DateTime.UtcNow - lastBatteryPoll > TimeSpan.FromMilliseconds(batteryPollIntervalMs))
    {
        var battery = reader.GetBatteryInformation(userIndex: 0);
        var state = BatteryTelemetryMonitor.Classify(battery);
        Console.WriteLine($"[Input] Battery state: {state}");
        lastBatteryPoll = DateTime.UtcNow;
    }

    exitSignal.Wait(pollIntervalMs);
}

Console.WriteLine("Input daemon shutting down.");
