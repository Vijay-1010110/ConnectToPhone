using ConnectToPhone.Core.Protocol;
using ConnectToPhone.Core.Protocol.Models;

namespace ConnectToPhone.Explorer;

public sealed class WindowsFileSystemHost
{
    public FsListDirResponse ListDirectory(string? targetPath, bool includeHidden = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(targetPath) || targetPath == "/" || targetPath == "\\")
            {
                return GetRootDrivesAndSpecialFolders();
            }

            if (!Directory.Exists(targetPath))
            {
                return new FsListDirResponse
                {
                    CurrentPath = targetPath,
                    Success = false,
                    ErrorMessage = $"Directory does not exist: {targetPath}"
                };
            }

            DirectoryInfo dirInfo = new(targetPath);
            List<FsEntry> entries = [];

            // 1. Subdirectories (with per-item exception handling for protected folders)
            try
            {
                foreach (var dir in dirInfo.EnumerateDirectories())
                {
                    try
                    {
                        if (!includeHidden && dir.Attributes.HasFlag(FileAttributes.Hidden))
                            continue;

                        entries.Add(new FsEntry
                        {
                            Name = dir.Name,
                            Path = dir.FullName,
                            EntryType = FsEntryType.Directory,
                            Category = FileCategory.Folder,
                            SizeBytes = 0,
                            ModifiedTimestamp = new DateTimeOffset(dir.LastWriteTimeUtc).ToUnixTimeMilliseconds(),
                            IsHidden = dir.Attributes.HasFlag(FileAttributes.Hidden)
                        });
                    }
                    catch { }
                }
            }
            catch { }

            // 2. Files
            try
            {
                foreach (var file in dirInfo.EnumerateFiles())
                {
                    try
                    {
                        if (!includeHidden && file.Attributes.HasFlag(FileAttributes.Hidden))
                            continue;

                        entries.Add(new FsEntry
                        {
                            Name = file.Name,
                            Path = file.FullName,
                            EntryType = FsEntryType.File,
                            Category = DetermineCategory(file.Extension),
                            SizeBytes = file.Length,
                            ModifiedTimestamp = new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeMilliseconds(),
                            IsHidden = file.Attributes.HasFlag(FileAttributes.Hidden)
                        });
                    }
                    catch { }
                }
            }
            catch { }

            return new FsListDirResponse
            {
                CurrentPath = dirInfo.FullName,
                Entries = entries,
                Success = true
            };
        }
        catch (Exception ex)
        {
            return new FsListDirResponse
            {
                CurrentPath = targetPath ?? string.Empty,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private static FsListDirResponse GetRootDrivesAndSpecialFolders()
    {
        List<FsEntry> entries = [];

        // Special folders
        AddSpecialFolder(entries, "Desktop", Environment.SpecialFolder.Desktop);
        AddSpecialFolder(entries, "Downloads", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        AddSpecialFolder(entries, "Documents", Environment.SpecialFolder.MyDocuments);
        AddSpecialFolder(entries, "Pictures", Environment.SpecialFolder.MyPictures);
        AddSpecialFolder(entries, "Videos", Environment.SpecialFolder.MyVideos);

        // Quick media access if D:\movies exists
        if (Directory.Exists(@"D:\movies"))
        {
            AddSpecialFolder(entries, "Movies (D:)", @"D:\movies");
        }

        // Logical drives (C:\, D:\, etc.)
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.IsReady)
            {
                entries.Add(new FsEntry
                {
                    Name = string.IsNullOrEmpty(drive.VolumeLabel) ? drive.Name : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})",
                    Path = drive.RootDirectory.FullName,
                    EntryType = FsEntryType.Drive,
                    Category = FileCategory.Folder,
                    SizeBytes = drive.TotalSize,
                    ModifiedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            }
        }

        return new FsListDirResponse
        {
            CurrentPath = "/",
            Entries = entries,
            Success = true
        };
    }

    private static void AddSpecialFolder(List<FsEntry> entries, string name, Environment.SpecialFolder folder)
    {
        string path = Environment.GetFolderPath(folder);
        AddSpecialFolder(entries, name, path);
    }

    private static void AddSpecialFolder(List<FsEntry> entries, string name, string path)
    {
        if (Directory.Exists(path))
        {
            DirectoryInfo dir = new(path);
            entries.Add(new FsEntry
            {
                Name = $"★ {name}",
                Path = dir.FullName,
                EntryType = FsEntryType.Directory,
                Category = FileCategory.Folder,
                ModifiedTimestamp = new DateTimeOffset(dir.LastWriteTimeUtc).ToUnixTimeMilliseconds()
            });
        }
    }

    private static FileCategory DetermineCategory(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp" or ".svg" => FileCategory.Image,
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".wmv" => FileCategory.Video,
            ".mp3" or ".flac" or ".wav" or ".m4a" or ".aac" or ".ogg" => FileCategory.Audio,
            ".pdf" or ".doc" or ".docx" or ".txt" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".csv" => FileCategory.Document,
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => FileCategory.Archive,
            ".apk" or ".aab" => FileCategory.Apk,
            ".cs" or ".kt" or ".java" or ".py" or ".js" or ".ts" or ".html" or ".css" or ".json" or ".xml" => FileCategory.Code,
            _ => FileCategory.Generic
        };
    }
}
