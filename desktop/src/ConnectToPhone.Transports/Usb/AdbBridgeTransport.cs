using System.Diagnostics;
using System.Net.Sockets;
using ConnectToPhone.Core.Protocol;
using ConnectToPhone.Transports.Sockets;

namespace ConnectToPhone.Transports.Usb;

/// <summary>
/// Provides zero-configuration ultra-high-speed USB connectivity via ADB port forwarding.
/// If an Android phone is plugged into PC with USB debugging, this automatically establishes
/// a direct hardware USB pipe capable of 80 - 200+ MB/s transfers.
/// </summary>
public sealed class AdbBridgeTransport
{
    public const int DefaultUsbForwardPort = 42426;
    public const int PhoneTargetPort = 42424;

    private readonly string _adbPath;

    public AdbBridgeTransport(string? adbPath = null)
    {
        _adbPath = adbPath ?? FindAdbExecutable() ?? "adb.exe";
    }

    /// <summary>
    /// Checks if any ADB device is connected and ready.
    /// </summary>
    public async Task<List<string>> GetConnectedAdbDevicesAsync(CancellationToken ct = default)
    {
        List<string> devices = [];
        try
        {
            ProcessStartInfo psi = new(_adbPath, "devices")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return devices;

            string output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            string[] lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.EndsWith("device", StringComparison.OrdinalIgnoreCase))
                {
                    string serial = line.Split('\t')[0].Trim();
                    devices.Add(serial);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to query ADB devices: {ex.Message}");
        }

        return devices;
    }

    /// <summary>
    /// Forwards host port to Android device port and connects a high-speed USB transport.
    /// </summary>
    public async Task<ITransport?> ConnectOverUsbAsync(string? deviceSerial = null, int localPort = DefaultUsbForwardPort, CancellationToken ct = default)
    {
        try
        {
            string serialArg = !string.IsNullOrEmpty(deviceSerial) ? $"-s {deviceSerial} " : "";
            ProcessStartInfo psi = new(_adbPath, $"{serialArg}forward tcp:{localPort} tcp:{PhoneTargetPort}")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            }

            // Connect to the local forwarded port over loopback -> physical USB cable
            Socket socket = new(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            await socket.ConnectAsync("127.0.0.1", localPort, ct).ConfigureAwait(false);

            var transport = new TcpSocketTransport(socket, TransportType.UsbAdb, $"usb_{deviceSerial ?? "default"}");
            return transport;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to connect over USB ADB forward: {ex.Message}");
            return null;
        }
    }

    private static string? FindAdbExecutable()
    {
        string[] candidates =
        [
            @"D:\Android\Sdk\platform-tools\adb.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Android\Sdk\platform-tools\adb.exe")
        ];

        return candidates.FirstOrDefault(File.Exists);
    }
}
