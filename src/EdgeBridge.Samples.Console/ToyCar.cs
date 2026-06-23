using EdgeBridge.Abstractions;

namespace EdgeBridge.Samples.Console;

internal sealed class ToyCar
{
    private readonly IMotor _leftMotor;
    private readonly IMotor _rightMotor;

    public ToyCar(IDevice device)
    {
        _leftMotor = device.Motor("left");
        _rightMotor = device.Motor("right");
    }

    public async ValueTask ForwardAsync(CancellationToken cancellationToken = default)
    {
        await _leftMotor.SetSpeedAsync(0.75, cancellationToken).ConfigureAwait(false);
        await _rightMotor.SetSpeedAsync(0.75, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        await _leftMotor.StopAsync(cancellationToken).ConfigureAwait(false);
        await _rightMotor.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}

