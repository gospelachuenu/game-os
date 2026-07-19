namespace CecController;

/// <summary>
/// Seam over the CoreAudio engine's audio-endpoint routing (plan.md §9.5). The real
/// implementation calls the undocumented IPolicyConfig COM interface to set the
/// default audio render endpoint; that requires a live audio session to exercise, so
/// only the "which endpoint should be active" decision is modeled here.
/// </summary>
public interface IAudioEndpointRouter
{
    void SetDefaultRenderEndpoint(string endpointId);
}

/// <summary>
/// Implements plan.md §9.5: the moment CEC reports the TV's HDMI audio endpoint is
/// available, force Windows to route sound there instead of falling back to onboard
/// motherboard aux out.
/// </summary>
public sealed class SoundDeviceEnforcer
{
    private readonly IAudioEndpointRouter _router;
    private readonly string _tvHdmiEndpointId;

    public SoundDeviceEnforcer(IAudioEndpointRouter router, string tvHdmiEndpointId)
    {
        _router = router;
        _tvHdmiEndpointId = tvHdmiEndpointId;
    }

    public void OnTvAudioEndpointConnected()
    {
        _router.SetDefaultRenderEndpoint(_tvHdmiEndpointId);
    }
}
