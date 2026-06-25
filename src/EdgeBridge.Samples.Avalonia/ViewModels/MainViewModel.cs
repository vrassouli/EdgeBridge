using System.Collections.ObjectModel;
using EdgeBridge.Abstractions;
using EdgeBridge.Client;
using EdgeBridge.Samples.Avalonia.Models;
using EdgeBridge.Samples.Avalonia.Services;

namespace EdgeBridge.Samples.Avalonia.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ProfileStore _profileStore = new();
    private readonly DeviceProfileStore _store = new();
    private DeviceProfile? _selectedProfile;
    private FeatureNavigationItem? _selectedFeature;
    private IDevice? _device;
    private IAsyncDisposable? _deviceLease;
    private DeviceInfo? _deviceInfo;
    private string _status = "Ready";
    private bool _isBusy;
    private bool _isConnected;

    public MainViewModel()
    {
        Gpio = new GpioFeatureViewModel(this);
        Pwm = new PwmFeatureViewModel(this);
        Motor = new MotorFeatureViewModel(this);
        I2c = new I2cFeatureViewModel(this);
        Camera = new CameraFeatureViewModel(this);
        AgentConfig = new AgentConfigViewModel(this);

        Features =
        [
            new FeatureNavigationItem { Name = "Overview", ViewModel = this },
            new FeatureNavigationItem { Name = "GPIO", ViewModel = Gpio },
            new FeatureNavigationItem { Name = "PWM", ViewModel = Pwm },
            new FeatureNavigationItem { Name = "Motors", ViewModel = Motor },
            new FeatureNavigationItem { Name = "I2C", ViewModel = I2c },
            new FeatureNavigationItem { Name = "Camera", ViewModel = Camera },
            new FeatureNavigationItem { Name = "Agent Config", ViewModel = AgentConfig }
        ];

        SelectedFeature = Features[0];
        ConnectCommand = new AsyncCommand(_ => ConnectAsync(), () => SelectedProfile is not null && !IsConnected);
        DisconnectCommand = new AsyncCommand(_ => DisconnectAsync(), () => IsConnected);
        AddDeviceCommand = new AsyncCommand(_ => AddDeviceAsync());
        RemoveDeviceCommand = new AsyncCommand(_ => RemoveDeviceAsync(), () => SelectedProfile is not null);
        SaveCommand = new AsyncCommand(_ => SaveProfilesAsync());
        _ = LoadAsync();
    }

    public ObservableCollection<DeviceProfile> Devices => _store.Devices;

    public IReadOnlyList<FeatureNavigationItem> Features { get; }

    public GpioFeatureViewModel Gpio { get; }

    public PwmFeatureViewModel Pwm { get; }

    public MotorFeatureViewModel Motor { get; }

    public I2cFeatureViewModel I2c { get; }

    public CameraFeatureViewModel Camera { get; }

    public AgentConfigViewModel AgentConfig { get; }

    public IDevice? Device => _device;

    public string ProfileStorePath => _profileStore.Path;

    public DeviceProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                LoadFeatureProfiles();
                OnPropertyChanged(nameof(SelectedDeviceName));
                OnPropertyChanged(nameof(SelectedEndpoint));
                ConnectCommand.RaiseCanExecuteChanged();
                RemoveDeviceCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public FeatureNavigationItem? SelectedFeature
    {
        get => _selectedFeature;
        set => SetProperty(ref _selectedFeature, value);
    }

    public string SelectedDeviceName
    {
        get => SelectedProfile?.Name ?? "";
        set
        {
            if (SelectedProfile is null)
            {
                return;
            }

            SelectedProfile.Name = value;
            OnPropertyChanged();
            _ = SaveProfilesAsync();
        }
    }

    public string SelectedEndpoint
    {
        get => SelectedProfile?.Endpoint ?? "";
        set
        {
            if (SelectedProfile is null)
            {
                return;
            }

            SelectedProfile.Endpoint = value;
            OnPropertyChanged();
            _ = SaveProfilesAsync();
        }
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(ConnectionState));
                ConnectCommand.RaiseCanExecuteChanged();
                DisconnectCommand.RaiseCanExecuteChanged();
                RefreshFeatureCommandStates();
            }
        }
    }

    public string ConnectionState => IsConnected ? "Connected" : "Disconnected";

    public DeviceInfo? DeviceInfo
    {
        get => _deviceInfo;
        set
        {
            if (SetProperty(ref _deviceInfo, value))
            {
                OnPropertyChanged(nameof(DeviceInfoText));
                OnPropertyChanged(nameof(CapabilitiesText));
            }
        }
    }

    public string DeviceInfoText => DeviceInfo is null
        ? "No device connected"
        : $"{DeviceInfo.DeviceName} ({DeviceInfo.DeviceId})";

    public string CapabilitiesText => DeviceInfo is null
        ? "Capabilities unavailable"
        : string.Join(", ", DeviceInfo.Capabilities);

    public AsyncCommand ConnectCommand { get; }

    public AsyncCommand DisconnectCommand { get; }

    public AsyncCommand AddDeviceCommand { get; }

    public AsyncCommand RemoveDeviceCommand { get; }

    public AsyncCommand SaveCommand { get; }

    public async Task SaveProfilesAsync()
    {
        await _profileStore.SaveAsync(_store).ConfigureAwait(true);
        Status = $"Profiles saved to {ProfileStorePath}";
    }

    private async Task LoadAsync()
    {
        try
        {
            var loaded = await _profileStore.LoadAsync().ConfigureAwait(true);
            Devices.Clear();
            foreach (var device in loaded.Devices)
            {
                Devices.Add(device);
            }

            if (Devices.Count == 0)
            {
                Devices.Add(new DeviceProfile());
                await SaveProfilesAsync();
            }

            SelectedProfile = Devices[0];
            Status = "Profiles loaded.";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    private async Task AddDeviceAsync()
    {
        var profile = new DeviceProfile { Name = $"Device {Devices.Count + 1}" };
        Devices.Add(profile);
        SelectedProfile = profile;
        await SaveProfilesAsync();
    }

    private async Task RemoveDeviceAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var index = Devices.IndexOf(SelectedProfile);
        Devices.Remove(SelectedProfile);
        SelectedProfile = Devices.Count == 0 ? null : Devices[Math.Clamp(index - 1, 0, Devices.Count - 1)];
        await SaveProfilesAsync();
    }

    private async Task ConnectAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        IsBusy = true;
        Status = "Connecting...";
        try
        {
            await DisconnectAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            _device = await EdgeDevice.ConnectAsync(SelectedProfile.Endpoint, timeout.Token).ConfigureAwait(true);
            _deviceLease = _device as IAsyncDisposable;
            DeviceInfo = await _device.GetInfoAsync(timeout.Token).ConfigureAwait(true);
            IsConnected = true;
            Status = $"Connected to {DeviceInfo.DeviceName}.";
            await AgentConfig.RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await DisconnectAsync().ConfigureAwait(true);
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DisconnectAsync()
    {
        if (_deviceLease is not null)
        {
            await _deviceLease.DisposeAsync().ConfigureAwait(true);
        }

        _device = null;
        _deviceLease = null;
        DeviceInfo = null;
        IsConnected = false;
        Status = "Disconnected.";
    }

    private void LoadFeatureProfiles()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        Gpio.Load(SelectedProfile);
        Pwm.Load(SelectedProfile);
        Motor.Load(SelectedProfile);
        I2c.Load(SelectedProfile);
        Camera.Load(SelectedProfile);
    }

    private void RefreshFeatureCommandStates()
    {
        Gpio.RefreshCommandStates();
        Pwm.RefreshCommandStates();
        Motor.RefreshCommandStates();
        I2c.RefreshCommandStates();
        Camera.RefreshCommandStates();
        AgentConfig.RefreshCommandStates();
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }
}
