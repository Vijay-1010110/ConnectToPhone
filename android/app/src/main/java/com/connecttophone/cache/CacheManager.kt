package com.connecttophone.cache

import android.content.Context
import android.content.Intent
import android.media.RingtoneManager
import android.net.Uri
import android.os.Environment
import android.os.StatFs
import android.widget.Toast
import androidx.core.content.FileProvider
import java.io.File

data class CachedItem(
    val name: String,
    val path: String,
    val sizeBytes: Long,
    val lastAccessTime: Long,
    val category: String
) {
    val sizeDisplay: String
        get() = when {
            sizeBytes >= 1024 * 1024 * 1024 -> "%.2f GB".format(sizeBytes / 1024.0 / 1024.0 / 1024.0)
            sizeBytes >= 1024 * 1024 -> "%.1f MB".format(sizeBytes / 1024.0 / 1024.0)
            sizeBytes >= 1024 -> "%.1f KB".format(sizeBytes / 1024.0)
            else -> "$sizeBytes B"
        }
}

data class PhoneStorageInfo(
    val totalInternalBytes: Long,
    val freeInternalBytes: Long,
    val cacheUsedBytes: Long,
    val cacheQuotaBytes: Long
) {
    val totalDisplay: String get() = "%.1f GB".format(totalInternalBytes / 1024.0 / 1024.0 / 1024.0)
    val freeDisplay: String get() = "%.1f GB".format(freeInternalBytes / 1024.0 / 1024.0 / 1024.0)
    val cacheUsedDisplay: String get() = "%.1f MB".format(cacheUsedBytes / 1024.0 / 1024.0)
    val quotaDisplay: String get() = "%.0f MB".format(cacheQuotaBytes / 1024.0 / 1024.0)
}

object CacheManager {
    const val DEFAULT_QUOTA_BYTES = 200L * 1024 * 1024 // 200 MB
    const val MAX_AGE_HOURS = 24

    fun getCacheDir(context: Context): File {
        val dir = File(context.cacheDir, "quick_preview")
        if (!dir.exists()) dir.mkdirs()
        return dir
    }

    fun getDownloadsDir(context: Context): File {
        val prefs = context.getSharedPreferences("c2p_storage", Context.MODE_PRIVATE)
        val custom = prefs.getString("downloads_path", null)
        if (!custom.isNullOrEmpty()) {
            val customFile = File(custom)
            if (customFile.exists() && customFile.isDirectory) return customFile
        }
        return context.getExternalFilesDir(Environment.DIRECTORY_DOWNLOADS) ?: context.filesDir
    }

    fun setDownloadsDir(context: Context, path: String) {
        context.getSharedPreferences("c2p_storage", Context.MODE_PRIVATE)
            .edit()
            .putString("downloads_path", path)
            .apply()
    }

    fun getStorageInfo(context: Context): PhoneStorageInfo {
        val cacheDir = getCacheDir(context)
        var cacheUsed = 0L
        cacheDir.listFiles()?.forEach { if (it.isFile) cacheUsed += it.length() }

        val stat = StatFs(context.filesDir.absolutePath)
        val total = stat.totalBytes
        val available = stat.availableBytes

        return PhoneStorageInfo(
            totalInternalBytes = total,
            freeInternalBytes = available,
            cacheUsedBytes = cacheUsed,
            cacheQuotaBytes = DEFAULT_QUOTA_BYTES
        )
    }

    fun getCachedFiles(context: Context, filter: String = "All"): List<CachedItem> {
        val dir = getCacheDir(context)
        val files = dir.listFiles()?.filter { it.isFile } ?: return emptyList()

        return files.mapNotNull { file ->
            val cat = determineCategory(file.extension)
            if (filter != "All") {
                val match = when (filter) {
                    "Images" -> cat == "Image"
                    "Documents" -> cat == "Document"
                    "Media" -> cat == "Video" || cat == "Audio"
                    "Other" -> cat != "Image" && cat != "Document" && cat != "Video" && cat != "Audio"
                    else -> true
                }
                if (!match) return@mapNotNull null
            }
            CachedItem(
                name = file.name,
                path = file.absolutePath,
                sizeBytes = file.length(),
                lastAccessTime = file.lastModified(),
                category = cat
            )
        }.sortedByDescending { it.lastAccessTime }
    }

    fun enforceQuotaAndPrune(context: Context) {
        val dir = getCacheDir(context)
        val files = dir.listFiles()?.filter { it.isFile }?.sortedBy { it.lastModified() } ?: return

        var totalBytes = files.sumOf { it.length() }
        val targetBytes = (DEFAULT_QUOTA_BYTES * 0.80).toLong() // 160 MB

        if (totalBytes > DEFAULT_QUOTA_BYTES) {
            for (file in files) {
                if (totalBytes <= targetBytes) break
                val size = file.length()
                if (file.delete()) {
                    totalBytes -= size
                }
            }
        }
    }

    fun runPeriodicCleanup(context: Context) {
        val dir = getCacheDir(context)
        val cutoff = System.currentTimeMillis() - (MAX_AGE_HOURS * 3600 * 1000L)
        dir.listFiles()?.filter { it.isFile }?.forEach { file ->
            if (file.lastModified() < cutoff) {
                file.delete()
            }
        }
        enforceQuotaAndPrune(context)
    }

    fun deleteCachedFile(path: String): Boolean {
        return try {
            val f = File(path)
            if (f.exists()) f.delete() else false
        } catch (_: Exception) {
            false
        }
    }

    fun clearAllCache(context: Context) {
        try {
            val dir = getCacheDir(context)
            dir.listFiles()?.forEach { it.deleteRecursively() }
        } catch (_: Exception) {}
    }

    fun openQuickView(context: Context, filePath: String) {
        val file = File(filePath)
        if (!file.exists()) {
            Toast.makeText(context, "File no longer in preview cache", Toast.LENGTH_SHORT).show()
            return
        }

        file.setLastModified(System.currentTimeMillis())

        try {
            val uri: Uri = FileProvider.getUriForFile(
                context,
                "com.connecttophone.fileprovider",
                file
            )

            val mime = context.contentResolver.getType(uri) ?: getMimeFromExt(file.extension)
            val intent = Intent(Intent.ACTION_VIEW).apply {
                setDataAndType(uri, mime)
                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            }
            context.startActivity(Intent.createChooser(intent, "Quick View"))
        } catch (e: Exception) {
            Toast.makeText(context, "Cannot preview: ${e.message}", Toast.LENGTH_LONG).show()
        }
    }

    fun playNotificationSound(context: Context) {
        try {
            val notificationUri = RingtoneManager.getDefaultUri(RingtoneManager.TYPE_NOTIFICATION)
            val ringtone = RingtoneManager.getRingtone(context.applicationContext, notificationUri)
            ringtone?.play()
        } catch (_: Exception) {}
    }

    private fun determineCategory(ext: String): String {
        return when (ext.lowercase()) {
            "jpg", "jpeg", "png", "webp", "gif", "bmp" -> "Image"
            "pdf", "doc", "docx", "txt", "xlsx", "csv" -> "Document"
            "mp4", "mkv", "avi", "mov", "webm" -> "Video"
            "mp3", "flac", "wav", "m4a", "ogg" -> "Audio"
            else -> "Other"
        }
    }

    private fun getMimeFromExt(ext: String): String {
        return when (ext.lowercase()) {
            "mkv", "mp4", "avi", "mov", "webm" -> "video/*"
            "mp3", "flac", "wav", "m4a", "ogg" -> "audio/*"
            "jpg", "jpeg", "png", "webp", "gif" -> "image/*"
            "pdf" -> "application/pdf"
            "txt" -> "text/plain"
            else -> "*/*"
        }
    }
}
