using CecController;

namespace CecController.Tests;

public class SoundDeviceEnforcerTests
{
    private sealed class FakeAudioEndpointRouter : IAudioEndpointRouter
    {
        public List<string> RoutedEndpointIds { get; } = new();

        public void SetDefaultRenderEndpoint(string endpointId)
        {
            RoutedEndpointIds.Add(endpointId);
        }
    }

    [Fact]
    public void OnTvAudioEndpointConnected_RoutesToConfiguredTvEndpoint()
    {
        var router = new FakeAudioEndpointRouter();
        var enforcer = new SoundDeviceEnforcer(router, "tv-hdmi-endpoint-guid");

        enforcer.OnTvAudioEndpointConnected();

        Assert.Equal(new[] { "tv-hdmi-endpoint-guid" }, router.RoutedEndpointIds);
    }

    [Fact]
    public void OnTvAudioEndpointConnected_ReRoutesEveryTimeItFires()
    {
        var router = new FakeAudioEndpointRouter();
        var enforcer = new SoundDeviceEnforcer(router, "tv-hdmi-endpoint-guid");

        enforcer.OnTvAudioEndpointConnected();
        enforcer.OnTvAudioEndpointConnected();

        Assert.Equal(2, router.RoutedEndpointIds.Count);
        Assert.All(router.RoutedEndpointIds, id => Assert.Equal("tv-hdmi-endpoint-guid", id));
    }
}
