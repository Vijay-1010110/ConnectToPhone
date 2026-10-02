package com.connecttophone.widget

import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.widget.RemoteViews
import com.connecttophone.AppState
import com.connecttophone.MainActivity
import com.connecttophone.R

class ConnectStatusWidgetProvider : AppWidgetProvider() {

    override fun onUpdate(context: Context, appWidgetManager: AppWidgetManager, appWidgetIds: IntArray) {
        for (widgetId in appWidgetIds) {
            updateWidget(context, appWidgetManager, widgetId)
        }
    }

    override fun onReceive(context: Context, intent: Intent) {
        super.onReceive(context, intent)
        when (intent.action) {
            ACTION_CONNECT -> {
                AppState.connectOrRefreshPc()
                updateAllWidgets(context)
            }
            AppWidgetManager.ACTION_APPWIDGET_UPDATE -> {
                updateAllWidgets(context)
            }
        }
    }

    companion object {
        const val ACTION_CONNECT = "com.connecttophone.widget.ACTION_CONNECT"

        fun updateWidget(context: Context, appWidgetManager: AppWidgetManager, widgetId: Int) {
            val views = RemoteViews(context.packageName, R.layout.widget_connect_status)

            if (AppState.isConnected) {
                views.setTextViewText(R.id.widget_status_badge, "⚡ Linked")
                views.setTextColor(R.id.widget_status_badge, 0xFF10B981.toInt())
                views.setTextViewText(R.id.widget_device_name, "PC: ${AppState.connectedDeviceName} (${AppState.activeProtocolName})")
            } else {
                views.setTextViewText(R.id.widget_status_badge, "💤 Standby")
                views.setTextColor(R.id.widget_status_badge, 0xFFF59E0B.toInt())
                views.setTextViewText(R.id.widget_device_name, "PC: Disconnected • Tap Connect to Link")
            }

            // Connect button intent
            val connectIntent = Intent(context, ConnectStatusWidgetProvider::class.java).apply {
                action = ACTION_CONNECT
            }
            val connectPending = PendingIntent.getBroadcast(
                context, 101, connectIntent,
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
            )
            views.setOnClickPendingIntent(R.id.btn_widget_connect, connectPending)

            // Open App to PC Files intent
            val filesIntent = Intent(context, MainActivity::class.java).apply {
                flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP
                putExtra("TARGET_TAB", 1)
            }
            val filesPending = PendingIntent.getActivity(
                context, 102, filesIntent,
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
            )
            views.setOnClickPendingIntent(R.id.btn_widget_files, filesPending)

            // Open App to Clipboard intent
            val clipIntent = Intent(context, MainActivity::class.java).apply {
                flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP
                putExtra("TARGET_TAB", 2)
            }
            val clipPending = PendingIntent.getActivity(
                context, 103, clipIntent,
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
            )
            views.setOnClickPendingIntent(R.id.btn_widget_clip, clipPending)

            // Root click opens main app
            val mainIntent = Intent(context, MainActivity::class.java).apply {
                flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP
            }
            val mainPending = PendingIntent.getActivity(
                context, 100, mainIntent,
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
            )
            views.setOnClickPendingIntent(R.id.widget_root, mainPending)

            appWidgetManager.updateAppWidget(widgetId, views)
        }

        fun updateAllWidgets(context: Context) {
            val appWidgetManager = AppWidgetManager.getInstance(context)
            val ids = appWidgetManager.getAppWidgetIds(
                ComponentName(context, ConnectStatusWidgetProvider::class.java)
            )
            for (id in ids) {
                updateWidget(context, appWidgetManager, id)
            }
        }
    }
}
