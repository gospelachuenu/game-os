using CecController;

namespace CecController.Tests;

internal sealed class FakeCecClient : ICecClient
{
    public List<string> SentCommands { get; } = new();

    public void SendCommand(string arguments)
    {
        SentCommands.Add(arguments);
    }
}
