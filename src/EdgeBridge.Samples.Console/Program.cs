using EdgeBridge.Abstractions;
using EdgeBridge.Client;
using EdgeBridge.Samples.Console;

var endpoint = args.ElementAtOrDefault(0) ?? "ws://rpi4-dev.local:8080/edgebridge/";
var sample = args.ElementAtOrDefault(1) ?? "blink";
var channel = int.TryParse(args.ElementAtOrDefault(2), out var parsedChannel)
    ? parsedChannel
    : 0;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    await using var device = await ConnectAsync(endpoint, cancellation.Token).ConfigureAwait(false);

    Console.WriteLine($"Connected to {device.DeviceId}");

    switch (sample)
    {
        case "blink":
            await BlinkAsync(device, cancellation.Token).ConfigureAwait(false);
            break;
        case "button":
            await WatchButtonAsync(device, cancellation.Token).ConfigureAwait(false);
            break;
        case "fade":
            await FadeLedAsync(device, channel, cancellation.Token).ConfigureAwait(false);
            break;
        case "toy-car":
            await DriveToyCarAsync(device, cancellation.Token).ConfigureAwait(false);
            break;
        default:
            Console.WriteLine("Samples: blink, button, fade, toy-car");
            break;
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Sample stopped.");
}

static async ValueTask<IAsyncDisposableDevice> ConnectAsync(string endpoint, CancellationToken cancellationToken)
{
    var device = await EdgeDevice.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
    return new IAsyncDisposableDevice(device);
}

static async Task BlinkAsync(IDevice device, CancellationToken cancellationToken)
{
    var led = device.DigitalOutput(17);

    while (!cancellationToken.IsCancellationRequested)
    {
        await led.SetAsync(true, cancellationToken).ConfigureAwait(false);
        Console.WriteLine("LED 17: on");
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);

        await led.SetAsync(false, cancellationToken).ConfigureAwait(false);
        Console.WriteLine("LED 17: off");
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
    }
}

static async Task WatchButtonAsync(IDevice device, CancellationToken cancellationToken)
{
    var button = device.DigitalInput(17);

    await foreach (var state in button.WatchAsync(cancellationToken).ConfigureAwait(false))
    {
        Console.WriteLine($"Button {state.Channel.Number}: {state.IsHigh} at {state.Timestamp:O}");
    }
}

static async Task FadeLedAsync(IDevice device, int channel, CancellationToken cancellationToken)
{
    var led = device.PwmOutput(channel);

    try
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            for (var step = 0; step <= 100; step += 5)
            {
                await SetLedBrightnessAsync(led, step, cancellationToken).ConfigureAwait(false);
            }

            for (var step = 95; step >= 0; step -= 5)
            {
                await SetLedBrightnessAsync(led, step, cancellationToken).ConfigureAwait(false);
            }
        }
    }
    finally
    {
        await led.SetDutyCycleAsync(0, CancellationToken.None).ConfigureAwait(false);
    }
}

static async Task SetLedBrightnessAsync(IPwmOutput led, int percent, CancellationToken cancellationToken)
{
    var dutyCycle = percent / 100.0;

    await led.SetDutyCycleAsync(dutyCycle, cancellationToken).ConfigureAwait(false);
    Console.WriteLine($"PWM {led.Channel.Number}: {percent}%");
    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
}

static async Task DriveToyCarAsync(IDevice device, CancellationToken cancellationToken)
{
    var car = new ToyCar(device);
    await car.ForwardAsync(cancellationToken).ConfigureAwait(false);
    Console.WriteLine("Toy car: forward");
    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
    await car.StopAsync(cancellationToken).ConfigureAwait(false);
    Console.WriteLine("Toy car: stopped");
}

internal sealed class IAsyncDisposableDevice : IDevice, IAsyncDisposable
{
    private readonly IDevice _inner;

    public IAsyncDisposableDevice(IDevice inner)
    {
        _inner = inner;
    }

    public string DeviceId => _inner.DeviceId;

    public ValueTask<DeviceInfo> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        return _inner.GetInfoAsync(cancellationToken);
    }

    public IDigitalOutput DigitalOutput(int channel) => _inner.DigitalOutput(channel);

    public IDigitalInput DigitalInput(int channel) => _inner.DigitalInput(channel);

    public IPwmOutput PwmOutput(int channel) => _inner.PwmOutput(channel);

    public IMotor Motor(string name) => _inner.Motor(name);

    public async ValueTask DisposeAsync()
    {
        if (_inner is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
    }
}
