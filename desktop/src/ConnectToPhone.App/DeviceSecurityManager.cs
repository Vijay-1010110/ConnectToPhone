using System.IO;
using System.Text.Json;
using ConnectToPhone.Core.Protocol;

namespace ConnectToPhone.App;

public sealed class PairedDeviceInfo
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public DeviceType DeviceType { get; set; }
    public bool IsTrusted { get; set; } = true;
    public long PairedTimestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public sealed class DeviceSecurityManager
{
    private readonly string _storagePath;
    private readonly Dictionary<string, PairedDeviceInfo> _devices = [];
    private string _currentSessionPin = "";

    public string CurrentSessionPin => _currentSessionPin;

    public DeviceSecurityManager()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ConnectToPhone");
        Directory.CreateDirectory(dir);
        _storagePath = Path.Combine(dir, "paired_devices.json");
        Load();
        GenerateNewPin();
    }

    public string GenerateNewPin()
    {
        _currentSessionPin = Random.Shared.Next(100000, 999999).ToString();
        return _currentSessionPin;
    }

    public bool IsDeviceTrusted(string deviceId)
    {
        return _devices.TryGetValue(deviceId, out var dev) && dev.IsTrusted;
    }

    public void TrustDevice(string deviceId, string deviceName, DeviceType type)
    {
        _devices[deviceId] = new PairedDeviceInfo
        {
            DeviceId = deviceId,
            DeviceName = deviceName,
            DeviceType = type,
            IsTrusted = true,
            PairedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        Save();
    }

    public void UnpairDevice(string deviceId)
    {
        if (_devices.Remove(deviceId))
        {
            Save();
        }
    }

    public IReadOnlyCollection<PairedDeviceInfo> GetAllDevices() => _devices.Values.ToList();

    private void Load()
    {
        try
        {
            if (File.Exists(_storagePath))
            {
                string json = File.ReadAllText(_storagePath);
                var list = JsonSerializer.Deserialize<List<PairedDeviceInfo>>(json);
                if (list != null)
                {
                    foreach (var item in list)
                    {
                        _devices[item.DeviceId] = item;
                    }
                }
            }
        }
        catch {}
    }

    private void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(_devices.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_storagePath, json);
        }
        catch {}
    }
}
