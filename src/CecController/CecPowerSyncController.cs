namespace CecController;

/// <summary>
/// Implements plan.md §8.2/§8.3: on resume from S3 sleep, broadcast ActiveSource
/// (forces the TV to switch input) followed by power-on; on entering suspend,
/// broadcast standby. Built against ICecClient so the command sequencing is fully
/// testable without a real Pulse-Eight adapter or TV.
/// </summary>
public sealed class CecPowerSyncController
{
    private readonly ICecClient _cecClient;

    public CecPowerSyncController(ICecClient cecClient)
    {
        _cecClient = cecClient;
    }

    public void OnPowerStateTransition(PowerStateTransition transition)
    {
        switch (transition)
        {
            case PowerStateTransition.ResumeFromSleep:
                _cecClient.SendCommand(CecCommands.PowerOn);
                _cecClient.SendCommand(CecCommands.ActiveSource);
                break;

            case PowerStateTransition.EnteringSuspend:
                _cecClient.SendCommand(CecCommands.PowerStandby);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(transition), transition, null);
        }
    }
}
