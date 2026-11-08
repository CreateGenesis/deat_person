package com.example.virtual_agent

import android.os.Handler
import android.os.Looper
import com.unity3d.player.UnityPlayer
import io.flutter.plugin.common.EventChannel
import org.json.JSONObject
import java.lang.ref.WeakReference

object UnityHostBridge {
    private val handler = Handler(Looper.getMainLooper())
    private var sink: EventChannel.EventSink? = null
    private var activity = WeakReference<UnityHostActivity>(null)
    private val retained = ArrayDeque<String>()
    private var pendingOpen: JSONObject? = null
    private var ready = false
    private var closing = false
    private var closeGeneration = 0
    @Volatile private var mode = ""

    fun listen(eventSink: EventChannel.EventSink?) {
        sink = eventSink
        if (eventSink != null) {
            retained.forEach { eventSink.success(it) }
            retained.clear()
        }
    }

    fun prepareOpen(request: JSONObject) {
        mode = request.getString("mode")
        pendingOpen = request
        closing = false
        closeGeneration++
    }

    fun attach(host: UnityHostActivity) {
        activity = WeakReference(host)
    }

    fun deliverOpen() {
        val request = pendingOpen
        if (ready && request != null) {
            pendingOpen = null
            send(request)
        }
    }

    @JvmStatic
    fun getLaunchMode(): String = mode

    @JvmStatic
    fun emitEvent(json: String) {
        handler.post {
            try {
                val event = JSONObject(json)
                if (event.optInt("version") != 1) return@post
                publish(json)
                when (event.optString("type")) {
                    "Ready" -> {
                        ready = true
                        activity.get()?.markUnityReady()
                        if (closing) send(JSONObject().put("version", 1).put("type", "CloseAR"))
                        else deliverOpen()
                    }
                    "Closed" -> {
                        closing = true
                        activity.get()?.returnToFlutter()
                    }
                }
            } catch (_: Exception) {
                publish(JSONObject().put("version", 1).put("type", "Error")
                    .put("message", "Invalid Unity event payload.").toString())
            }
        }
    }

    private fun publish(json: String) {
        val currentSink = sink
        if (currentSink != null) currentSink.success(json)
        else {
            if (retained.size >= 30) retained.removeFirst()
            retained.addLast(json)
        }
    }

    private fun send(request: JSONObject) {
        UnityPlayer.UnitySendMessage("FlutterUnityBridge", "ReceiveCommand", request.toString())
    }

    fun requestClose() {
        if (closing) {
            activity.get()?.returnToFlutter()
            return
        }
        closing = true
        pendingOpen = null
        val generation = ++closeGeneration
        if (ready) send(JSONObject().put("version", 1).put("type", "CloseAR"))
        handler.postDelayed({
            if (closing && generation == closeGeneration && activity.get()?.hasReturned == false) {
                publish(JSONObject().put("version", 1).put("type", "Error")
                    .put("message", "Unity close acknowledgement timed out; Android paused the player.").toString())
                activity.get()?.returnToFlutter()
            }
        }, 2500)
    }
}
