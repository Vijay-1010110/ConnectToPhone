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

class RemoteControlWidgetProvider : AppWidgetProvider() {

    override fun onUpdate(context: Context, appWidgetManager: AppWidgetManager, appWidgetIds: IntArray) {
        for (widgetId in appWidgetIds) {
            updateWidget(context, appWidgetManager, widgetId)
        }
    }

    override fun onReceive(context: Context, intent: Intent) {
        super.onReceive(context, intent)
        when (intent.action) {
            ACTION_PREV -> AppState.sendMediaAction(2)
            ACTION_PLAY -> AppState.sendMediaAction(0)
            ACTION_NEXT -> AppState.sendMediaAction(1)
            ACTION_VOL_DOWN -> AppState.sendMediaAction(4)
            ACTION_VOL_UP -> AppState.sendMediaAction(3)
            ACTION_LOCK -> AppState.sendPowerAction(0)
            AppWidgetManager.ACTION_APPWIDGET_UPDATE -> updateAllWidgets(context)
        }
    }

    companion object {
        const val ACTION_PREV = "com.connecttophone.widget.ACTION_PREV"
        const val ACTION_PLAY = "com.connecttophone.widget.ACTION_PLAY"
        const val ACTION_NEXT = "com.connecttophone.widget.ACTION_NEXT"
        const val ACTION_VOL_DOWN = "com.connecttophone.widget.ACTION_VOL_DOWN"
        const val ACTION_VOL_UP = "com.connecttophone.widget.ACTION_VOL_UP"
        const val ACTION_LOCK = "com.connecttophone.widget.ACTION_LOCK"

        fun updateWidget(context: Context, appWidgetManager: AppWidgetManager, widgetId: Int) {
            val views = RemoteViews(context.packageName, R.layout.widget_remote_control)

            views.setTextViewText(
                R.id.widget_remote_device,
                if (AppState.isConnected) AppState.connectedDeviceName else "Offline"
            )

            // Setup button intents
            fun setAction(btnId: Int, action: String, reqCode: Int) {
                val intent = Intent(context, RemoteControlWidgetProvider::class.java).apply {
                    this.action = action
                }
                val pending = PendingIntent.getBroadcast(
                    context, reqCode, intent,
                    PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
                )
                views.setOnClickPendingIntent(btnId, pending)
            }

            setAction(R.id.btn_remote_prev, ACTION_PREV, 201)
            setAction(R.id.btn_remote_play, ACTION_PLAY, 202)
            setAction(R.id.btn_remote_next, ACTION_NEXT, 203)
            setAction(R.id.btn_remote_voldown, ACTION_VOL_DOWN, 204)
            setAction(R.id.btn_remote_volup, ACTION_VOL_UP, 205)
            setAction(R.id.btn_remote_lock, ACTION_LOCK, 206)

            // Click root opens remote tab in app
            val mainIntent = Intent(context, MainActivity::class.java).apply {
                flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP
                putExtra("TARGET_TAB", 3)
            }
            val mainPending = PendingIntent.getActivity(
                context, 200, mainIntent,
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
            )
            views.setOnClickPendingIntent(R.id.widget_remote_root, mainPending)

            appWidgetManager.updateAppWidget(widgetId, views)
        }

        fun updateAllWidgets(context: Context) {
            val appWidgetManager = AppWidgetManager.getInstance(context)
            val ids = appWidgetManager.getAppWidgetIds(
                ComponentName(context, RemoteControlWidgetProvider::class.java)
            )
            for (id in ids) {
                updateWidget(context, appWidgetManager, id)
            }
        }
    }
}
