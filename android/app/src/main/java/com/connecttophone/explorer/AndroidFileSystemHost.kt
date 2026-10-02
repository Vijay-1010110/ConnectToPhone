package com.connecttophone.explorer

import android.os.Environment
import com.connecttophone.protocol.*
import java.io.File

class AndroidFileSystemHost {

    fun listDirectory(targetPath: String?, includeHidden: Boolean = false): FsListDirResponse {
        return try {
            if (targetPath.isNullOrEmpty() || targetPath == "/" || targetPath == "\\") {
                getRootVolumes()
            } else {
                val dir = File(targetPath)
                if (!dir.exists() || !dir.isDirectory) {
                    return FsListDirResponse(
                        currentPath = targetPath,
                        entries = emptyList(),
                        success = false,
                        errorMessage = "Directory does not exist: $targetPath"
                    )
                }

                val entries = mutableListOf<FsEntry>()
                dir.listFiles()?.forEach { file ->
                    if (!includeHidden && file.isHidden) return@forEach

                    val isDir = file.isDirectory
                    val category = if (isDir) FileCategory.FOLDER else determineCategory(file.extension)
                    val entryType = if (isDir) FsEntryType.DIRECTORY else FsEntryType.FILE

                    entries.add(
                        FsEntry(
                            name = file.name,
                            path = file.absolutePath,
                            entryType = entryType,
                            category = category,
                            sizeBytes = if (isDir) 0L else file.length(),
                            modifiedTimestamp = file.lastModified(),
                            isHidden = file.isHidden
                        )
                    )
                }

                FsListDirResponse(
                    currentPath = dir.absolutePath,
                    entries = entries,
                    success = true
                )
            }
        } catch (e: Exception) {
            FsListDirResponse(
                currentPath = targetPath ?: "",
                entries = emptyList(),
                success = false,
                errorMessage = e.message
            )
        }
    }

    private fun getRootVolumes(): FsListDirResponse {
        val entries = mutableListOf<FsEntry>()

        // 1. Primary Internal Storage
        val internalStorage = Environment.getExternalStorageDirectory()
        if (internalStorage != null && internalStorage.exists()) {
            entries.add(
                FsEntry(
                    name = "📱 Internal Storage",
                    path = internalStorage.absolutePath,
                    entryType = FsEntryType.DRIVE,
                    category = FileCategory.FOLDER,
                    sizeBytes = internalStorage.totalSpace,
                    modifiedTimestamp = System.currentTimeMillis(),
                    isHidden = false
                )
            )

            // Common shortcuts
            addFolderShortcut(entries, "Downloads", Environment.DIRECTORY_DOWNLOADS)
            addFolderShortcut(entries, "Camera (DCIM)", Environment.DIRECTORY_DCIM)
            addFolderShortcut(entries, "Pictures", Environment.DIRECTORY_PICTURES)
            addFolderShortcut(entries, "Documents", Environment.DIRECTORY_DOCUMENTS)
            addFolderShortcut(entries, "Music", Environment.DIRECTORY_MUSIC)
        }

        return FsListDirResponse(
            currentPath = "/",
            entries = entries,
            success = true
        )
    }

    private fun addFolderShortcut(entries: MutableList<FsEntry>, label: String, dirType: String) {
        val folder = Environment.getExternalStoragePublicDirectory(dirType)
        if (folder != null && folder.exists()) {
            entries.add(
                FsEntry(
                    name = "★ $label",
                    path = folder.absolutePath,
                    entryType = FsEntryType.DIRECTORY,
                    category = FileCategory.FOLDER,
                    sizeBytes = 0L,
                    modifiedTimestamp = folder.lastModified(),
                    isHidden = false
                )
            )
        }
    }

    private fun determineCategory(ext: String): FileCategory {
        return when (ext.lowercase()) {
            "jpg", "jpeg", "png", "webp", "gif", "bmp", "svg" -> FileCategory.IMAGE
            "mp4", "mkv", "avi", "mov", "webm", "3gp" -> FileCategory.VIDEO
            "mp3", "flac", "wav", "m4a", "aac", "ogg" -> FileCategory.AUDIO
            "pdf", "doc", "docx", "txt", "xls", "xlsx", "ppt", "pptx", "csv" -> FileCategory.DOCUMENT
            "zip", "rar", "7z", "tar", "gz" -> FileCategory.ARCHIVE
            "apk", "xapk", "apks" -> FileCategory.APK
            "kt", "java", "py", "c", "cpp", "h", "cs", "html", "css", "js", "ts", "json", "xml" -> FileCategory.CODE
            else -> FileCategory.GENERIC
        }
    }
}
