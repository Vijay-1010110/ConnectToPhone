using System.IO;
using System.Text;
using ConnectToPhone.Core.Protocol;
using ConnectToPhone.Core.Protocol.Models;

namespace ConnectToPhone.Transports.Simulation;

public sealed class SimulatedTransport : ITransport
{
    public string ChannelId { get; }
    public TransportType Type { get; set; } = TransportType.WifiLan;
    public bool IsConnected { get; private set; } = true;
    public double CurrentSpeedBytesPerSec { get; set; } = 95.0 * 1024 * 1024;
    public string SimulatedDeviceName { get; set; }
    public string SimulatedDeviceId { get; set; }

    public event Func<ITransport, BinaryFrame, Task>? FrameReceived;
    public event Action<ITransport>? Disconnected;

    private readonly CancellationTokenSource _cts = new();

    public SimulatedTransport(string deviceName = "Simulated Galaxy Tab S9", string deviceId = "sim_device_001", TransportType type = TransportType.WifiLan)
    {
        SimulatedDeviceName = deviceName;
        SimulatedDeviceId = deviceId;
        ChannelId = $"SIM_{deviceId}";
        Type = type;
    }

    public void StartReceiving()
    {
        // Simulated transport is ready immediately
    }

    public async Task SendFrameAsync(BinaryFrame frame, CancellationToken ct = default)
    {
        if (!IsConnected) throw new IOException("Simulated transport is disconnected.");

        // Asynchronously process incoming frames and simulate phone responses
        _ = Task.Run(async () =>
        {
            await Task.Delay(15, ct); // simulate minimal network latency
            await HandleFrameAsync(frame);
        }, ct);
    }

    private async Task HandleFrameAsync(BinaryFrame frame)
    {
        switch (frame.Type)
        {
            case FrameType.HandshakeSyn:
                var syn = HandshakeSyn.FromUtf8Bytes(frame.Payload);
                var ack = new HandshakeAck
                {
                    Accepted = true,
                    SessionId = frame.SessionId,
                    DeviceId = SimulatedDeviceId,
                    DeviceName = SimulatedDeviceName
                };
                await DispatchFrameAsync(new BinaryFrame(FrameType.HandshakeAck, frame.SessionId, ack.ToUtf8Bytes()));
                break;

            case FrameType.FsListDirReq:
                var req = FsListDirRequest.FromUtf8Bytes(frame.Payload);
                string path = req?.TargetPath ?? "/";
                var listResp = GenerateSimulatedDir(path);
                await DispatchFrameAsync(new BinaryFrame(FrameType.FsListDirResp, frame.SessionId, listResp.ToUtf8Bytes()));
                break;

            case FrameType.FsPullFileReq:
                var pullReq = FsPullFileRequest.FromUtf8Bytes(frame.Payload);
                if (pullReq != null)
                {
                    await SimulateFilePullAsync(pullReq.RemoteFilePath, pullReq.IsPreview, frame.SessionId);
                }
                break;

            case FrameType.TransferManifest:
                var manifest = TransferManifest.FromUtf8Bytes(frame.Payload);
                // Ready to accept chunks
                break;

            case FrameType.TransferChunk:
                if (TransferChunk.TryParse(frame.Payload, out var incomingChunk) && incomingChunk != null)
                {
                    var chunkAck = new TransferChunkAck
                    {
                        TransferId = incomingChunk.TransferId,
                        ChunkIndex = incomingChunk.ChunkIndex,
                        Success = true
                    };
                    await DispatchFrameAsync(new BinaryFrame(FrameType.TransferChunkAck, frame.SessionId, chunkAck.Serialize()));
                }
                break;

            case FrameType.ClipboardSync:
                // Echo / acknowledge clipboard if needed
                break;
        }
    }

    private async Task SimulateFilePullAsync(string remotePath, bool isPreview, ulong sessionId)
    {
        string fileName = Path.GetFileName(remotePath);
        if (string.IsNullOrEmpty(fileName)) fileName = "simulated_sample.mp4";

        long totalSize = 12 * 1024 * 1024; // 12 MB simulated sample file
        int chunkSize = 2 * 1024 * 1024;
        int totalChunks = (int)Math.Ceiling((double)totalSize / chunkSize);

        var manifest = new TransferManifest
        {
            TransferId = (ulong)Random.Shared.Next(100000, 999999),
            FileName = fileName,
            TotalBytes = totalSize,
            ChunkSize = chunkSize,
            TotalChunks = totalChunks,
            IsPreview = isPreview
        };

        // 1. Send manifest
        await DispatchFrameAsync(new BinaryFrame(FrameType.TransferManifest, sessionId, manifest.ToUtf8Bytes()));

        // 2. Stream simulated chunks
        byte[] dummyData = new byte[chunkSize];
        for (int i = 0; i < dummyData.Length; i++) dummyData[i] = (byte)(i % 256);

        for (int i = 0; i < totalChunks && IsConnected; i++)
        {
            await Task.Delay(25); // ~80 MB/s speed simulation
            var chunk = new TransferChunk
            {
                TransferId = manifest.TransferId,
                ChunkIndex = (uint)i,
                ChunkHash = 12345678UL,
                Payload = dummyData
            };
            await DispatchFrameAsync(new BinaryFrame(FrameType.TransferChunk, sessionId, chunk.Serialize()));
        }
    }

    private FsListDirResponse GenerateSimulatedDir(string path)
    {
        path = path.TrimEnd('/', '\\');
        if (string.IsNullOrEmpty(path)) path = "/";

        var resp = new FsListDirResponse
        {
            CurrentPath = path,
            Success = true
        };

        if (path == "/")
        {
            resp.Entries.Add(new FsEntry
            {
                Name = "Internal Storage (/storage/emulated/0)",
                Path = "/storage/emulated/0",
                EntryType = FsEntryType.Directory,
                Category = FileCategory.Folder
            });
            resp.Entries.Add(new FsEntry
            {
                Name = "SD Card (/storage/0000-0000)",
                Path = "/storage/0000-0000",
                EntryType = FsEntryType.Directory,
                Category = FileCategory.Folder
            });
        }
        else if (path.Equals("/storage/emulated/0", StringComparison.OrdinalIgnoreCase))
        {
            resp.Entries.Add(new FsEntry { Name = "DCIM", Path = "/storage/emulated/0/DCIM", EntryType = FsEntryType.Directory, Category = FileCategory.Folder });
            resp.Entries.Add(new FsEntry { Name = "Download", Path = "/storage/emulated/0/Download", EntryType = FsEntryType.Directory, Category = FileCategory.Folder });
            resp.Entries.Add(new FsEntry { Name = "Movies", Path = "/storage/emulated/0/Movies", EntryType = FsEntryType.Directory, Category = FileCategory.Folder });
            resp.Entries.Add(new FsEntry { Name = "Documents", Path = "/storage/emulated/0/Documents", EntryType = FsEntryType.Directory, Category = FileCategory.Folder });
            resp.Entries.Add(new FsEntry { Name = "Music", Path = "/storage/emulated/0/Music", EntryType = FsEntryType.Directory, Category = FileCategory.Folder });
        }
        else if (path.Contains("Movies", StringComparison.OrdinalIgnoreCase))
        {
            resp.Entries.Add(new FsEntry { Name = "Dune_Part_Two_Trailer_4K.mp4", Path = $"{path}/Dune_Part_Two_Trailer_4K.mp4", EntryType = FsEntryType.File, Category = FileCategory.Video, SizeBytes = 45 * 1024 * 1024 });
            resp.Entries.Add(new FsEntry { Name = "Cyberpunk_Edgerunners_Clip.mkv", Path = $"{path}/Cyberpunk_Edgerunners_Clip.mkv", EntryType = FsEntryType.File, Category = FileCategory.Video, SizeBytes = 128 * 1024 * 1024 });
            resp.Entries.Add(new FsEntry { Name = "Interstellar_Docking_Scene.mp4", Path = $"{path}/Interstellar_Docking_Scene.mp4", EntryType = FsEntryType.File, Category = FileCategory.Video, SizeBytes = 89 * 1024 * 1024 });
        }
        else
        {
            resp.Entries.Add(new FsEntry { Name = "ConnectToWindow_QuickGuide.pdf", Path = $"{path}/ConnectToWindow_QuickGuide.pdf", EntryType = FsEntryType.File, Category = FileCategory.Document, SizeBytes = 2 * 1024 * 1024 });
            resp.Entries.Add(new FsEntry { Name = "Screenshot_20261003_HDR.png", Path = $"{path}/Screenshot_20261003_HDR.png", EntryType = FsEntryType.File, Category = FileCategory.Image, SizeBytes = 4 * 1024 * 1024 });
            resp.Entries.Add(new FsEntry { Name = "Project_Archive_Backup.zip", Path = $"{path}/Project_Archive_Backup.zip", EntryType = FsEntryType.File, Category = FileCategory.Archive, SizeBytes = 64 * 1024 * 1024 });
        }

        return resp;
    }

    private async Task DispatchFrameAsync(BinaryFrame frame)
    {
        if (FrameReceived != null)
        {
            await FrameReceived.Invoke(this, frame);
        }
    }

    public void TriggerDisconnect()
    {
        if (IsConnected)
        {
            IsConnected = false;
            Disconnected?.Invoke(this);
        }
    }

    public void TriggerReconnect()
    {
        IsConnected = true;
    }

    public ValueTask DisposeAsync()
    {
        TriggerDisconnect();
        _cts.Cancel();
        _cts.Dispose();
        return ValueTask.CompletedTask;
    }
}
