package com.connecttophone.service

import android.app.PendingIntent
import android.content.Intent
import android.graphics.drawable.Icon
import android.os.Build
import android.service.quicksettings.Tile
import android.service.quicksettings.TileService
import androidx.annotation.RequiresApi
import com.connecttophone.AppState
import com.connecttophone.MainActivity
import com.connecttophone.R

/**
 * Android Quick Settings (Control Panel) Tile Service.
 * Allows user to control and view ConnectToWindow link directly from Android's top control panel.
 */
@RequiresApi(Build.VERSION_CODES.N)
class QuickConnectTileService : TileService() {

    override fun onStartListening() {
        super.onStartListening()
        updateTileState()
    }

    override fun onClick() {
        super.onClick()
        val tile = qsTile ?: return

        if (!AppState.isConnected) {
            AppState.connectOrRefreshPc()
            tile.state = Tile.STATE_ACTIVE
            tile.label = "ConnectToWindow"
            tile.subtitle = "Connecting..."
            tile.updateTile()
        }

        // Launch app
        val launchIntent = Intent(this, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            val pendingIntent = PendingIntent.getActivity(
                this, 0, launchIntent,
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
            )
            startActivityAndCollapse(pendingIntent)
        } else {
            @Suppress("DEPRECATION")
            startActivityAndCollapse(launchIntent)
        }
    }

    private fun updateTileState() {
        val tile = qsTile ?: return
        if (AppState.isConnected) {
            tile.state = Tile.STATE_ACTIVE
            tile.label = "ConnectToWindow"
            tile.subtitle = "${AppState.connectedDeviceName} ⚡"
        } else {
            tile.state = Tile.STATE_INACTIVE
            tile.label = "ConnectToWindow"
            tile.subtitle = "Tap to Connect"
        }
        tile.icon = Icon.createWithResource(this, R.drawable.ic_qs_tile)
        tile.updateTile()
    }
}
