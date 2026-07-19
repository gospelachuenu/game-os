using CecController;

namespace CecController.Tests;

public class CecPowerSyncControllerTests
{
    [Fact]
    public void OnPowerStateTransition_ResumeSendsPowerOnThenActiveSource()
    {
        var client = new FakeCecClient();
        var controller = new CecPowerSyncController(client);

        controller.OnPowerStateTransition(PowerStateTransition.ResumeFromSleep);

        Assert.Equal(new[] { CecCommands.PowerOn, CecCommands.ActiveSource }, client.SentCommands);
    }

    [Fact]
    public void OnPowerStateTransition_SuspendSendsStandbyOnly()
    {
        var client = new FakeCecClient();
        var controller = new CecPowerSyncController(client);

        controller.OnPowerStateTransition(PowerStateTransition.EnteringSuspend);

        Assert.Equal(new[] { CecCommands.PowerStandby }, client.SentCommands);
    }

    [Fact]
    public void OnPowerStateTransition_SuspendNeverSendsActiveSource()
    {
        var client = new FakeCecClient();
        var controller = new CecPowerSyncController(client);

        controller.OnPowerStateTransition(PowerStateTransition.EnteringSuspend);

        Assert.DoesNotContain(CecCommands.ActiveSource, client.SentCommands);
    }

    [Fact]
    public void OnPowerStateTransition_ResumeNeverSendsStandby()
    {
        var client = new FakeCecClient();
        var controller = new CecPowerSyncController(client);

        controller.OnPowerStateTransition(PowerStateTransition.ResumeFromSleep);

        Assert.DoesNotContain(CecCommands.PowerStandby, client.SentCommands);
    }

    [Fact]
    public void OnPowerStateTransition_MultipleResumesEachFireTheirOwnCommandPair()
    {
        var client = new FakeCecClient();
        var controller = new CecPowerSyncController(client);

        controller.OnPowerStateTransition(PowerStateTransition.ResumeFromSleep);
        controller.OnPowerStateTransition(PowerStateTransition.EnteringSuspend);
        controller.OnPowerStateTransition(PowerStateTransition.ResumeFromSleep);

        Assert.Equal(
            new[] { CecCommands.PowerOn, CecCommands.ActiveSource, CecCommands.PowerStandby, CecCommands.PowerOn, CecCommands.ActiveSource },
            client.SentCommands);
    }
}
