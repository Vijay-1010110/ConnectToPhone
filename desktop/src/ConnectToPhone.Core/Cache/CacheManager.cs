using System.IO;
using System.Text.Json;
using ConnectToPhone.Core.Protocol;
using ConnectToPhone.Core.Protocol.Models;

namespace ConnectToPhone.Core.Cache;

public record CachedFileItem(
    string FileName,
    string FilePath,
    long SizeBytes,
    DateTime LastAccessedUtc,
    FileCategory Category
)
{
    public string SizeDisplay => SizeBytes switch
    {
        >= 1024 * 1024 * 1024 => $"{(SizeBytes / 1024.0 / 1024.0 / 1024.0):F2} GB",
        >= 1024 * 1024 => $"{(SizeBytes / 1024.0 / 1024.0):F1} MB",
        >= 1024 => $"{(SizeBytes / 1024.0):F1} KB",
        _ => $"{SizeBytes} B"
    };
}

public record StorageSpaceInfo(
    string DriveName,
    long TotalBytes,
    long FreeBytes,
    long CacheUsedBytes,
    long CacheQuotaBytes
)
{
    public string TotalDisplay => $"{(TotalBytes / 1024.0 / 1024.0 / 1024.0):F1} GB";
    public string FreeDisplay => $"{(FreeBytes / 1024.0 / 1024.0 / 1024.0):F1} GB";
    public string CacheUsedDisplay => $"{(CacheUsedBytes / 1024.0 / 1024.0):F1} MB";
    public string QuotaDisplay => $"{(CacheQuotaBytes / 1024.0 / 1024.0):F0} MB";
}

public sealed class CacheManager
{
    public const long DefaultQuotaBytes = 500 * 1024 * 1024; // 500 MB
    public const int MaxFileAgeHours = 24;

    private readonly string _settingsFilePath;
    private string _cacheDirectory;
    private string _downloadsDirectory;
    private long _cacheQuotaBytes;

    public string CacheDirectory => _cacheDirectory;
    public string DownloadsDirectory => _downloadsDirectory;
    public long CacheQuotaBytes => _cacheQuotaBytes;

    public event Action? CacheUpdated;

    public CacheManager()
    {
        string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConnectToPhone");
        Directory.CreateDirectory(appData);
        _settingsFilePath = Path.Combine(appData, "storage_settings.json");

        // Prioritize non-C drive (e.g. D:\) to protect C: drive free space
        string nonCDriveRoot = FindPreferredNonCDriveRoot();

        _cacheDirectory = Path.Combine(nonCDriveRoot, "ConnectToPhone", "Cache");
        _downloadsDirectory = Path.Combine(nonCDriveRoot, "ConnectToPhone", "Downloads");
        _cacheQuotaBytes = DefaultQuotaBytes;

        LoadSettings();
        EnsureDirectories();
    }

    private static string FindPreferredNonCDriveRoot()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.IsReady && !drive.Name.StartsWith("C:", StringComparison.OrdinalIgnoreCase))
            {
                return drive.RootDirectory.FullName;
            }
        }

        // Fallback if only C exists
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ConnectToPhone_Storage");
    }

    private void EnsureDirectories()
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            Directory.CreateDirectory(_downloadsDirectory);
        }
        catch { }
    }

    public StorageSpaceInfo GetStorageSpaceInfo()
    {
        long cacheUsed = 0;
        try
        {
            if (Directory.Exists(_cacheDirectory))
            {
                DirectoryInfo d = new(_cacheDirectory);
                foreach (var f in d.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    cacheUsed += f.Length;
                }
            }

            string root = Path.GetPathRoot(_cacheDirectory) ?? "D:\\";
            DriveInfo di = new(root);
            if (di.IsReady)
            {
                return new StorageSpaceInfo(
                    di.Name,
                    di.TotalSize,
                    di.AvailableFreeSpace,
                    cacheUsed,
                    _cacheQuotaBytes
                );
            }
        }
        catch { }

        return new StorageSpaceInfo("D:\\", 0, 0, cacheUsed, _cacheQuotaBytes);
    }

    public List<CachedFileItem> GetCachedFiles(string filter = "All")
    {
        List<CachedFileItem> list = [];
        if (!Directory.Exists(_cacheDirectory)) return list;

        try
        {
            DirectoryInfo dir = new(_cacheDirectory);
            foreach (var fi in dir.EnumerateFiles())
            {
                var cat = DetermineCategory(fi.Extension);
                if (filter != "All")
                {
                    bool match = filter switch
                    {
                        "Images" => cat == FileCategory.Image,
                        "Documents" => cat == FileCategory.Document,
                        "Media" => cat == FileCategory.Video || cat == FileCategory.Audio,
                        "Other" => cat != FileCategory.Image && cat != FileCategory.Document && cat != FileCategory.Video && cat != FileCategory.Audio,
                        _ => true
                    };
                    if (!match) continue;
                }

                list.Add(new CachedFileItem(
                    fi.Name,
                    fi.FullName,
                    fi.Length,
                    fi.LastAccessTimeUtc,
                    cat
                ));
            }
        }
        catch { }

        return list.OrderByDescending(x => x.LastAccessedUtc).ToList();
    }

    public string GetCacheDestinationPath(string fileName)
    {
        EnsureDirectories();
        return Path.Combine(_cacheDirectory, fileName);
    }

    public void EnforceQuotaAndPrune()
    {
        if (!Directory.Exists(_cacheDirectory)) return;

        try
        {
            DirectoryInfo dir = new(_cacheDirectory);
            var files = dir.EnumerateFiles().OrderBy(f => f.LastAccessTimeUtc).ToList();

            long totalBytes = files.Sum(f => f.Length);
            long targetBytes = (long)(_cacheQuotaBytes * 0.80); // Prune to 80%

            if (totalBytes > _cacheQuotaBytes)
            {
                foreach (var file in files)
                {
                    if (totalBytes <= targetBytes) break;
                    try
                    {
                        long size = file.Length;
                        file.Delete();
                        totalBytes -= size;
                    }
                    catch { }
                }
            }
        }
        catch { }

        CacheUpdated?.Invoke();
    }

    public void RunPeriodicCleanup()
    {
        if (!Directory.Exists(_cacheDirectory)) return;

        try
        {
            DateTime cutoff = DateTime.UtcNow.AddHours(-MaxFileAgeHours);
            DirectoryInfo dir = new(_cacheDirectory);
            foreach (var file in dir.EnumerateFiles())
            {
                if (file.LastAccessTimeUtc < cutoff)
                {
                    try { file.Delete(); } catch { }
                }
            }
        }
        catch { }

        EnforceQuotaAndPrune();
    }

    public bool DeleteCachedFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                CacheUpdated?.Invoke();
                return true;
            }
        }
        catch { }
        return false;
    }

    public void ClearAllCache()
    {
        try
        {
            if (Directory.Exists(_cacheDirectory))
            {
                DirectoryInfo dir = new(_cacheDirectory);
                foreach (var f in dir.EnumerateFiles())
                {
                    try { f.Delete(); } catch { }
                }
                foreach (var d in dir.EnumerateDirectories())
                {
                    try { d.Delete(true); } catch { }
                }
            }
        }
        catch { }

        CacheUpdated?.Invoke();
    }

    public void OpenQuickView(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var p = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo(filePath)
                    {
                        UseShellExecute = true
                    }
                };
                p.Start();
            }
        }
        catch { }
    }

    public void SetCacheDirectory(string newPath)
    {
        if (string.IsNullOrWhiteSpace(newPath)) return;
        _cacheDirectory = newPath;
        EnsureDirectories();
        SaveSettings();
        CacheUpdated?.Invoke();
    }

    public void SetDownloadsDirectory(string newPath)
    {
        if (string.IsNullOrWhiteSpace(newPath)) return;
        _downloadsDirectory = newPath;
        EnsureDirectories();
        SaveSettings();
    }

    public void SetQuotaMb(long quotaMb)
    {
        _cacheQuotaBytes = quotaMb * 1024 * 1024;
        SaveSettings();
        EnforceQuotaAndPrune();
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("CacheDirectory", out var cd))
                    _cacheDirectory = cd.GetString() ?? _cacheDirectory;
                if (doc.RootElement.TryGetProperty("DownloadsDirectory", out var dd))
                    _downloadsDirectory = dd.GetString() ?? _downloadsDirectory;
                if (doc.RootElement.TryGetProperty("CacheQuotaBytes", out var cq))
                    _cacheQuotaBytes = cq.GetInt64();
            }
        }
        catch { }
    }

    private void SaveSettings()
    {
        try
        {
            var data = new
            {
                CacheDirectory = _cacheDirectory,
                DownloadsDirectory = _downloadsDirectory,
                CacheQuotaBytes = _cacheQuotaBytes
            };
            File.WriteAllText(_settingsFilePath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private static FileCategory DetermineCategory(string ext)
    {
        return ext.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp" or ".svg" => FileCategory.Image,
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" => FileCategory.Video,
            ".mp3" or ".flac" or ".wav" or ".m4a" or ".aac" or ".ogg" => FileCategory.Audio,
            ".pdf" or ".doc" or ".docx" or ".txt" or ".xls" or ".xlsx" or ".ppt" or ".pptx" => FileCategory.Document,
            _ => FileCategory.Generic
        };
    }
}
