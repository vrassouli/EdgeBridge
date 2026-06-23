using EdgeBridge.Abstractions;

namespace EdgeBridge.Samples.Avalonia;

internal sealed class RgbLedController
{
    private readonly IDigitalOutput _red;
    private readonly IDigitalOutput _green;
    private readonly IDigitalOutput _blue;

    public RgbLedController(IDevice device, int redChannel, int greenChannel, int blueChannel)
    {
        _red = device.DigitalOutput(redChannel);
        _green = device.DigitalOutput(greenChannel);
        _blue = device.DigitalOutput(blueChannel);
    }

    public ValueTask SetRedAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        return _red.SetAsync(enabled, cancellationToken);
    }

    public ValueTask SetGreenAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        return _green.SetAsync(enabled, cancellationToken);
    }

    public ValueTask SetBlueAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        return _blue.SetAsync(enabled, cancellationToken);
    }

    public async ValueTask TurnOffAsync(CancellationToken cancellationToken = default)
    {
        await _red.SetAsync(false, cancellationToken).ConfigureAwait(false);
        await _green.SetAsync(false, cancellationToken).ConfigureAwait(false);
        await _blue.SetAsync(false, cancellationToken).ConfigureAwait(false);
    }
}
