using System.Text.Json;
using EdgeBridge.Protocol;
using EdgeBridge.Samples.Avalonia.Models;

namespace EdgeBridge.Samples.Avalonia.Services;

public sealed class ProfileStore
{
    private readonly string _path;

    public ProfileStore()
    {
        _path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EdgeBridge",
            "sample-devices.json");
    }

    public string Path => _path;

    public async ValueTask<DeviceProfileStore> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new DeviceProfileStore();
        }

        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<DeviceProfileStore>(stream, ProtocolJson.Options, cancellationToken)
            .ConfigureAwait(false) ?? new DeviceProfileStore();
    }

    public async ValueTask SaveAsync(DeviceProfileStore store, CancellationToken cancellationToken = default)
    {
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, store, ProtocolJson.Options, cancellationToken)
            .ConfigureAwait(false);
    }
}
