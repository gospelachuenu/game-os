namespace CecController;

/// <summary>
/// Seam over cec-client.exe process invocation (plan.md §8). The real implementation
/// shells out to the Pulse-Eight cec-client binary and requires the actual USB-to-HDMI
/// CEC adapter connected to a TV to meaningfully exercise, so it is not implemented or
/// executed in this dev environment. This interface lets the command-selection logic
/// (which command to send on which event) be built and tested against a fake now.
/// </summary>
public interface ICecClient
{
    void SendCommand(string arguments);
}
