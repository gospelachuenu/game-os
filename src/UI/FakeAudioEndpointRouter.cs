using CecController;

namespace UI;

/// <summary>
/// Fake IAudioEndpointRouter for this windowed test build — does not touch real Windows
/// audio routing (that requires the undocumented IPolicyConfig COM interop, not
/// implemented, and shouldn't run against this laptop's real audio devices anyway).
/// Just records what SoundDeviceEnforcer decided to do so the UI can display it.
/// </summary>
public sealed class FakeAudioEndpointRouter : IAudioEndpointRouter
{
    public string? LastRoutedEndpointId { get; private set; }

    public void SetDefaultRenderEndpoint(string endpointId)
    {
        LastRoutedEndpointId = endpointId;
    }
}
