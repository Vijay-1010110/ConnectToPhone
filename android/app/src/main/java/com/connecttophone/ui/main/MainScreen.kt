package com.connecttophone.ui.main

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.net.Uri
import android.widget.Toast
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.navigation3.runtime.NavKey
import com.connecttophone.AppState
import com.connecttophone.RemoteFileItem
import com.connecttophone.TransferHistoryItem
import com.connecttophone.cache.CacheManager
import com.connecttophone.cache.CachedItem
import com.connecttophone.cache.PhoneStorageInfo
import com.connecttophone.service.TransferForegroundService
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun MainScreen(
    onItemClick: (NavKey) -> Unit = {},
    modifier: Modifier = Modifier
) {
    val context = LocalContext.current
    val selectedTab = AppState.selectedNavigationTab
    val coroutineScope = rememberCoroutineScope()

    // Clipboard state
    var autoClipboardSync by remember { mutableStateOf(true) }

    // PC PIN Pairing Dialog
    if (AppState.isPairingDialogVisible) {
        var pinInput by remember { mutableStateOf("") }
        AlertDialog(
            onDismissRequest = { AppState.isPairingDialogVisible = false },
            title = { Text("Pair with Windows PC", fontWeight = FontWeight.Bold) },
            text = {
                Column {
                    Text("Enter the 6-digit PIN displayed on your ConnectToPhone desktop screen:", fontSize = 13.sp)
                    Spacer(Modifier.height(12.dp))
                    OutlinedTextField(
                        value = pinInput,
                        onValueChange = { if (it.length <= 6) pinInput = it },
                        label = { Text("6-Digit PIN") },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
                    )
                }
            },
            confirmButton = {
                Button(
                    onClick = {
                        if (pinInput.length == 6) {
                            AppState.pairWithPin(pinInput)
                            Toast.makeText(context, "Pairing with PC using PIN $pinInput...", Toast.LENGTH_SHORT).show()
                        } else {
                            Toast.makeText(context, "Please enter 6-digit PIN", Toast.LENGTH_SHORT).show()
                        }
                    }
                ) {
                    Text("Pair & Connect")
                }
            },
            dismissButton = {
                OutlinedButton(onClick = { AppState.isPairingDialogVisible = false }) {
                    Text("Cancel")
                }
            }
        )
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Column(
                        modifier = Modifier.clickable {
                            AppState.selectedNavigationTab = 2
                        }
                    ) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Text("ConnectToWindow", fontWeight = FontWeight.Bold, fontSize = 18.sp)
                            Spacer(Modifier.width(8.dp))
                            Box(
                                modifier = Modifier
                                    .size(8.dp)
                                    .background(
                                        if (AppState.isConnected) Color(0xFF10B981) else Color(0xFFEF4444),
                                        CircleShape
                                    )
                            )
                        }
                        Text(
                            if (AppState.isConnected) "Connected: ${AppState.connectedDeviceName.ifEmpty { AppState.targetPcName }}" else "Waiting for PC • Tap to Pair...",
                            fontSize = 12.sp,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                },
                actions = {
                    Surface(
                        color = MaterialTheme.colorScheme.primaryContainer,
                        shape = RoundedCornerShape(8.dp),
                        modifier = Modifier
                            .padding(end = 12.dp)
                            .clickable {
                                if (AppState.isConnected) {
                                    AppState.forceSyncNow(context)
                                } else {
                                    AppState.selectedNavigationTab = 2
                                }
                            }
                    ) {
                        Text(
                            text = if (AppState.isConnected) (if (AppState.currentSpeedMb > 0) "⚡ %.1f MB/s".format(AppState.currentSpeedMb) else "⚡ Sync") else "📱 Pair PC",
                            color = MaterialTheme.colorScheme.onPrimaryContainer,
                            fontWeight = FontWeight.Bold,
                            fontSize = 13.sp,
                            modifier = Modifier.padding(horizontal = 10.dp, vertical = 6.dp)
                        )
                    }
                }
            )
        },
        bottomBar = {
            NavigationBar {
                NavigationBarItem(
                    selected = selectedTab == 0,
                    onClick = { AppState.selectedNavigationTab = 0 },
                    icon = { Text("⚡", fontSize = 18.sp) },
                    label = { Text("Transfers") }
                )
                NavigationBarItem(
                    selected = selectedTab == 1,
                    onClick = {
                        AppState.selectedNavigationTab = 1
                        if (AppState.isConnected && AppState.pcEntries.isEmpty()) {
                            AppState.requestPcDirectory("/")
                        }
                    },
                    icon = { Text("💻", fontSize = 18.sp) },
                    label = { Text("PC Files") }
                )
                NavigationBarItem(
                    selected = selectedTab == 2,
                    onClick = { AppState.selectedNavigationTab = 2 },
                    icon = { Text("📱", fontSize = 18.sp) },
                    label = { Text("Devices") }
                )
                NavigationBarItem(
                    selected = selectedTab == 3,
                    onClick = { AppState.selectedNavigationTab = 3 },
                    icon = { Text("🎮", fontSize = 18.sp) },
                    label = { Text("Remote") }
                )
                NavigationBarItem(
                    selected = selectedTab == 4,
                    onClick = { AppState.selectedNavigationTab = 4 },
                    icon = { Text("📋", fontSize = 18.sp) },
                    label = { Text("Clipboard") }
                )
            }
        }
    ) { innerPadding ->
        Box(
            modifier = modifier
                .fillMaxSize()
                .padding(innerPadding)
                .padding(horizontal = 8.dp, vertical = 4.dp)
        ) {
            when (selectedTab) {
                0 -> TransfersTab(
                    context = context,
                    history = AppState.transferHistory,
                    coroutineScope = coroutineScope
                )
                1 -> PcExplorerTab(
                    context = context,
                    currentPath = AppState.currentPcPath,
                    entries = AppState.pcEntries,
                    onNavigate = { AppState.requestPcDirectory(it) },
                    onPull = { path, isPreview -> AppState.pullFileFromPc(path, isPreview, context) }
                )
                2 -> DevicesTab(
                    context = context,
                    coroutineScope = coroutineScope
                )
                3 -> RemoteControlTab(context = context)
                4 -> ClipboardTab(
                    context = context,
                    autoSync = autoClipboardSync,
                    onAutoSyncChange = { autoClipboardSync = it },
                    history = AppState.clipboardHistory
                )
                5 -> StorageTab(context = context)
            }
        }
    }
}

@Composable
fun TransfersTab(
    context: Context,
    history: List<TransferHistoryItem>,
    coroutineScope: CoroutineScope
) {
    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        item {
            // Sleek Connection Summary Bar linking to Devices Hub
            Card(
                colors = CardDefaults.cardColors(
                    containerColor = if (AppState.isConnected) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surfaceVariant
                ),
                shape = RoundedCornerShape(12.dp),
                modifier = Modifier
                    .fillMaxWidth()
                    .clickable { AppState.selectedNavigationTab = 2 }
            ) {
                Row(
                    modifier = Modifier.padding(horizontal = 14.dp, vertical = 10.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Box(
                        modifier = Modifier
                            .size(10.dp)
                            .background(
                                if (AppState.isConnected) Color(0xFF10B981) else Color(0xFFEF4444),
                                CircleShape
                            )
                    )
                    Spacer(Modifier.width(10.dp))
                    Column(Modifier.weight(1f)) {
                        Text(
                            text = if (AppState.isConnected) "Connected: ${AppState.connectedDeviceName.ifEmpty { AppState.targetPcName }}" else "Disconnected • Tap to Pair PC",
                            fontWeight = FontWeight.Bold,
                            fontSize = 13.sp,
                            maxLines = 1
                        )
                        Text(
                            text = if (AppState.isConnected) "Via ${AppState.activeProtocolName} • Tap to manage pairing & devices" else "Scan QR, enter PIN or connect via Wi-Fi/USB",
                            fontSize = 11.sp,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                    if (AppState.isConnected) {
                        FilledTonalButton(
                            onClick = { AppState.forceSyncNow(context) },
                            contentPadding = PaddingValues(horizontal = 10.dp, vertical = 4.dp),
                            shape = RoundedCornerShape(8.dp)
                        ) {
                            Text("🔄 Sync", fontSize = 11.sp, fontWeight = FontWeight.Bold)
                        }
                    } else {
                        Button(
                            onClick = { AppState.selectedNavigationTab = 2 },
                            contentPadding = PaddingValues(horizontal = 10.dp, vertical = 4.dp),
                            shape = RoundedCornerShape(8.dp)
                        ) {
                            Text("📱 Pair", fontSize = 11.sp, fontWeight = FontWeight.Bold)
                        }
                    }
                }
            }
        }

        item {
            // Speed breakdown card - All 3 Protocols (USB, Wi-Fi, Bluetooth)
            Card(
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
                shape = RoundedCornerShape(12.dp)
            ) {
                Column(Modifier.padding(12.dp)) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text("Multi-Path Bonded Status", fontWeight = FontWeight.Bold, fontSize = 14.sp)
                        Text(
                            text = "Active: ${AppState.activeProtocolName}",
                            fontSize = 11.sp,
                            fontWeight = FontWeight.Bold,
                            color = MaterialTheme.colorScheme.primary
                        )
                    }
                    Spacer(Modifier.height(8.dp))
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(6.dp)
                    ) {
                        Surface(
                            color = Color(0xFF1E3A8A),
                            shape = RoundedCornerShape(8.dp),
                            modifier = Modifier.weight(1f)
                        ) {
                            Text(
                                "⚡ USB: ${if (AppState.isConnected && AppState.activeProtocolName.contains("USB")) "Active" else "Ready"}",
                                color = Color(0xFF93C5FD),
                                fontSize = 11.sp,
                                fontWeight = FontWeight.Bold,
                                modifier = Modifier.padding(horizontal = 6.dp, vertical = 4.dp)
                            )
                        }
                        Surface(
                            color = Color(0xFF064E3B),
                            shape = RoundedCornerShape(8.dp),
                            modifier = Modifier.weight(1f)
                        ) {
                            Text(
                                "📶 Wi-Fi: ${if (AppState.isConnected && AppState.activeProtocolName.contains("Wi-Fi")) "Active" else "Ready"}",
                                color = Color(0xFF6EE7B7),
                                fontSize = 11.sp,
                                fontWeight = FontWeight.Bold,
                                modifier = Modifier.padding(horizontal = 6.dp, vertical = 4.dp)
                            )
                        }
                        Surface(
                            color = Color(0xFF312E81),
                            shape = RoundedCornerShape(8.dp),
                            modifier = Modifier.weight(1f)
                        ) {
                            Text(
                                "📱 BT: ${if (AppState.isBluetoothConnected) "Active" else if (AppState.isBluetoothEnabled) "Ready" else "Off"}",
                                color = Color(0xFFC7D2FE),
                                fontSize = 11.sp,
                                fontWeight = FontWeight.Bold,
                                modifier = Modifier.padding(horizontal = 6.dp, vertical = 4.dp)
                            )
                        }
                    }
                }
            }
        }

        item {
            OutlinedButton(
                onClick = { AppState.selectedNavigationTab = 5 },
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(8.dp)
            ) {
                Text("💾 View Cached Previews & Storage Management (${CacheManager.getStorageInfo(context).cacheUsedDisplay})", fontSize = 12.sp)
            }
        }

        item {
            Card(
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.secondaryContainer),
                shape = RoundedCornerShape(12.dp)
            ) {
                Column(Modifier.padding(12.dp)) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text("Completed Files", fontWeight = FontWeight.Bold, fontSize = 15.sp)
                        Text("${history.size} items", fontSize = 12.sp, color = MaterialTheme.colorScheme.onSecondaryContainer)
                    }
                    Spacer(Modifier.height(4.dp))
                    Text("Tap any file to open directly in video/audio player or file manager", fontSize = 11.sp, color = MaterialTheme.colorScheme.onSecondaryContainer.copy(alpha = 0.8f))
                }
            }
        }

        if (history.isEmpty()) {
            item {
                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(32.dp),
                    contentAlignment = Alignment.Center
                ) {
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        Text("📁", fontSize = 36.sp)
                        Spacer(Modifier.height(8.dp))
                        Text(
                            "No files transferred yet",
                            fontWeight = FontWeight.SemiBold,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                }
            }
        } else {
            items(history) { item ->
                Card(
                    modifier = Modifier
                        .fillMaxWidth()
                        .clickable { AppState.openFile(context, item) },
                    shape = RoundedCornerShape(8.dp)
                ) {
                    Column(Modifier.padding(12.dp)) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Text("🎬", fontSize = 24.sp)
                            Spacer(Modifier.width(12.dp))
                            Column(Modifier.weight(1f)) {
                                Text(item.fileName, fontWeight = FontWeight.SemiBold, fontSize = 14.sp)
                                Text(
                                    "${item.sizeDisplay} • Ready on Phone",
                                    fontSize = 11.sp,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                        }

                        Spacer(Modifier.height(8.dp))
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.End
                        ) {
                            FilledTonalButton(
                                onClick = { AppState.openFile(context, item) },
                                modifier = Modifier.padding(end = 8.dp)
                            ) {
                                Text("▶ Open File")
                            }
                        }
                    }
                }
            }
        }

        item {
            Button(
                onClick = {
                    TransferForegroundService.startService(context)
                    Toast.makeText(context, "Speed meter active in Android Top Bar!", Toast.LENGTH_SHORT).show()
                },
                modifier = Modifier.fillMaxWidth()
            ) {
                Text("🔔")
                Spacer(Modifier.width(8.dp))
                Text("Show Live Speed in Android Status Bar")
            }
        }
    }
}

fun parsePairUrlAndConnect(url: String, context: Context) {
    try {
        val uri = Uri.parse(url)
        val ip = uri.getQueryParameter("ip") ?: if (url.contains(".") && !url.contains("/")) url.trim() else null
        val name = uri.getQueryParameter("name") ?: "Windows PC"
        val port = uri.getQueryParameter("port")?.toIntOrNull() ?: 42424
        val pin = uri.getQueryParameter("pin")
        if (!ip.isNullOrEmpty()) {
            AppState.targetPcName = name
            AppState.targetPcIp = ip
            AppState.targetPcPort = port
            if (!pin.isNullOrEmpty()) {
                AppState.pairWithPin(pin)
            }
            AppState.connectToDirectIp(ip, port, pin)
            Toast.makeText(context, "Connecting to $name ($ip:$port)...", Toast.LENGTH_SHORT).show()
        } else {
            Toast.makeText(context, "Invalid pairing URL or IP", Toast.LENGTH_SHORT).show()
        }
    } catch (e: Exception) {
        Toast.makeText(context, "Error parsing pairing URL: ${e.message}", Toast.LENGTH_SHORT).show()
    }
}

@Composable
fun DevicesTab(
    context: Context,
    coroutineScope: CoroutineScope
) {
    var manualPinInput by remember { mutableStateOf("") }
    var manualIpInput by remember { mutableStateOf(AppState.targetPcIp.ifEmpty { "192.168.250.225" }) }
    var manualPortInput by remember { mutableStateOf(AppState.targetPcPort.toString()) }
    var qrLinkInput by remember { mutableStateOf("") }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        // 1. Hero Device Status Card
        item {
            Card(
                colors = CardDefaults.cardColors(
                    containerColor = if (AppState.isConnected) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surfaceVariant
                ),
                shape = RoundedCornerShape(16.dp),
                border = BorderStroke(1.dp, if (AppState.isConnected) MaterialTheme.colorScheme.primary.copy(alpha = 0.4f) else MaterialTheme.colorScheme.outline.copy(alpha = 0.2f))
            ) {
                Column(Modifier.padding(16.dp)) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Surface(
                            shape = CircleShape,
                            color = if (AppState.isConnected) Color(0xFF10B981).copy(alpha = 0.2f) else Color(0xFFEF4444).copy(alpha = 0.2f),
                            modifier = Modifier.size(54.dp)
                        ) {
                            Box(contentAlignment = Alignment.Center) {
                                Text(if (AppState.isConnected) "💻" else "📡", fontSize = 28.sp)
                            }
                        }
                        Spacer(Modifier.width(14.dp))
                        Column(modifier = Modifier.weight(1f)) {
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Text(
                                    text = if (AppState.isConnected) AppState.connectedDeviceName.ifEmpty { AppState.targetPcName } else "No Device Connected",
                                    fontWeight = FontWeight.Bold,
                                    fontSize = 17.sp,
                                    maxLines = 1
                                )
                                Spacer(Modifier.width(6.dp))
                                Box(
                                    modifier = Modifier
                                        .size(8.dp)
                                        .background(
                                            if (AppState.isConnected) Color(0xFF10B981) else Color(0xFFEF4444),
                                            CircleShape
                                        )
                                )
                            }
                            Spacer(Modifier.height(2.dp))
                            Text(
                                text = if (AppState.isConnected)
                                    "Active: ${AppState.activeProtocolName} • Multi-Path Bonded"
                                else
                                    "Waiting for PC connection over USB, Wi-Fi or Bluetooth",
                                fontSize = 12.sp,
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                    }

                    Spacer(Modifier.height(14.dp))

                    if (AppState.isConnected) {
                        // Force Sync button
                        Button(
                            onClick = { AppState.forceSyncNow(context) },
                            modifier = Modifier.fillMaxWidth(),
                            shape = RoundedCornerShape(10.dp),
                            colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.primary)
                        ) {
                            Text("🔄 Force Sync Now (Files & Clipboard)", fontWeight = FontWeight.Bold, fontSize = 13.sp)
                        }

                        Spacer(Modifier.height(8.dp))

                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.spacedBy(8.dp)
                        ) {
                            Button(
                                onClick = {
                                    AppState.disconnectPc()
                                    Toast.makeText(context, "Disconnected from PC.", Toast.LENGTH_SHORT).show()
                                },
                                colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF991B1B)),
                                shape = RoundedCornerShape(8.dp),
                                modifier = Modifier.weight(1f)
                            ) {
                                Text("⚡ Disconnect", fontSize = 12.sp, fontWeight = FontWeight.Bold)
                            }

                            OutlinedButton(
                                onClick = {
                                    AppState.unpairPc()
                                    Toast.makeText(context, "Unpaired PC successfully.", Toast.LENGTH_SHORT).show()
                                },
                                shape = RoundedCornerShape(8.dp),
                                modifier = Modifier.weight(1f)
                            ) {
                                Text("✕ Unpair", fontSize = 12.sp)
                            }
                        }
                    } else {
                        Button(
                            onClick = {
                                AppState.reconnectPc()
                                Toast.makeText(context, "Scanning & connecting to PC...", Toast.LENGTH_SHORT).show()
                            },
                            modifier = Modifier.fillMaxWidth(),
                            shape = RoundedCornerShape(10.dp),
                            colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF2563EB))
                        ) {
                            Text("🔄 Connect / Reconnect PC", fontWeight = FontWeight.Bold, fontSize = 13.sp)
                        }
                    }
                }
            }
        }

        // 2. Pair New PC Hub (Phone's PIN, PC PIN, QR Code Link, Direct IP)
        item {
            Card(
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
                shape = RoundedCornerShape(16.dp)
            ) {
                Column(Modifier.padding(16.dp)) {
                    Text("🔐 Pair New Windows PC", fontWeight = FontWeight.Bold, fontSize = 15.sp)
                    Spacer(Modifier.height(6.dp))
                    Text(
                        "Pairing authorizes encrypted end-to-end file transfers, clipboard sharing, and remote touch control.",
                        fontSize = 11.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )

                    Spacer(Modifier.height(14.dp))

                    // A. Phone's 6-Digit PIN Display
                    Text("1. This Phone's Pairing PIN:", fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
                    Spacer(Modifier.height(6.dp))
                    val pin = AppState.phonePairingPin.padEnd(6, '0')
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.Center,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        for (i in 0 until 3) {
                            Surface(
                                shape = RoundedCornerShape(8.dp),
                                color = MaterialTheme.colorScheme.surface,
                                border = BorderStroke(1.dp, MaterialTheme.colorScheme.primary.copy(alpha = 0.5f)),
                                modifier = Modifier.size(38.dp, 44.dp)
                            ) {
                                Box(contentAlignment = Alignment.Center) {
                                    Text(pin.getOrNull(i)?.toString() ?: "-", fontSize = 20.sp, fontWeight = FontWeight.Bold, fontFamily = FontFamily.Monospace)
                                }
                            }
                            if (i < 2) Spacer(Modifier.width(6.dp))
                        }
                        Text("  -  ", fontWeight = FontWeight.Bold, fontSize = 18.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                        for (i in 3 until 6) {
                            Surface(
                                shape = RoundedCornerShape(8.dp),
                                color = MaterialTheme.colorScheme.surface,
                                border = BorderStroke(1.dp, MaterialTheme.colorScheme.primary.copy(alpha = 0.5f)),
                                modifier = Modifier.size(38.dp, 44.dp)
                            ) {
                                Box(contentAlignment = Alignment.Center) {
                                    Text(pin.getOrNull(i)?.toString() ?: "-", fontSize = 20.sp, fontWeight = FontWeight.Bold, fontFamily = FontFamily.Monospace)
                                }
                            }
                            if (i < 5) Spacer(Modifier.width(6.dp))
                        }
                    }
                    Spacer(Modifier.height(8.dp))
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.Center
                    ) {
                        OutlinedButton(
                            onClick = {
                                val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as? ClipboardManager
                                clipboard?.setPrimaryClip(ClipData.newPlainText("Pairing PIN", AppState.phonePairingPin))
                                Toast.makeText(context, "PIN copied to clipboard: ${AppState.phonePairingPin}", Toast.LENGTH_SHORT).show()
                            },
                            shape = RoundedCornerShape(8.dp),
                            contentPadding = PaddingValues(horizontal = 12.dp, vertical = 4.dp)
                        ) {
                            Text("📋 Copy Phone PIN", fontSize = 11.sp)
                        }
                    }

                    Spacer(Modifier.height(14.dp))
                    HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.5f))
                    Spacer(Modifier.height(14.dp))

                    // B. Enter PC PIN
                    Text("2. Or Enter PC's 6-Digit PIN:", fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
                    Spacer(Modifier.height(6.dp))
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        OutlinedTextField(
                            value = manualPinInput,
                            onValueChange = { if (it.length <= 6) manualPinInput = it },
                            placeholder = { Text("e.g. 849201", fontSize = 12.sp) },
                            singleLine = true,
                            modifier = Modifier.weight(1f)
                        )
                        Button(
                            onClick = {
                                if (manualPinInput.length == 6) {
                                    AppState.pairWithPin(manualPinInput)
                                    Toast.makeText(context, "Paired with PC using PIN $manualPinInput!", Toast.LENGTH_SHORT).show()
                                    manualPinInput = ""
                                } else {
                                    Toast.makeText(context, "Please enter full 6-digit PIN", Toast.LENGTH_SHORT).show()
                                }
                            },
                            shape = RoundedCornerShape(8.dp)
                        ) {
                            Text("Pair PIN", fontSize = 12.sp, fontWeight = FontWeight.Bold)
                        }
                    }

                    Spacer(Modifier.height(14.dp))
                    HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.5f))
                    Spacer(Modifier.height(14.dp))

                    // C. QR Code URL / Deep Link Input
                    Text("3. Scan Desktop QR Code or Paste Link:", fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
                    Spacer(Modifier.height(6.dp))
                    OutlinedTextField(
                        value = qrLinkInput,
                        onValueChange = { qrLinkInput = it },
                        placeholder = { Text("connecttowindow://pair?name=...&ip=...&pin=...", fontSize = 11.sp) },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
                    )
                    Spacer(Modifier.height(6.dp))
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        OutlinedButton(
                            onClick = {
                                val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as? ClipboardManager
                                val clip = clipboard?.primaryClip?.getItemAt(0)?.text?.toString() ?: ""
                                if (clip.isNotEmpty()) {
                                    qrLinkInput = clip
                                    parsePairUrlAndConnect(clip, context)
                                } else {
                                    Toast.makeText(context, "Clipboard is empty", Toast.LENGTH_SHORT).show()
                                }
                            },
                            shape = RoundedCornerShape(8.dp),
                            modifier = Modifier.weight(1f)
                        ) {
                            Text("📋 Paste & Pair", fontSize = 11.sp)
                        }

                        Button(
                            onClick = {
                                if (qrLinkInput.isNotEmpty()) {
                                    parsePairUrlAndConnect(qrLinkInput, context)
                                } else {
                                    Toast.makeText(context, "Please enter or paste pair URL", Toast.LENGTH_SHORT).show()
                                }
                            },
                            shape = RoundedCornerShape(8.dp),
                            modifier = Modifier.weight(1f)
                        ) {
                            Text("Connect Link", fontSize = 11.sp, fontWeight = FontWeight.Bold)
                        }
                    }

                    Spacer(Modifier.height(14.dp))
                    HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.5f))
                    Spacer(Modifier.height(14.dp))

                    // D. Direct IP Manual Connect
                    Text("4. Direct IP Manual Connect (Wi-Fi / Reverse ADB):", fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
                    Spacer(Modifier.height(6.dp))
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        OutlinedTextField(
                            value = manualIpInput,
                            onValueChange = { manualIpInput = it },
                            placeholder = { Text("192.168.x.x or 127.0.0.1", fontSize = 12.sp) },
                            singleLine = true,
                            modifier = Modifier.weight(2f)
                        )
                        OutlinedTextField(
                            value = manualPortInput,
                            onValueChange = { manualPortInput = it },
                            placeholder = { Text("42424", fontSize = 12.sp) },
                            singleLine = true,
                            modifier = Modifier.weight(1f)
                        )
                    }
                    Spacer(Modifier.height(6.dp))
                    Button(
                        onClick = {
                            val port = manualPortInput.toIntOrNull() ?: 42424
                            if (manualIpInput.isNotEmpty()) {
                                AppState.connectToDirectIp(manualIpInput.trim(), port)
                                Toast.makeText(context, "Connecting directly to ${manualIpInput.trim()}:$port...", Toast.LENGTH_SHORT).show()
                            } else {
                                Toast.makeText(context, "Please enter valid IP address", Toast.LENGTH_SHORT).show()
                            }
                        },
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(8.dp)
                    ) {
                        Text("⚡ Direct Connect IP", fontSize = 12.sp, fontWeight = FontWeight.Bold)
                    }
                }
            }
        }

        // 3. Discovered Nearby PCs (UDP Radar Discovery)
        item {
            Card(
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
                shape = RoundedCornerShape(16.dp)
            ) {
                Column(Modifier.padding(16.dp)) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text("📡 Discovered Nearby PCs (Radar)", fontWeight = FontWeight.Bold, fontSize = 14.sp)
                        Surface(
                            color = MaterialTheme.colorScheme.primaryContainer,
                            shape = RoundedCornerShape(12.dp)
                        ) {
                            Text(
                                text = "UDP 42425",
                                color = MaterialTheme.colorScheme.onPrimaryContainer,
                                fontSize = 10.sp,
                                fontWeight = FontWeight.Bold,
                                modifier = Modifier.padding(horizontal = 8.dp, vertical = 2.dp)
                            )
                        }
                    }

                    Spacer(Modifier.height(8.dp))

                    if (AppState.detectedNearbyPcs.isEmpty()) {
                        Box(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(16.dp),
                            contentAlignment = Alignment.Center
                        ) {
                            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                                Text("📡", fontSize = 28.sp)
                                Spacer(Modifier.height(6.dp))
                                Text(
                                    "Scanning local network for ConnectToPhone desktops...",
                                    fontSize = 12.sp,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                    textAlign = TextAlign.Center
                                )
                                Spacer(Modifier.height(4.dp))
                                Text(
                                    "Ensure your PC and phone are on the same Wi-Fi network or connected via USB.",
                                    fontSize = 10.sp,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.7f),
                                    textAlign = TextAlign.Center
                                )
                                Spacer(Modifier.height(10.dp))
                                OutlinedButton(
                                    onClick = {
                                        AppState.reconnectPc()
                                        Toast.makeText(context, "Rescanning subnet...", Toast.LENGTH_SHORT).show()
                                    },
                                    shape = RoundedCornerShape(8.dp)
                                ) {
                                    Text("🔄 Rescan Radar", fontSize = 11.sp)
                                }
                            }
                        }
                    } else {
                        AppState.detectedNearbyPcs.forEach { pc ->
                            Card(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(vertical = 4.dp),
                                shape = RoundedCornerShape(10.dp),
                                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface)
                            ) {
                                Row(
                                    modifier = Modifier.padding(12.dp),
                                    verticalAlignment = Alignment.CenterVertically
                                ) {
                                    Text("💻", fontSize = 24.sp)
                                    Spacer(Modifier.width(10.dp))
                                    Column(Modifier.weight(1f)) {
                                        Text(pc.name, fontWeight = FontWeight.Bold, fontSize = 13.sp)
                                        Text("${pc.ip}:${pc.port} • ${pc.transport}", fontSize = 11.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                                    }
                                    Button(
                                        onClick = {
                                            AppState.connectToDirectIp(pc.ip, pc.port)
                                            Toast.makeText(context, "Connecting to ${pc.name}...", Toast.LENGTH_SHORT).show()
                                        },
                                        shape = RoundedCornerShape(8.dp),
                                        contentPadding = PaddingValues(horizontal = 12.dp, vertical = 4.dp)
                                    ) {
                                        Text("Connect ⚡", fontSize = 11.sp, fontWeight = FontWeight.Bold)
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // 4. Bluetooth Devices & Pairing Card
        item {
            Card(
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
                shape = RoundedCornerShape(16.dp)
            ) {
                Column(Modifier.padding(16.dp)) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text("📶 Bluetooth Devices & SPP Serial", fontWeight = FontWeight.Bold, fontSize = 14.sp)
                        Surface(
                            color = if (AppState.isBluetoothConnected) Color(0xFF10B981) else if (AppState.isBluetoothEnabled) Color(0xFF3B82F6) else Color(0xFF6B7280),
                            shape = RoundedCornerShape(12.dp)
                        ) {
                            Text(
                                text = if (AppState.isBluetoothConnected) "Connected" else if (AppState.isBluetoothEnabled) "Enabled" else "Disabled",
                                color = Color.White,
                                fontSize = 11.sp,
                                fontWeight = FontWeight.Bold,
                                modifier = Modifier.padding(horizontal = 8.dp, vertical = 2.dp)
                            )
                        }
                    }

                    Spacer(Modifier.height(8.dp))

                    if (AppState.pairedBluetoothDevices.isNotEmpty()) {
                        Text("Paired Bluetooth PCs:", fontSize = 11.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                        Spacer(Modifier.height(4.dp))
                        AppState.pairedBluetoothDevices.forEach { devString ->
                            Row(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(vertical = 3.dp),
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Text(
                                    devString,
                                    fontSize = 12.sp,
                                    fontWeight = FontWeight.Medium,
                                    modifier = Modifier.weight(1f)
                                )
                                Button(
                                    onClick = { AppState.triggerBluetoothConnectCallback?.invoke(devString) },
                                    contentPadding = PaddingValues(horizontal = 10.dp, vertical = 4.dp),
                                    shape = RoundedCornerShape(8.dp),
                                    colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF2563EB))
                                ) {
                                    Text("Connect", fontSize = 11.sp)
                                }
                            }
                        }
                    } else {
                        Text(
                            if (AppState.isBluetoothEnabled) "No paired PC detected. Tap 'Pair New PC' below to open Android Bluetooth settings." else "Bluetooth is turned off on this device. Please turn on Bluetooth.",
                            fontSize = 11.sp,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }

                    Spacer(Modifier.height(10.dp))

                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        OutlinedButton(
                            onClick = { AppState.triggerBluetoothConnectCallback?.invoke(null) },
                            modifier = Modifier.weight(1f),
                            shape = RoundedCornerShape(8.dp)
                        ) {
                            Text("🔄 Connect Paired", fontSize = 11.sp)
                        }
                        Button(
                            onClick = { AppState.triggerBluetoothSettingsCallback?.invoke() },
                            modifier = Modifier.weight(1f),
                            shape = RoundedCornerShape(8.dp),
                            colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.secondary)
                        ) {
                            Text("⚙ Pair New PC", fontSize = 11.sp)
                        }
                    }
                }
            }
        }

        // 5. Hardware Protocol Toggles
        item {
            Card(
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
                shape = RoundedCornerShape(16.dp)
            ) {
                Column(Modifier.padding(16.dp)) {
                    Text("⚙️ Hardware Transports & Protocol Toggles", fontWeight = FontWeight.Bold, fontSize = 14.sp)
                    Spacer(Modifier.height(4.dp))
                    Text(
                        "Enable or disable individual physical channels to force specific routes or test failover:",
                        fontSize = 11.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    Spacer(Modifier.height(10.dp))

                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        Surface(
                            color = if (AppState.isUsbProtocolEnabled) Color(0xFF1E3A8A) else Color(0xFF27272A),
                            shape = RoundedCornerShape(10.dp),
                            modifier = Modifier.weight(1f).clickable { AppState.toggleUsbProtocol() }
                        ) {
                            Column(Modifier.padding(10.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                                Text("⚡ USB", fontSize = 13.sp, fontWeight = FontWeight.Bold, color = if (AppState.isUsbProtocolEnabled) Color(0xFF93C5FD) else Color(0xFF9CA3AF))
                                Spacer(Modifier.height(2.dp))
                                Text(if (AppState.isUsbProtocolEnabled) "Enabled" else "Disabled", fontSize = 10.sp, color = if (AppState.isUsbProtocolEnabled) Color(0xFF60A5FA) else Color(0xFF6B7280))
                            }
                        }

                        Surface(
                            color = if (AppState.isWifiProtocolEnabled) Color(0xFF064E3B) else Color(0xFF27272A),
                            shape = RoundedCornerShape(10.dp),
                            modifier = Modifier.weight(1f).clickable { AppState.toggleWifiProtocol() }
                        ) {
                            Column(Modifier.padding(10.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                                Text("📶 Wi-Fi", fontSize = 13.sp, fontWeight = FontWeight.Bold, color = if (AppState.isWifiProtocolEnabled) Color(0xFF6EE7B7) else Color(0xFF9CA3AF))
                                Spacer(Modifier.height(2.dp))
                                Text(if (AppState.isWifiProtocolEnabled) "Enabled" else "Disabled", fontSize = 10.sp, color = if (AppState.isWifiProtocolEnabled) Color(0xFF34D399) else Color(0xFF6B7280))
                            }
                        }

                        Surface(
                            color = if (AppState.isBluetoothProtocolEnabled) Color(0xFF312E81) else Color(0xFF27272A),
                            shape = RoundedCornerShape(10.dp),
                            modifier = Modifier.weight(1f).clickable { AppState.toggleBluetoothProtocol() }
                        ) {
                            Column(Modifier.padding(10.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                                Text("📱 BT", fontSize = 13.sp, fontWeight = FontWeight.Bold, color = if (AppState.isBluetoothProtocolEnabled) Color(0xFFC7D2FE) else Color(0xFF9CA3AF))
                                Spacer(Modifier.height(2.dp))
                                Text(if (AppState.isBluetoothProtocolEnabled) "Enabled" else "Disabled", fontSize = 10.sp, color = if (AppState.isBluetoothProtocolEnabled) Color(0xFF818CF8) else Color(0xFF6B7280))
                            }
                        }
                    }
                }
            }
        }

        // 6. Failover Simulation Lab
        item {
            Card(
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
                shape = RoundedCornerShape(16.dp)
            ) {
                Column(Modifier.padding(16.dp)) {
                    Text("🧪 Multi-Path Failover Simulation", fontWeight = FontWeight.Bold, fontSize = 14.sp)
                    Spacer(Modifier.height(4.dp))
                    Text(
                        "Test multi-channel resilience. Tapping below simulates an instant Wi-Fi/cable disconnect. The active session will automatically failover to the next available route without file corruption.",
                        fontSize = 11.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    Spacer(Modifier.height(10.dp))
                    OutlinedButton(
                        onClick = {
                            Toast.makeText(context, "Testing failover: Simulating Wi-Fi drop -> fallback to USB/BT...", Toast.LENGTH_LONG).show()
                            AppState.disconnectPc()
                            coroutineScope.launch {
                                delay(1500)
                                AppState.reconnectPc()
                            }
                        },
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(10.dp)
                    ) {
                        Text("🧪 Simulate Channel Drop (Test Failover)", fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
                    }
                }
            }
        }
    }
}

@Composable
fun PcExplorerTab(
    context: Context,
    currentPath: String,
    entries: List<RemoteFileItem>,
    onNavigate: (String) -> Unit,
    onPull: (String, Boolean) -> Unit
) {
    Column {
        if (!AppState.isConnected) {
            Card(
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.errorContainer),
                shape = RoundedCornerShape(12.dp),
                modifier = Modifier.fillMaxWidth().padding(bottom = 12.dp)
            ) {
                Column(Modifier.padding(14.dp)) {
                    Text("PC Not Connected Yet", fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onErrorContainer, fontSize = 14.sp)
                    Spacer(Modifier.height(2.dp))
                    Text("Connect via USB cable or Wi-Fi to browse PC drives.", fontSize = 11.sp, color = MaterialTheme.colorScheme.onErrorContainer)
                    Spacer(Modifier.height(8.dp))
                    Button(
                        onClick = {
                            AppState.connectOrRefreshPc()
                            Toast.makeText(context, "Connecting to PC...", Toast.LENGTH_SHORT).show()
                        },
                        colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error)
                    ) {
                        Text("⚡ Connect to PC")
                    }
                }
            }
        }

        Surface(
            color = MaterialTheme.colorScheme.surfaceVariant,
            shape = RoundedCornerShape(8.dp),
            modifier = Modifier.fillMaxWidth()
        ) {
            Row(
                modifier = Modifier.padding(12.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                if (currentPath != "/" && currentPath.isNotEmpty()) {
                    TextButton(onClick = {
                        if (currentPath.length <= 3 && currentPath.contains(":")) {
                            onNavigate("/")
                        } else {
                            val parent = if (currentPath.contains("\\")) {
                                val p = currentPath.substringBeforeLast("\\")
                                if (p.length == 2 && p.endsWith(":")) "$p\\" else p.ifEmpty { "/" }
                            } else {
                                "/"
                            }
                            onNavigate(parent)
                        }
                    }) {
                        Text("⬅ Up")
                    }
                    TextButton(onClick = { onNavigate("/") }) {
                        Text("🏠 Root")
                    }
                } else {
                    Text("📁", fontSize = 16.sp)
                }
                Spacer(Modifier.width(4.dp))
                Text(currentPath, fontSize = 12.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, modifier = Modifier.weight(1f))
                TextButton(onClick = { onNavigate(currentPath) }) {
                    Text("🔄")
                }
            }
        }

        Spacer(Modifier.height(12.dp))
        Text("Real PC Drives & Folders (👁 View without storing, ⬇ Download):", fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Spacer(Modifier.height(8.dp))

        if (AppState.isQueryingPcFiles) {
            Box(Modifier.fillMaxWidth().padding(32.dp), contentAlignment = Alignment.Center) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    CircularProgressIndicator(modifier = Modifier.size(32.dp))
                    Spacer(Modifier.height(8.dp))
                    Text("Reading PC directory over high-speed link...", fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        } else if (entries.isEmpty()) {
            Box(Modifier.fillMaxWidth().padding(32.dp), contentAlignment = Alignment.Center) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text("No files in this folder or PC standing by.", fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Spacer(Modifier.height(8.dp))
                    OutlinedButton(onClick = { onNavigate("/") }) {
                        Text("Go to PC Root Drives (C:\\, D:\\)")
                    }
                }
            }
        } else {
            LazyColumn(
                modifier = Modifier.fillMaxWidth().weight(1f),
                verticalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                items(entries) { item ->
                    Card(
                        modifier = Modifier
                            .fillMaxWidth()
                            .clickable {
                                if (item.isDirectory) {
                                    onNavigate(item.path)
                                } else {
                                    onPull(item.path, true) // Tap previews directly
                                }
                            },
                        shape = RoundedCornerShape(8.dp)
                    ) {
                        Row(
                            modifier = Modifier.padding(10.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Text(if (item.isDirectory) "📁" else "📄", fontSize = 20.sp)
                            Spacer(Modifier.width(10.dp))
                            Column(Modifier.weight(1f)) {
                                Text(item.name, fontWeight = FontWeight.SemiBold, fontSize = 13.sp, maxLines = 1)
                                Text(item.sizeDisplay, fontSize = 11.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                            if (!item.isDirectory) {
                                FilledTonalButton(
                                    onClick = { onPull(item.path, true) },
                                    contentPadding = PaddingValues(horizontal = 8.dp, vertical = 4.dp),
                                    modifier = Modifier.padding(end = 4.dp)
                                ) {
                                    Text("👁 View", fontSize = 11.sp, fontWeight = FontWeight.Bold)
                                }
                                Button(
                                    onClick = { onPull(item.path, false) },
                                    contentPadding = PaddingValues(horizontal = 8.dp, vertical = 4.dp)
                                ) {
                                    Text("⬇ Save", fontSize = 11.sp, fontWeight = FontWeight.Bold)
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
fun ClipboardTab(
    context: Context,
    autoSync: Boolean,
    onAutoSyncChange: (Boolean) -> Unit,
    history: List<String>
) {
    Column(modifier = Modifier.fillMaxSize()) {
        Card(
            colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
            shape = RoundedCornerShape(12.dp)
        ) {
            Row(
                modifier = Modifier.padding(16.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Column(Modifier.weight(1f)) {
                    Text("Auto Clipboard Sync", fontWeight = FontWeight.Bold, fontSize = 15.sp)
                    Text("Copies on PC appear on Phone & vice versa", fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                Switch(checked = autoSync, onCheckedChange = onAutoSyncChange)
            }
        }

        Spacer(Modifier.height(16.dp))
        Text("Recent Synced Clips", fontWeight = FontWeight.Bold, fontSize = 14.sp)
        Spacer(Modifier.height(8.dp))

        if (history.isEmpty()) {
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(32.dp),
                contentAlignment = Alignment.Center
            ) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text("📋", fontSize = 32.sp)
                    Spacer(Modifier.height(8.dp))
                    Text(
                        "No clips synced yet",
                        fontWeight = FontWeight.SemiBold,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    Spacer(Modifier.height(4.dp))
                    Text(
                        "Copy text on PC or Phone to sync automatically",
                        fontSize = 12.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.7f)
                    )
                }
            }
        } else {
            LazyColumn(
                modifier = Modifier.fillMaxWidth().weight(1f),
                verticalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                items(history) { clip ->
                    Card(
                        modifier = Modifier.fillMaxWidth().clickable {
                            try {
                                val cm = context.getSystemService(Context.CLIPBOARD_SERVICE) as? android.content.ClipboardManager
                                cm?.setPrimaryClip(android.content.ClipData.newPlainText("text", clip))
                            } catch (_: Exception) {}
                            Toast.makeText(context, "Copied to Phone Clipboard!", Toast.LENGTH_SHORT).show()
                        }
                    ) {
                        Row(
                            modifier = Modifier.padding(12.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Text("📋", fontSize = 16.sp)
                            Spacer(Modifier.width(12.dp))
                            Text(clip, fontSize = 13.sp, maxLines = 2, modifier = Modifier.weight(1f))
                        }
                    }
                }
            }
        }
    }
}

@Composable
fun RemoteControlTab(context: Context) {
    var textToSend by remember { mutableStateOf("") }
    var showExtraHotkeys by remember { mutableStateOf(false) }

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(6.dp)
    ) {
        // TOP SECTION: COMPACT KEYBOARD, SHORTCUTS, MEDIA & POWER (Fixed at top, fits on screen)
        Card(
            colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
            shape = RoundedCornerShape(10.dp),
            modifier = Modifier.fillMaxWidth()
        ) {
            Column(Modifier.padding(horizontal = 8.dp, vertical = 6.dp)) {
                // Row 1: Text input to PC + Send button + Toggle Hotkeys
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    OutlinedTextField(
                        value = textToSend,
                        onValueChange = { textToSend = it },
                        placeholder = { Text("Type to PC...", fontSize = 12.sp) },
                        modifier = Modifier.weight(1f).height(46.dp),
                        singleLine = true
                    )
                    Spacer(Modifier.width(6.dp))
                    Button(
                        onClick = {
                            if (textToSend.isNotEmpty()) {
                                AppState.sendKeyboardKey(text = textToSend)
                                textToSend = ""
                                Toast.makeText(context, "Sent to PC!", Toast.LENGTH_SHORT).show()
                            }
                        },
                        contentPadding = PaddingValues(horizontal = 10.dp, vertical = 2.dp),
                        modifier = Modifier.height(42.dp)
                    ) {
                        Text("Send", fontSize = 12.sp, fontWeight = FontWeight.Bold)
                    }
                    IconButton(
                        onClick = { showExtraHotkeys = !showExtraHotkeys },
                        modifier = Modifier.size(36.dp)
                    ) {
                        Text(if (showExtraHotkeys) "▲" else "⌨️", fontSize = 16.sp)
                    }
                }

                // Row 2: Collapsible Quick Hotkeys
                if (showExtraHotkeys) {
                    Spacer(Modifier.height(4.dp))
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .horizontalScroll(rememberScrollState()),
                        horizontalArrangement = Arrangement.spacedBy(6.dp)
                    ) {
                        listOf(
                            "Alt+Tab" to "ALT_TAB",
                            "Win+D" to "WIN_D",
                            "F5" to "F5",
                            "Ctrl+C" to "COPY",
                            "Ctrl+V" to "PASTE",
                            "Ctrl+Z" to "UNDO",
                            "Enter" to "ENTER",
                            "⌫ Back" to "BACKSPACE",
                            "Esc" to "ESC",
                            "Space" to "SPACE"
                        ).forEach { (label, key) ->
                            FilledTonalButton(
                                onClick = { AppState.sendKeyboardKey(specialKey = key) },
                                contentPadding = PaddingValues(horizontal = 8.dp, vertical = 2.dp),
                                modifier = Modifier.height(30.dp)
                            ) {
                                Text(label, fontSize = 10.sp)
                            }
                        }
                    }
                }

                Spacer(Modifier.height(4.dp))

                // Row 3: Media & Quick Power Actions in ONE sleek row
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Row(horizontalArrangement = Arrangement.spacedBy(2.dp)) {
                        IconButton(onClick = { AppState.sendMediaAction(2) }, modifier = Modifier.size(32.dp)) {
                            Text("⏮", fontSize = 15.sp)
                        }
                        IconButton(onClick = { AppState.sendMediaAction(0) }, modifier = Modifier.size(32.dp)) {
                            Text("⏯", fontSize = 17.sp)
                        }
                        IconButton(onClick = { AppState.sendMediaAction(1) }, modifier = Modifier.size(32.dp)) {
                            Text("⏭", fontSize = 15.sp)
                        }
                        IconButton(onClick = { AppState.sendMediaAction(4) }, modifier = Modifier.size(32.dp)) {
                            Text("🔉", fontSize = 15.sp)
                        }
                        IconButton(onClick = { AppState.sendMediaAction(3) }, modifier = Modifier.size(32.dp)) {
                            Text("🔊", fontSize = 15.sp)
                        }
                        IconButton(onClick = { AppState.sendMediaAction(5) }, modifier = Modifier.size(32.dp)) {
                            Text("🔇", fontSize = 15.sp)
                        }
                    }

                    Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                        FilledTonalButton(
                            onClick = {
                                AppState.sendPowerAction(0)
                                Toast.makeText(context, "Locking PC...", Toast.LENGTH_SHORT).show()
                            },
                            contentPadding = PaddingValues(horizontal = 8.dp, vertical = 2.dp),
                            modifier = Modifier.height(30.dp)
                        ) {
                            Text("🔒 Lock", fontSize = 10.sp, fontWeight = FontWeight.Bold)
                        }
                        OutlinedButton(
                            onClick = {
                                AppState.sendPowerAction(1)
                                Toast.makeText(context, "Sleep...", Toast.LENGTH_SHORT).show()
                            },
                            contentPadding = PaddingValues(horizontal = 8.dp, vertical = 2.dp),
                            modifier = Modifier.height(30.dp)
                        ) {
                            Text("💤", fontSize = 10.sp)
                        }
                    }
                }
            }
        }

        // BOTTOM THUMB ZONE: FULL-WIDTH TRACKPAD (weight 1f, no outer scroll!), CLICKS & HORIZONTAL WHEEL
        Card(
            colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
            shape = RoundedCornerShape(12.dp),
            modifier = Modifier.fillMaxWidth().weight(1f)
        ) {
            Column(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(6.dp)
            ) {
                // Header
                Row(
                    modifier = Modifier.fillMaxWidth().padding(horizontal = 4.dp, vertical = 2.dp),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text("Thumb-Zone Touchpad", fontWeight = FontWeight.Bold, fontSize = 13.sp)
                    Text("Tap=Left • Long=Right", fontSize = 11.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }

                Spacer(Modifier.height(4.dp))

                // Full-width Touchpad + Dedicated Vertical Scroll Strip
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .weight(1f)
                ) {
                    // Precision Trackpad Area
                    Box(
                        modifier = Modifier
                            .weight(1f)
                            .fillMaxHeight()
                            .background(Color(0xFF1E1E24), RoundedCornerShape(10.dp))
                            .pointerInput(Unit) {
                                detectTapGestures(
                                    onTap = { AppState.sendMouseDelta(0, 0, leftClick = true) },
                                    onDoubleTap = {
                                        AppState.sendMouseDelta(0, 0, leftClick = true)
                                        AppState.sendMouseDelta(0, 0, leftClick = true)
                                    },
                                    onLongPress = { AppState.sendMouseDelta(0, 0, rightClick = true) }
                                )
                            }
                            .pointerInput(Unit) {
                                detectDragGestures { change, dragAmount ->
                                    change.consume()
                                    AppState.sendMouseDelta(
                                        (dragAmount.x * 1.6f).toInt(),
                                        (dragAmount.y * 1.6f).toInt()
                                    )
                                }
                            },
                        contentAlignment = Alignment.Center
                    ) {
                        Column(horizontalAlignment = Alignment.CenterHorizontally) {
                            Text("👆", fontSize = 28.sp)
                            Spacer(Modifier.height(4.dp))
                            Text(
                                "Precision Thumb Trackpad\nGlide cursor smoothly",
                                color = Color(0xFFA1A1AA),
                                fontSize = 11.sp,
                                textAlign = TextAlign.Center
                            )
                        }
                    }

                    Spacer(Modifier.width(6.dp))

                    // Dedicated Vertical Scroll Strip (No outer scroll competition!)
                    Box(
                        modifier = Modifier
                            .width(42.dp)
                            .fillMaxHeight()
                            .background(Color(0xFF27272A), RoundedCornerShape(10.dp))
                            .pointerInput(Unit) {
                                detectDragGestures { change, dragAmount ->
                                    change.consume()
                                    val scroll = (-dragAmount.y * 14).toInt()
                                    if (scroll != 0) {
                                        AppState.sendMouseDelta(0, 0, wheelDelta = scroll)
                                    }
                                }
                            },
                        contentAlignment = Alignment.Center
                    ) {
                        Column(horizontalAlignment = Alignment.CenterHorizontally) {
                            Text("▲", fontSize = 11.sp, color = Color(0xFFA1A1AA))
                            Spacer(Modifier.height(4.dp))
                            Text("↕", fontSize = 16.sp, color = Color(0xFF60A5FA), fontWeight = FontWeight.Bold)
                            Spacer(Modifier.height(4.dp))
                            Text("▼", fontSize = 11.sp, color = Color(0xFFA1A1AA))
                        }
                    }
                }

                Spacer(Modifier.height(6.dp))

                // Left, Middle & Right Click buttons BELOW trackpad, ABOVE horizontal scroll
                Row(
                    modifier = Modifier.fillMaxWidth().height(46.dp),
                    horizontalArrangement = Arrangement.spacedBy(6.dp)
                ) {
                    Button(
                        onClick = { AppState.sendMouseDelta(0, 0, leftClick = true) },
                        modifier = Modifier.weight(1.1f).fillMaxHeight(),
                        shape = RoundedCornerShape(8.dp)
                    ) {
                        Text("Left Click", fontWeight = FontWeight.Bold, fontSize = 12.sp)
                    }
                    FilledTonalButton(
                        onClick = { AppState.sendMouseDelta(0, 0, middleClick = true) },
                        modifier = Modifier.weight(1f).fillMaxHeight(),
                        shape = RoundedCornerShape(8.dp)
                    ) {
                        Text("🔘 Middle", fontWeight = FontWeight.Bold, fontSize = 12.sp)
                    }
                    Button(
                        onClick = { AppState.sendMouseDelta(0, 0, rightClick = true) },
                        modifier = Modifier.weight(1.1f).fillMaxHeight(),
                        shape = RoundedCornerShape(8.dp),
                        colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.secondary)
                    ) {
                        Text("Right Click", fontWeight = FontWeight.Bold, fontSize = 12.sp)
                    }
                }

                Spacer(Modifier.height(6.dp))

                // Horizontal Scroll Wheel Strip at the very bottom
                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(36.dp)
                        .background(Color(0xFF27272A), RoundedCornerShape(8.dp))
                        .pointerInput(Unit) {
                            detectDragGestures { change, dragAmount ->
                                change.consume()
                                val scroll = (dragAmount.x * 14).toInt()
                                if (scroll != 0) {
                                    AppState.sendMouseDelta(0, 0, wheelDeltaX = scroll)
                                }
                            }
                        },
                    contentAlignment = Alignment.Center
                ) {
                    Text("◀  Horizontal Scroll Wheel  ▶", fontSize = 11.sp, color = Color(0xFF93C5FD), fontWeight = FontWeight.SemiBold)
                }
            }
        }
    }
}

@Composable
fun StorageTab(context: Context) {
    var storageInfo by remember { mutableStateOf(CacheManager.getStorageInfo(context)) }
    var selectedFilter by remember { mutableStateOf("All") }
    var cachedFiles by remember { mutableStateOf(CacheManager.getCachedFiles(context, selectedFilter)) }

    fun refresh() {
        storageInfo = CacheManager.getStorageInfo(context)
        cachedFiles = CacheManager.getCachedFiles(context, selectedFilter)
    }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        item {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .clickable { AppState.selectedNavigationTab = 0 }
                    .padding(vertical = 4.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text("← Back to Transfers", fontSize = 13.sp, fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.primary)
            }
        }

        // 1. Storage Overview Card
        item {
            Card(
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
                shape = RoundedCornerShape(12.dp)
            ) {
                Column(Modifier.padding(12.dp)) {
                    Text("Storage & Cache Manager", fontWeight = FontWeight.Bold, fontSize = 16.sp)
                    Spacer(Modifier.height(4.dp))
                    Text(
                        "Preview files are kept in fast cache and auto-evicted by LRU quota. Permanent files are saved in Downloads.",
                        fontSize = 12.sp,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    Spacer(Modifier.height(12.dp))

                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween
                    ) {
                        Surface(
                            color = MaterialTheme.colorScheme.secondaryContainer,
                            shape = RoundedCornerShape(8.dp)
                        ) {
                            Column(Modifier.padding(horizontal = 12.dp, vertical = 8.dp)) {
                                Text("INTERNAL STORAGE", fontSize = 10.sp, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onSecondaryContainer.copy(alpha = 0.7f))
                                Text("${storageInfo.freeDisplay} Free / ${storageInfo.totalDisplay}", fontSize = 13.sp, fontWeight = FontWeight.Bold)
                            }
                        }

                        Surface(
                            color = MaterialTheme.colorScheme.tertiaryContainer,
                            shape = RoundedCornerShape(8.dp)
                        ) {
                            Column(Modifier.padding(horizontal = 12.dp, vertical = 8.dp)) {
                                Text("PREVIEW CACHE", fontSize = 10.sp, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onTertiaryContainer.copy(alpha = 0.7f))
                                Text("${storageInfo.cacheUsedDisplay} / ${storageInfo.quotaDisplay}", fontSize = 13.sp, fontWeight = FontWeight.Bold)
                            }
                        }
                    }

                    Spacer(Modifier.height(12.dp))
                    val fraction = (storageInfo.cacheUsedBytes.toFloat() / storageInfo.cacheQuotaBytes.toFloat()).coerceIn(0f, 1f)
                    LinearProgressIndicator(
                        progress = { fraction },
                        modifier = Modifier.fillMaxWidth().height(6.dp),
                    )

                    Spacer(Modifier.height(12.dp))
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        OutlinedButton(
                            onClick = {
                                CacheManager.enforceQuotaAndPrune(context)
                                refresh()
                                Toast.makeText(context, "LRU quota checked and pruned!", Toast.LENGTH_SHORT).show()
                            },
                            modifier = Modifier.weight(1f)
                        ) {
                            Text("Auto-Prune LRU", fontSize = 11.sp)
                        }

                        Button(
                            onClick = {
                                CacheManager.clearAllCache(context)
                                CacheManager.playNotificationSound(context)
                                refresh()
                                Toast.makeText(context, "Cache memory cleared completely!", Toast.LENGTH_SHORT).show()
                            },
                            colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.error),
                            modifier = Modifier.weight(1f)
                        ) {
                            Text("🧹 Clear All Cache", fontSize = 11.sp, fontWeight = FontWeight.Bold)
                        }
                    }
                }
            }
        }

        // 2. Category Filters
        item {
            Column {
                Text("Filter Cached Files:", fontWeight = FontWeight.Bold, fontSize = 13.sp)
                Spacer(Modifier.height(8.dp))
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(6.dp)
                ) {
                    listOf("All", "Images", "Documents", "Media", "Other").forEach { cat ->
                        val isSelected = selectedFilter == cat
                        Surface(
                            color = if (isSelected) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.surfaceVariant,
                            shape = RoundedCornerShape(16.dp),
                            modifier = Modifier.clickable {
                                selectedFilter = cat
                                cachedFiles = CacheManager.getCachedFiles(context, cat)
                            }
                        ) {
                            Text(
                                text = cat,
                                color = if (isSelected) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.onSurfaceVariant,
                                fontSize = 12.sp,
                                fontWeight = if (isSelected) FontWeight.Bold else FontWeight.Normal,
                                modifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp)
                            )
                        }
                    }
                }
            }
        }

        // 3. Cached Preview Items List
        if (cachedFiles.isEmpty()) {
            item {
                Box(
                    modifier = Modifier.fillMaxWidth().padding(32.dp),
                    contentAlignment = Alignment.Center
                ) {
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        Text("🗑️", fontSize = 32.sp)
                        Spacer(Modifier.height(8.dp))
                        Text("No cached preview files found.", color = MaterialTheme.colorScheme.onSurfaceVariant, fontSize = 13.sp)
                        Text("Use '👁 View' in PC Files to quickly view files without permanent download.", fontSize = 11.sp, color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.7f), textAlign = TextAlign.Center)
                    }
                }
            }
        } else {
            items(cachedFiles) { item ->
                Card(
                    modifier = Modifier
                        .fillMaxWidth()
                        .clickable { CacheManager.openQuickView(context, item.path) },
                    shape = RoundedCornerShape(8.dp)
                ) {
                    Row(
                        modifier = Modifier.padding(12.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        val icon = when (item.category) {
                            "Image" -> "🖼️"
                            "Document" -> "📄"
                            "Video" -> "🎬"
                            "Audio" -> "🎵"
                            else -> "📦"
                        }
                        Text(icon, fontSize = 20.sp)
                        Spacer(Modifier.width(10.dp))
                        Column(Modifier.weight(1f)) {
                            Text(item.name, fontWeight = FontWeight.SemiBold, fontSize = 13.sp, maxLines = 1)
                            Text("${item.sizeDisplay} • ${item.category}", fontSize = 11.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                        IconButton(
                            onClick = {
                                CacheManager.deleteCachedFile(item.path)
                                CacheManager.playNotificationSound(context)
                                refresh()
                            }
                        ) {
                            Text("🗑️", fontSize = 16.sp)
                        }
                    }
                }
            }
        }

        // 4. Directory Paths Info Card
        item {
            Card(
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
                shape = RoundedCornerShape(8.dp)
            ) {
                Column(Modifier.padding(12.dp)) {
                    Text("Storage Locations on Phone:", fontWeight = FontWeight.Bold, fontSize = 12.sp)
                    Spacer(Modifier.height(4.dp))
                    Text("• Preview Cache: ${CacheManager.getCacheDir(context).name}/ (temporary, LRU auto-clean)", fontSize = 11.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Text("• Downloads: ${CacheManager.getDownloadsDir(context).name}/ (permanent storage)", fontSize = 11.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }
    }
}
