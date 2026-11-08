package com.example.virtual_agent

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.EventChannel
import io.flutter.plugin.common.MethodChannel
import org.json.JSONObject

class MainActivity : FlutterActivity() {
    private var methods: MethodChannel? = null
    private var events: EventChannel? = null
    private var pendingOpen: JSONObject? = null
    private var pendingResult: MethodChannel.Result? = null

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        events = EventChannel(flutterEngine.dartExecutor.binaryMessenger, "data_person/unity/events")
        events?.setStreamHandler(object : EventChannel.StreamHandler {
            override fun onListen(arguments: Any?, sink: EventChannel.EventSink) {
                UnityHostBridge.listen(sink)
            }

            override fun onCancel(arguments: Any?) {
                UnityHostBridge.listen(null)
            }
        })
        methods = MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "data_person/unity/methods")
        methods?.setMethodCallHandler { call, result ->
            when (call.method) {
                "getHostInfo" -> result.success(mapOf(
                    "unityAvailable" to Build.SUPPORTED_64_BIT_ABIS.contains("arm64-v8a"),
                    "protocolVersion" to 1,
                    "applicationId" to packageName
                ))
                "openAr" -> {
                    val args = call.arguments as? Map<*, *>
                    val mode = args?.get("mode") as? String
                    if (args?.get("version") != 1 || mode !in listOf("preview", "onsite")) {
                        result.error("INVALID_ARGUMENT", "Unsupported protocol or Unity mode.", null)
                    } else if (pendingResult != null) {
                        result.error("BUSY", "A permission request is already pending.", null)
                    } else {
                        val request = JSONObject()
                            .put("version", 1).put("type", "OpenPlace")
                            .put("mode", mode).put("requestId", args?.get("requestId")?.toString() ?: "")
                            .put("backendBaseUrl", args?.get("backendBaseUrl")?.toString() ?: "")
                            .put("backendApiKey", args?.get("backendApiKey")?.toString() ?: "")
                            .put("site", args?.get("site")?.toString() ?: "Block_test_1")
                        if (!hasPermissions(mode)) {
                            pendingOpen = request
                            pendingResult = result
                            requestPermissions(requiredPermissions(mode), 4101)
                        } else {
                            openUnity(request, result)
                        }
                    }
                }
                else -> result.notImplemented()
            }
        }
    }

    private fun requiredPermissions(mode: String?): Array<String> =
        if (mode == "onsite") {
            arrayOf(
                Manifest.permission.RECORD_AUDIO,
                Manifest.permission.CAMERA,
                Manifest.permission.ACCESS_COARSE_LOCATION,
                Manifest.permission.ACCESS_FINE_LOCATION
            )
        } else {
            arrayOf(Manifest.permission.RECORD_AUDIO)
        }

    private fun hasPermissions(mode: String?): Boolean =
        requiredPermissions(mode).all {
            checkSelfPermission(it) == PackageManager.PERMISSION_GRANTED
        }

    private fun openUnity(request: JSONObject, result: MethodChannel.Result) {
        try {
            UnityHostBridge.prepareOpen(request)
            startActivity(Intent(this, UnityHostActivity::class.java)
                .addFlags(Intent.FLAG_ACTIVITY_REORDER_TO_FRONT))
            result.success(null)
        } catch (failure: Exception) {
            result.error("UNITY_OPEN_FAILED", failure.message, null)
        }
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode != 4101) return
        val request = pendingOpen
        val result = pendingResult
        pendingOpen = null
        pendingResult = null
        if (request != null && result != null) {
            if (hasPermissions(request.optString("mode"))) openUnity(request, result)
            else result.error("PERMISSION_DENIED", "现场模式需要相机与精确位置权限；离线预览不需要这些权限。", null)
        }
    }

    override fun onBackPressed() {
        moveTaskToBack(true)
    }

    override fun cleanUpFlutterEngine(flutterEngine: FlutterEngine) {
        methods?.setMethodCallHandler(null)
        events?.setStreamHandler(null)
        UnityHostBridge.listen(null)
        pendingResult?.error("ACTIVITY_DESTROYED", "Permission request was interrupted.", null)
        pendingResult = null
        pendingOpen = null
        super.cleanUpFlutterEngine(flutterEngine)
    }
}
