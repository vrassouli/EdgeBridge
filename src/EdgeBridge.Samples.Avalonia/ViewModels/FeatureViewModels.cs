using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using EdgeBridge.Abstractions;
using EdgeBridge.Client;
using EdgeBridge.Protocol;
using EdgeBridge.Samples.Avalonia.Models;

namespace EdgeBridge.Samples.Avalonia.ViewModels;

public sealed class FeatureNavigationItem
{
    public required string Name { get; init; }

    public required object ViewModel { get; init; }
}

public sealed class GpioFeatureViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public GpioFeatureViewModel(MainViewModel main)
    {
        _main = main;
        AddInputCommand = new AsyncCommand(_ => AddAsync(GpioDirection.Input));
        AddOutputCommand = new AsyncCommand(_ => AddAsync(GpioDirection.Output));
    }

    public ObservableCollection<GpioChannelViewModel> Channels { get; } = [];

    public AsyncCommand AddInputCommand { get; }

    public AsyncCommand AddOutputCommand { get; }

    public void Load(DeviceProfile profile)
    {
        Channels.Clear();
        foreach (var channel in profile.GpioChannels)
        {
            Channels.Add(new GpioChannelViewModel(_main, profile, channel));
        }
    }

    public void RefreshCommandStates()
    {
        foreach (var channel in Channels)
        {
            channel.RefreshCommandStates();
        }
    }

    private async Task AddAsync(GpioDirection direction)
    {
        if (_main.SelectedProfile is null)
        {
            return;
        }

        var channel = new GpioChannelProfile
        {
            Name = direction == GpioDirection.Input ? "Input" : "Output",
            Direction = direction
        };
        _main.SelectedProfile.GpioChannels.Add(channel);
        Channels.Add(new GpioChannelViewModel(_main, _main.SelectedProfile, channel));
        await _main.SaveProfilesAsync();
    }
}

public sealed class GpioChannelViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly DeviceProfile _profile;
    private readonly GpioChannelProfile _model;
    private CancellationTokenSource? _watchCts;
    private string _status = "Idle";
    private bool _isWatching;

    public GpioChannelViewModel(MainViewModel main, DeviceProfile profile, GpioChannelProfile model)
    {
        _main = main;
        _profile = profile;
        _model = model;
        ReadCommand = new AsyncCommand(_ => ReadAsync(), () => Direction == GpioDirection.Input && _main.IsConnected);
        RemoveCommand = new AsyncCommand(_ => RemoveAsync());
        ToggleWatchCommand = new AsyncCommand(_ => ToggleWatchAsync(), () => Direction == GpioDirection.Input && _main.IsConnected);
    }

    public string Name
    {
        get => _model.Name;
        set { _model.Name = value; OnPropertyChanged(); _ = _main.SaveProfilesAsync(); }
    }

    public int Channel
    {
        get => _model.Channel;
        set { _model.Channel = value; OnPropertyChanged(); _ = _main.SaveProfilesAsync(); }
    }

    public GpioDirection Direction
    {
        get => _model.Direction;
        set
        {
            _model.Direction = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsInput));
            OnPropertyChanged(nameof(IsOutput));
            RefreshCommandStates();
            _ = _main.SaveProfilesAsync();
        }
    }

    public bool Value
    {
        get => _model.LastValue;
        set
        {
            if (_model.LastValue == value)
            {
                return;
            }

            _model.LastValue = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ValueText));
            _ = _main.SaveProfilesAsync();

            if (Direction == GpioDirection.Output)
            {
                _ = WriteOutputValueAsync(value);
            }
        }
    }

    public bool IsInput => Direction == GpioDirection.Input;

    public bool IsOutput => Direction == GpioDirection.Output;

    public string ValueText => Value ? "High" : "Low";

    public DigitalInputPullMode PullMode
    {
        get => _model.PullMode;
        set { _model.PullMode = value; OnPropertyChanged(); _ = _main.SaveProfilesAsync(); }
    }

    public IReadOnlyList<DigitalInputPullMode> PullModes { get; } =
        Enum.GetValues<DigitalInputPullMode>();

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public bool IsWatching
    {
        get => _isWatching;
        set
        {
            if (SetProperty(ref _isWatching, value))
            {
                OnPropertyChanged(nameof(WatchButtonText));
            }
        }
    }

    public string WatchButtonText => IsWatching ? "Stop" : "Watch";

    public AsyncCommand ReadCommand { get; }

    public AsyncCommand ToggleWatchCommand { get; }

    public AsyncCommand RemoveCommand { get; }

    public void RefreshCommandStates()
    {
        ReadCommand.RaiseCanExecuteChanged();
        ToggleWatchCommand.RaiseCanExecuteChanged();
    }

    private async Task ReadAsync()
    {
        if (_main.Device is null)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var state = await _main.Device
                .DigitalInput(Channel, new DigitalInputOptions(PullMode))
                .ReadAsync(timeout.Token)
                .ConfigureAwait(true);
            Value = state.IsHigh;
            Status = $"Read {ValueText} at {state.Timestamp:T}";
        }
        catch (OperationCanceledException)
        {
            Status = "Read timed out";
        }
        catch (Exception ex)
        {
            Status = $"Read failed: {ex.Message}";
        }
    }

    private async Task WriteOutputValueAsync(bool value)
    {
        if (_main.Device is null)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _main.Device.DigitalOutput(Channel).SetAsync(value, timeout.Token).ConfigureAwait(true);
            Status = $"Wrote {(value ? "High" : "Low")}";
        }
        catch (Exception ex)
        {
            Status = $"Write failed: {ex.Message}";
        }
    }

    private Task ToggleWatchAsync()
    {
        if (IsWatching)
        {
            _watchCts?.Cancel();
            _watchCts = null;
            IsWatching = false;
            Status = "Watch stopped";
            return Task.CompletedTask;
        }

        if (_main.Device is null)
        {
            return Task.CompletedTask;
        }

        _watchCts = new CancellationTokenSource();
        IsWatching = true;
        Status = "Watching...";
        _ = WatchAsync(_watchCts.Token);
        return Task.CompletedTask;
    }

    private async Task WatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var state in _main.Device!
                .DigitalInput(Channel, new DigitalInputOptions(PullMode))
                .WatchAsync(cancellationToken)
                .ConfigureAwait(true))
            {
                Value = state.IsHigh;
                Status = $"Changed at {state.Timestamp:T}";
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsWatching = false;
        }
    }

    private async Task RemoveAsync()
    {
        _watchCts?.Cancel();
        _profile.GpioChannels.Remove(_model);
        _main.Gpio.Channels.Remove(this);
        await _main.SaveProfilesAsync();
    }
}

public sealed class PwmFeatureViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public PwmFeatureViewModel(MainViewModel main)
    {
        _main = main;
        AddCommand = new AsyncCommand(_ => AddAsync());
    }

    public ObservableCollection<PwmChannelViewModel> Channels { get; } = [];

    public AsyncCommand AddCommand { get; }

    public void Load(DeviceProfile profile)
    {
        Channels.Clear();
        foreach (var channel in profile.PwmChannels)
        {
            Channels.Add(new PwmChannelViewModel(_main, profile, channel));
        }
    }

    public void RefreshCommandStates()
    {
        foreach (var channel in Channels)
        {
            channel.RefreshCommandStates();
        }
    }

    private async Task AddAsync()
    {
        if (_main.SelectedProfile is null)
        {
            return;
        }

        var channel = new PwmChannelProfile { Name = "PWM", Channel = 0 };
        _main.SelectedProfile.PwmChannels.Add(channel);
        Channels.Add(new PwmChannelViewModel(_main, _main.SelectedProfile, channel));
        await _main.SaveProfilesAsync();
    }
}

public sealed class PwmChannelViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly DeviceProfile _profile;
    private readonly PwmChannelProfile _model;
    private CancellationTokenSource? _applyCts;
    private string _status = "Idle";

    public PwmChannelViewModel(MainViewModel main, DeviceProfile profile, PwmChannelProfile model)
    {
        _main = main;
        _profile = profile;
        _model = model;
        RemoveCommand = new AsyncCommand(_ => RemoveAsync());
    }

    public string Name { get => _model.Name; set { _model.Name = value; OnPropertyChanged(); _ = _main.SaveProfilesAsync(); } }

    public int Channel { get => _model.Channel; set { _model.Channel = value; OnPropertyChanged(); _ = _main.SaveProfilesAsync(); } }

    public double DutyCycle
    {
        get => _model.DutyCycle;
        set
        {
            var dutyCycle = Math.Clamp(value, 0, 1);
            if (double.IsNaN(dutyCycle) || Math.Abs(_model.DutyCycle - dutyCycle) < 0.0001)
            {
                return;
            }

            _model.DutyCycle = dutyCycle;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DutyPercent));
            _ = _main.SaveProfilesAsync();
            ApplyLatestDutyCycle();
        }
    }

    public int DutyPercent => (int)Math.Round(DutyCycle * 100);

    public string Status { get => _status; set => SetProperty(ref _status, value); }

    public AsyncCommand RemoveCommand { get; }

    public void RefreshCommandStates()
    {
    }

    private void ApplyLatestDutyCycle()
    {
        var device = _main.Device;
        if (device is null)
        {
            Status = "Connect to apply PWM";
            return;
        }

        _applyCts?.Cancel();
        var applyCts = new CancellationTokenSource();
        _applyCts = applyCts;
        _ = ApplyAsync(device, Channel, DutyCycle, DutyPercent, applyCts);
    }

    private async Task ApplyAsync(IDevice device, int channel, double dutyCycle, int dutyPercent, CancellationTokenSource applyCts)
    {
        try
        {
            if (applyCts.IsCancellationRequested)
            {
                return;
            }

            if (ReferenceEquals(_applyCts, applyCts))
            {
                Status = "Applying...";
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(applyCts.Token, timeout.Token);
            await device.PwmOutput(channel).SetDutyCycleAsync(dutyCycle, linked.Token).ConfigureAwait(true);
            if (ReferenceEquals(_applyCts, applyCts))
            {
                Status = $"Applied {dutyPercent}%";
            }
        }
        catch (OperationCanceledException) when (applyCts.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_applyCts, applyCts))
            {
                Status = "PWM command timed out";
            }
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_applyCts, applyCts))
            {
                Status = $"PWM failed: {ex.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_applyCts, applyCts))
            {
                _applyCts = null;
            }

            applyCts.Dispose();
        }
    }

    private async Task RemoveAsync()
    {
        _applyCts?.Cancel();
        _profile.PwmChannels.Remove(_model);
        _main.Pwm.Channels.Remove(this);
        await _main.SaveProfilesAsync();
    }
}

public sealed class MotorFeatureViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public MotorFeatureViewModel(MainViewModel main)
    {
        _main = main;
        AddCommand = new AsyncCommand(_ => AddAsync());
    }

    public ObservableCollection<MotorViewModel> Motors { get; } = [];

    public AsyncCommand AddCommand { get; }

    public void Load(DeviceProfile profile)
    {
        Motors.Clear();
        foreach (var motor in profile.Motors)
        {
            Motors.Add(new MotorViewModel(_main, profile, motor));
        }
    }

    public void RefreshCommandStates()
    {
        foreach (var motor in Motors)
        {
            motor.RefreshCommandStates();
        }
    }

    private async Task AddAsync()
    {
        if (_main.SelectedProfile is null)
        {
            return;
        }

        var motor = new MotorProfile { Name = "motor" };
        _main.SelectedProfile.Motors.Add(motor);
        Motors.Add(new MotorViewModel(_main, _main.SelectedProfile, motor));
        await _main.SaveProfilesAsync();
    }
}

public sealed class MotorViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly DeviceProfile _profile;
    private readonly MotorProfile _model;
    private string _status = "Idle";

    public MotorViewModel(MainViewModel main, DeviceProfile profile, MotorProfile model)
    {
        _main = main;
        _profile = profile;
        _model = model;
        ApplyCommand = new AsyncCommand(_ => ApplyAsync(), () => _main.IsConnected);
        StopCommand = new AsyncCommand(_ => StopAsync(), () => _main.IsConnected);
        RemoveCommand = new AsyncCommand(_ => RemoveAsync());
    }

    public string Name { get => _model.Name; set { _model.Name = value; OnPropertyChanged(); _ = _main.SaveProfilesAsync(); } }

    public double Speed { get => _model.Speed; set { _model.Speed = Math.Clamp(value, -1, 1); OnPropertyChanged(); OnPropertyChanged(nameof(SpeedPercent)); _ = _main.SaveProfilesAsync(); } }

    public int SpeedPercent => (int)Math.Round(Speed * 100);

    public string Status { get => _status; set => SetProperty(ref _status, value); }

    public AsyncCommand ApplyCommand { get; }

    public AsyncCommand StopCommand { get; }

    public AsyncCommand RemoveCommand { get; }

    public void RefreshCommandStates()
    {
        ApplyCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
    }

    private async Task ApplyAsync()
    {
        if (_main.Device is null)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _main.Device.Motor(Name).SetSpeedAsync(Speed, timeout.Token).ConfigureAwait(true);
            Status = $"Speed {SpeedPercent}%";
        }
        catch (OperationCanceledException)
        {
            Status = "Motor command timed out";
        }
        catch (Exception ex)
        {
            Status = $"Motor failed: {ex.Message}";
        }
    }

    private async Task StopAsync()
    {
        if (_main.Device is null)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _main.Device.Motor(Name).StopAsync(timeout.Token).ConfigureAwait(true);
            Speed = 0;
            Status = "Stopped";
        }
        catch (OperationCanceledException)
        {
            Status = "Motor command timed out";
        }
        catch (Exception ex)
        {
            Status = $"Motor failed: {ex.Message}";
        }
    }

    private async Task RemoveAsync()
    {
        _profile.Motors.Remove(_model);
        _main.Motor.Motors.Remove(this);
        await _main.SaveProfilesAsync();
    }
}

public sealed class I2cFeatureViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public I2cFeatureViewModel(MainViewModel main)
    {
        _main = main;
        AddCommand = new AsyncCommand(_ => AddAsync());
    }

    public ObservableCollection<I2cDeviceViewModel> Devices { get; } = [];

    public AsyncCommand AddCommand { get; }

    public void Load(DeviceProfile profile)
    {
        Devices.Clear();
        foreach (var device in profile.I2cDevices)
        {
            Devices.Add(new I2cDeviceViewModel(_main, profile, device));
        }
    }

    public void RefreshCommandStates()
    {
        foreach (var device in Devices)
        {
            device.RefreshCommandStates();
        }
    }

    private async Task AddAsync()
    {
        if (_main.SelectedProfile is null)
        {
            return;
        }

        var device = new I2cDeviceProfile { Name = "I2C Device", Bus = 1, ReadLength = 1 };
        _main.SelectedProfile.I2cDevices.Add(device);
        Devices.Add(new I2cDeviceViewModel(_main, _main.SelectedProfile, device));
        await _main.SaveProfilesAsync();
    }
}

public sealed class I2cDeviceViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly DeviceProfile _profile;
    private readonly I2cDeviceProfile _model;
    private string _status = "Idle";
    private string _busText = "";
    private string _addressText = "";
    private string _registerText = "";
    private string _readLengthText = "";

    public I2cDeviceViewModel(MainViewModel main, DeviceProfile profile, I2cDeviceProfile model)
    {
        _main = main;
        _profile = profile;
        _model = model;
        UpdateValueTextFields();
        ReadCommand = new AsyncCommand(_ => ReadAsync(), () => _main.IsConnected);
        WriteCommand = new AsyncCommand(_ => WriteAsync(), () => _main.IsConnected);
        RemoveCommand = new AsyncCommand(_ => RemoveAsync());
    }

    public I2cValueFormat[] ValueFormats { get; } = [I2cValueFormat.Hex, I2cValueFormat.Decimal];

    public string Name { get => _model.Name; set { _model.Name = value; OnPropertyChanged(); _ = _main.SaveProfilesAsync(); } }

    public I2cValueFormat ValueFormat
    {
        get => _model.ValueFormat;
        set
        {
            var oldFormat = _model.ValueFormat;
            if (oldFormat == value)
            {
                return;
            }

            _model.ValueFormat = value;
            OnPropertyChanged();
            UpdateValueTextFields();
            if (TryParseBytes(LastRead, oldFormat, out var lastRead))
            {
                LastRead = FormatBytes(lastRead, value);
            }

            if (TryParseBytes(WriteBytes, oldFormat, out var writeBytes))
            {
                _model.WriteBytes = FormatBytes(writeBytes, value);
                OnPropertyChanged(nameof(WriteBytes));
            }

            _ = _main.SaveProfilesAsync();
        }
    }

    public int Bus { get => _model.Bus; set { _model.Bus = value; OnPropertyChanged(); _ = _main.SaveProfilesAsync(); } }

    public string BusText
    {
        get => _busText;
        set
        {
            SetProperty(ref _busText, value);
            TrySetNumber(value, 0, 16, number => _model.Bus = number, nameof(Bus));
        }
    }

    public int Address { get => _model.Address; set { _model.Address = value; OnPropertyChanged(); _ = _main.SaveProfilesAsync(); } }

    public string AddressText
    {
        get => _addressText;
        set
        {
            SetProperty(ref _addressText, value);
            TrySetNumber(value, 0, 127, number => _model.Address = number, nameof(Address));
        }
    }

    public int Register { get => _model.Register; set { _model.Register = value; OnPropertyChanged(); _ = _main.SaveProfilesAsync(); } }

    public string RegisterText
    {
        get => _registerText;
        set
        {
            SetProperty(ref _registerText, value);
            TrySetNumber(value, 0, 255, number => _model.Register = number, nameof(Register));
        }
    }

    public int ReadLength { get => _model.ReadLength; set { _model.ReadLength = Math.Max(1, value); OnPropertyChanged(); _ = _main.SaveProfilesAsync(); } }

    public string ReadLengthText
    {
        get => _readLengthText;
        set
        {
            SetProperty(ref _readLengthText, value);
            TrySetNumber(value, 1, 64, number => _model.ReadLength = number, nameof(ReadLength));
        }
    }

    public string WriteBytes { get => _model.WriteBytes; set { _model.WriteBytes = value; OnPropertyChanged(); _ = _main.SaveProfilesAsync(); } }

    public string LastRead { get => _model.LastRead; set { _model.LastRead = value; OnPropertyChanged(); _ = _main.SaveProfilesAsync(); } }

    public string Status { get => _status; set => SetProperty(ref _status, value); }

    public AsyncCommand ReadCommand { get; }

    public AsyncCommand WriteCommand { get; }

    public AsyncCommand RemoveCommand { get; }

    public void RefreshCommandStates()
    {
        ReadCommand.RaiseCanExecuteChanged();
        WriteCommand.RaiseCanExecuteChanged();
    }

    private async Task ReadAsync()
    {
        if (_main.Device is null)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var data = await _main.Device.I2cDevice(Bus, Address).ReadRegisterAsync(Register, ReadLength, timeout.Token).ConfigureAwait(true);
            LastRead = FormatBytes(data);
            Status = $"Read {data.Length} byte(s)";
        }
        catch (OperationCanceledException)
        {
            Status = "I2C read timed out";
        }
        catch (Exception ex)
        {
            Status = $"I2C read failed: {ex.Message}";
        }
    }

    private async Task WriteAsync()
    {
        if (_main.Device is null)
        {
            return;
        }

        if (!TryParseBytes(WriteBytes, ValueFormat, out var data))
        {
            Status = ValueFormat == I2cValueFormat.Hex
                ? "Enter bytes as hex values from 0x00 to 0xFF"
                : "Enter bytes as decimal values from 0 to 255";
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _main.Device.I2cDevice(Bus, Address).WriteRegisterAsync(Register, data, timeout.Token).ConfigureAwait(true);
            Status = $"Wrote {data.Length} byte(s)";
        }
        catch (OperationCanceledException)
        {
            Status = "I2C write timed out";
        }
        catch (Exception ex)
        {
            Status = $"I2C write failed: {ex.Message}";
        }
    }

    private async Task RemoveAsync()
    {
        _profile.I2cDevices.Remove(_model);
        _main.I2c.Devices.Remove(this);
        await _main.SaveProfilesAsync();
    }

    private void UpdateValueTextFields()
    {
        _busText = FormatNumber(_model.Bus);
        _addressText = FormatNumber(_model.Address);
        _registerText = FormatNumber(_model.Register);
        _readLengthText = FormatNumber(_model.ReadLength);
        OnPropertyChanged(nameof(BusText));
        OnPropertyChanged(nameof(AddressText));
        OnPropertyChanged(nameof(RegisterText));
        OnPropertyChanged(nameof(ReadLengthText));
    }

    private void TrySetNumber(string text, int minimum, int maximum, Action<int> apply, string propertyName)
    {
        if (!TryParseNumber(text, out var value) || value < minimum || value > maximum)
        {
            Status = $"Enter {propertyName} from {FormatNumber(minimum)} to {FormatNumber(maximum)}";
            return;
        }

        apply(value);
        OnPropertyChanged(propertyName);
        Status = "Idle";
        _ = _main.SaveProfilesAsync();
    }

    private bool TryParseNumber(string text, out int value)
    {
        var input = text.Trim();
        if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(input[2..], System.Globalization.NumberStyles.HexNumber, null, out value);
        }

        var numberStyle = ValueFormat == I2cValueFormat.Hex
            ? System.Globalization.NumberStyles.HexNumber
            : System.Globalization.NumberStyles.Integer;
        return int.TryParse(input, numberStyle, null, out value);
    }

    private string FormatNumber(int value)
    {
        return ValueFormat == I2cValueFormat.Hex ? $"0x{value:X2}" : value.ToString();
    }

    private static bool TryParseBytes(string text, I2cValueFormat valueFormat, out byte[] bytes)
    {
        var parts = text.Split([' ', ',', ';', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parsedBytes = new List<byte>();
        foreach (var part in parts)
        {
            var input = part.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? part[2..] : part;
            var fromBase = part.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || valueFormat == I2cValueFormat.Hex ? 16 : 10;
            try
            {
                parsedBytes.Add(Convert.ToByte(input, fromBase));
            }
            catch (FormatException)
            {
                bytes = [];
                return false;
            }
            catch (OverflowException)
            {
                bytes = [];
                return false;
            }
        }

        bytes = parsedBytes.ToArray();
        return true;
    }

    private string FormatBytes(IEnumerable<byte> bytes)
    {
        return FormatBytes(bytes, ValueFormat);
    }

    private static string FormatBytes(IEnumerable<byte> bytes, I2cValueFormat valueFormat)
    {
        return string.Join(" ", bytes.Select(value => valueFormat == I2cValueFormat.Hex ? $"0x{value:X2}" : value.ToString()));
    }
}

public sealed class CameraFeatureViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public CameraFeatureViewModel(MainViewModel main)
    {
        _main = main;
        AddCommand = new AsyncCommand(_ => AddAsync());
    }

    public ObservableCollection<CameraViewModel> Cameras { get; } = [];

    public AsyncCommand AddCommand { get; }

    public void Load(DeviceProfile profile)
    {
        Cameras.Clear();
        foreach (var camera in profile.Cameras)
        {
            Cameras.Add(new CameraViewModel(_main, profile, camera));
        }
    }

    public void RefreshCommandStates()
    {
        foreach (var camera in Cameras)
        {
            camera.RefreshCommandStates();
        }
    }

    private async Task AddAsync()
    {
        if (_main.SelectedProfile is null)
        {
            return;
        }

        var camera = new CameraProfile { CameraId = "camera0", Name = "Camera" };
        _main.SelectedProfile.Cameras.Add(camera);
        Cameras.Add(new CameraViewModel(_main, _main.SelectedProfile, camera));
        await _main.SaveProfilesAsync();
    }
}

public sealed class CameraViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly DeviceProfile _profile;
    private readonly CameraProfile _model;
    private string _status = "Idle";

    public CameraViewModel(MainViewModel main, DeviceProfile profile, CameraProfile model)
    {
        _main = main;
        _profile = profile;
        _model = model;
        StartCommand = new AsyncCommand(_ => StartAsync(), () => _main.IsConnected);
        StopCommand = new AsyncCommand(_ => StopAsync(), () => _main.IsConnected);
        RefreshCommand = new AsyncCommand(_ => RefreshAsync(), () => _main.IsConnected);
        RemoveCommand = new AsyncCommand(_ => RemoveAsync());
    }

    public string CameraId { get => _model.CameraId; set { _model.CameraId = value; OnPropertyChanged(); _ = _main.SaveProfilesAsync(); } }

    public string Name { get => _model.Name; set { _model.Name = value; OnPropertyChanged(); _ = _main.SaveProfilesAsync(); } }

    public bool IsStreaming { get => _model.IsStreaming; set { _model.IsStreaming = value; OnPropertyChanged(); OnPropertyChanged(nameof(StateText)); _ = _main.SaveProfilesAsync(); } }

    public string StateText => IsStreaming ? "Streaming" : "Stopped";

    public string Status { get => _status; set => SetProperty(ref _status, value); }

    public AsyncCommand StartCommand { get; }

    public AsyncCommand StopCommand { get; }

    public AsyncCommand RefreshCommand { get; }

    public AsyncCommand RemoveCommand { get; }

    public void RefreshCommandStates()
    {
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        RefreshCommand.RaiseCanExecuteChanged();
    }

    private async Task StartAsync()
    {
        if (_main.Device is null)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _main.Device.Camera(CameraId).StartStreamAsync(timeout.Token).ConfigureAwait(true);
            IsStreaming = true;
            Status = "Started";
        }
        catch (OperationCanceledException)
        {
            Status = "Camera command timed out";
        }
        catch (Exception ex)
        {
            Status = $"Camera failed: {ex.Message}";
        }
    }

    private async Task StopAsync()
    {
        if (_main.Device is null)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _main.Device.Camera(CameraId).StopStreamAsync(timeout.Token).ConfigureAwait(true);
            IsStreaming = false;
            Status = "Stopped";
        }
        catch (OperationCanceledException)
        {
            Status = "Camera command timed out";
        }
        catch (Exception ex)
        {
            Status = $"Camera failed: {ex.Message}";
        }
    }

    private async Task RefreshAsync()
    {
        if (_main.Device is null)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var state = await _main.Device.Camera(CameraId).GetStatusAsync(timeout.Token).ConfigureAwait(true);
            IsStreaming = state.IsStreaming;
            Status = $"Status at {state.Timestamp:T}";
        }
        catch (OperationCanceledException)
        {
            Status = "Camera status timed out";
        }
        catch (Exception ex)
        {
            Status = $"Camera failed: {ex.Message}";
        }
    }

    private async Task RemoveAsync()
    {
        _profile.Cameras.Remove(_model);
        _main.Camera.Cameras.Remove(this);
        await _main.SaveProfilesAsync();
    }
}

public sealed class AgentConfigViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private string _configJson = "";
    private string _status = "Connect to load Agent configuration.";
    private bool _restartRequired;

    public AgentConfigViewModel(MainViewModel main)
    {
        _main = main;
        RefreshCommand = new AsyncCommand(_ => RefreshAsync(), () => _main.IsConnected);
        ApplyCommand = new AsyncCommand(_ => ApplyAsync(), () => _main.IsConnected);
    }

    public string ConfigJson
    {
        get => _configJson;
        set => SetProperty(ref _configJson, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public bool RestartRequired
    {
        get => _restartRequired;
        set => SetProperty(ref _restartRequired, value);
    }

    public AsyncCommand RefreshCommand { get; }

    public AsyncCommand ApplyCommand { get; }

    public void RefreshCommandStates()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        ApplyCommand.RaiseCanExecuteChanged();
    }

    public async Task RefreshAsync()
    {
        if (_main.Device is not IAgentConfigurationClient configClient)
        {
            Status = "Connected device does not expose Agent configuration commands.";
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var config = await configClient.GetAgentConfigAsync(timeout.Token).ConfigureAwait(true);
            ConfigJson = JsonSerializer.Serialize(config, ProtocolJson.Options);
            RestartRequired = false;
            Status = "Agent configuration loaded.";
        }
        catch (OperationCanceledException)
        {
            Status = "Agent configuration request timed out.";
        }
        catch (Exception ex)
        {
            Status = $"Agent configuration failed: {ex.Message}";
        }
    }

    private async Task ApplyAsync()
    {
        if (_main.Device is not IAgentConfigurationClient configClient)
        {
            Status = "Connected device does not expose Agent configuration commands.";
            return;
        }

        try
        {
            var config = JsonSerializer.Deserialize<AgentConfigDto>(ConfigJson, ProtocolJson.Options)
                ?? throw new InvalidOperationException("Agent configuration JSON is empty.");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await configClient.UpdateAgentConfigAsync(config, timeout.Token).ConfigureAwait(true);
            ConfigJson = JsonSerializer.Serialize(result.Config, ProtocolJson.Options);
            RestartRequired = result.RestartRequired;
            Status = result.Message;
        }
        catch (JsonException ex)
        {
            Status = $"Invalid configuration JSON: {ex.Message}";
        }
        catch (OperationCanceledException)
        {
            Status = "Agent configuration update timed out.";
        }
        catch (Exception ex)
        {
            Status = $"Agent configuration update failed: {ex.Message}";
        }
    }
}
