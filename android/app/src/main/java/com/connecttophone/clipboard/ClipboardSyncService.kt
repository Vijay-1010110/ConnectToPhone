package com.connecttophone.clipboard

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import com.connecttophone.protocol.ClipboardFormat
import com.connecttophone.protocol.ClipboardPayload
import java.util.Collections
import java.util.zip.CRC32

class ClipboardSyncService(private val context: Context) {

    private val clipboardManager = context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
    private val handledHashes = Collections.synchronizedSet(mutableSetOf<Long>())

    var onLocalClipboardChanged: ((ClipboardPayload) -> Unit)? = null

    private val clipListener = ClipboardManager.OnPrimaryClipChangedListener {
        try {
            val clip = clipboardManager.primaryClip ?: return@OnPrimaryClipChangedListener
            if (clip.itemCount > 0) {
                val item = clip.getItemAt(0)
                val text = item.coerceToText(context)?.toString() ?: return@OnPrimaryClipChangedListener

                val crc = CRC32()
                crc.update(text.toByteArray(Charsets.UTF_8))
                val hash = crc.value

                if (handledHashes.contains(hash)) {
                    // Ignore echo loop
                    return@OnPrimaryClipChangedListener
                }

                handledHashes.add(hash)
                if (handledHashes.size > 100) {
                    handledHashes.clear()
                    handledHashes.add(hash)
                }

                val payload = ClipboardPayload(
                    format = ClipboardFormat.PLAIN_TEXT,
                    contentHash = hash,
                    timestamp = System.currentTimeMillis(),
                    text = text,
                    mimeType = "text/plain"
                )

                onLocalClipboardChanged?.invoke(payload)
            }
        } catch (_: Exception) {}
    }

    fun startListening() {
        clipboardManager.addPrimaryClipChangedListener(clipListener)
    }

    fun stopListening() {
        clipboardManager.removePrimaryClipChangedListener(clipListener)
    }

    fun setRemoteClipboard(payload: ClipboardPayload) {
        val text = payload.text ?: return
        handledHashes.add(payload.contentHash)

        try {
            val clip = ClipData.newPlainText("ConnectToPhone", text)
            clipboardManager.setPrimaryClip(clip)
        } catch (_: Exception) {}
    }
}
