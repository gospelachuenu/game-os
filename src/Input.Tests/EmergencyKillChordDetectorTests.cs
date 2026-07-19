using InputDaemon;

namespace Input.Tests;

public class EmergencyKillChordDetectorTests
{
    private static GamepadSnapshot Snapshot(XInputButtons buttons, bool connected = true) =>
        new(connected, buttons, 0, 0, 0, 0);

    [Fact]
    public void Observe_FiresWhenGuideAndStartBothHeld()
    {
        var detector = new EmergencyKillChordDetector();
        var fired = false;
        detector.ChordTriggered += () => fired = true;

        detector.Observe(Snapshot(XInputButtons.Guide | XInputButtons.Start));

        Assert.True(fired);
    }

    [Fact]
    public void Observe_DoesNotFireWhenOnlyGuideHeld()
    {
        var detector = new EmergencyKillChordDetector();
        var fired = false;
        detector.ChordTriggered += () => fired = true;

        detector.Observe(Snapshot(XInputButtons.Guide));

        Assert.False(fired);
    }

    [Fact]
    public void Observe_DoesNotFireWhenOnlyStartHeld()
    {
        var detector = new EmergencyKillChordDetector();
        var fired = false;
        detector.ChordTriggered += () => fired = true;

        detector.Observe(Snapshot(XInputButtons.Start));

        Assert.False(fired);
    }

    [Fact]
    public void Observe_FiresOnlyOncePerContinuousHold_NotEveryPoll()
    {
        var detector = new EmergencyKillChordDetector();
        var fireCount = 0;
        detector.ChordTriggered += () => fireCount++;

        var chordSnapshot = Snapshot(XInputButtons.Guide | XInputButtons.Start);
        detector.Observe(chordSnapshot);
        detector.Observe(chordSnapshot);
        detector.Observe(chordSnapshot);

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void Observe_FiresAgainAfterReleaseAndRePress()
    {
        var detector = new EmergencyKillChordDetector();
        var fireCount = 0;
        detector.ChordTriggered += () => fireCount++;

        var chordSnapshot = Snapshot(XInputButtons.Guide | XInputButtons.Start);
        var releasedSnapshot = Snapshot(0);

        detector.Observe(chordSnapshot);
        detector.Observe(releasedSnapshot);
        detector.Observe(chordSnapshot);

        Assert.Equal(2, fireCount);
    }

    [Fact]
    public void Observe_IgnoresChordWhenControllerDisconnected()
    {
        var detector = new EmergencyKillChordDetector();
        var fired = false;
        detector.ChordTriggered += () => fired = true;

        detector.Observe(Snapshot(XInputButtons.Guide | XInputButtons.Start, connected: false));

        Assert.False(fired);
    }
}
