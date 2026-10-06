package com.connecttophone.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.os.Binder
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat
import com.connecttophone.MainActivity

/**
 * Android Foreground Service displaying real-time transfer speed, progress bar,
 * and quick actions directly in the Android Status Bar / Top Bar.
 */
class TransferForegroundService : Service() {

    private val binder = LocalBinder()
    private lateinit var notificationManager: NotificationManager

    companion object {
        const val CHANNEL_ID = "c2p_transfers"
        const val NOTIFICATION_ID = 1001

        const val ACTION_START = "com.connecttophone.service.START"
        const val ACTION_STOP = "com.connecttophone.service.STOP"
        const val ACTION_PAUSE = "com.connecttophone.service.PAUSE"
        const val ACTION_RESUME = "com.connecttophone.service.RESUME"
        const val ACTION_CANCEL = "com.connecttophone.service.CANCEL"

        var isServiceRunning = false

        fun startService(context: Context) {
            val intent = Intent(context, TransferForegroundService::class.java).apply {
                action = ACTION_START
            }
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                context.startForegroundService(intent)
            } else {
                context.startService(intent)
            }
        }
    }

    inner class LocalBinder : Binder() {
        fun getService(): TransferForegroundService = this@TransferForegroundService
    }

    override fun onCreate() {
        super.onCreate()
        isServiceRunning = true
        notificationManager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        createNotificationChannel()
    }

    override fun onDestroy() {
        isServiceRunning = false
        super.onDestroy()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        isServiceRunning = true
        when (intent?.action) {
            ACTION_STOP -> stopForegroundService()
            ACTION_PAUSE -> { /* Handle pause */ }
            ACTION_RESUME -> { /* Handle resume */ }
            ACTION_CANCEL -> { /* Handle cancel */ }
            else -> {
                startForeground(NOTIFICATION_ID, buildInitialNotification())
            }
        }
        return START_STICKY
    }

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(
                CHANNEL_ID,
                "ConnectToWindow Transfers & Sync",
                NotificationManager.IMPORTANCE_LOW
            ).apply {
                description = "Keeps zero-idle background sync and transfers active"
                setShowBadge(false)
            }
            notificationManager.createNotificationChannel(channel)
        }
    }

    private fun buildInitialNotification(): Notification {
        val launchIntent = Intent(this, MainActivity::class.java)
        val pendingIntent = PendingIntent.getActivity(
            this, 0, launchIntent,
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        )

        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle("ConnectToWindow ⚡ (Background Active)")
            .setContentText("Zero-idle clipboard sync & file pulls ready")
            .setSmallIcon(android.R.drawable.stat_sys_upload)
            .setContentIntent(pendingIntent)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .build()
    }

    /**
     * Updates the status bar notification with real-time transfer stats.
     * Displays: ⚡ 124.5 MB/s | USB + Wi-Fi
     */
    fun updateTransferProgress(
        fileName: String,
        bytesTransferred: Long,
        totalBytes: Long,
        speedBytesPerSec: Double,
        activeTransportsLabel: String,
        etaSeconds: Int
    ) {
        val progress = if (totalBytes > 0) ((bytesTransferred * 100) / totalBytes).toInt() else 0
        val speedMb = speedBytesPerSec / (1024.0 * 1024.0)
        val transferredMb = bytesTransferred / (1024.0 * 1024.0)
        val totalMb = totalBytes / (1024.0 * 1024.0)

        val speedTitle = String.format("⚡ %.1f MB/s • %s", speedMb, activeTransportsLabel)
        val contentText = String.format("%s: %d%% (%.1f / %.1f MB) • ETA: %ds", fileName, progress, transferredMb, totalMb, etaSeconds)

        val pauseIntent = Intent(this, TransferForegroundService::class.java).apply { action = ACTION_PAUSE }
        val pendingPause = PendingIntent.getService(this, 1, pauseIntent, PendingIntent.FLAG_IMMUTABLE)

        val cancelIntent = Intent(this, TransferForegroundService::class.java).apply { action = ACTION_CANCEL }
        val pendingCancel = PendingIntent.getService(this, 2, cancelIntent, PendingIntent.FLAG_IMMUTABLE)

        val notification = NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle(speedTitle)
            .setContentText(contentText)
            .setSmallIcon(android.R.drawable.stat_sys_upload)
            .setProgress(100, progress, false)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .addAction(android.R.drawable.ic_media_pause, "Pause", pendingPause)
            .addAction(android.R.drawable.ic_menu_close_clear_cancel, "Cancel", pendingCancel)
            .build()

        notificationManager.notify(NOTIFICATION_ID, notification)
    }

    fun stopForegroundService() {
        stopForeground(STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    override fun onBind(intent: Intent?): IBinder = binder
}
