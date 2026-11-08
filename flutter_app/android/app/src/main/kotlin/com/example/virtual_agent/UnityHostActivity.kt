package com.example.virtual_agent

import android.content.Intent
import android.os.Bundle
import android.view.Gravity
import android.view.KeyEvent
import android.view.View
import android.widget.Button
import android.widget.FrameLayout
import com.unity3d.player.UnityPlayerActivity

class UnityHostActivity : UnityPlayerActivity() {
    private var loadingBack: Button? = null
    var hasReturned = false
        private set

    override fun onCreate(savedInstanceState: Bundle?) {
        UnityHostBridge.attach(this)
        super.onCreate(savedInstanceState)
        val back = Button(this).apply {
            text = "返回 Flutter"
            setOnClickListener { UnityHostBridge.requestClose() }
        }
        loadingBack = back
        val margin = (16 * resources.displayMetrics.density).toInt()
        addContentView(back, FrameLayout.LayoutParams(
            FrameLayout.LayoutParams.WRAP_CONTENT,
            FrameLayout.LayoutParams.WRAP_CONTENT,
            Gravity.TOP or Gravity.END
        ).apply {
            topMargin = margin * 3
            rightMargin = margin
        })
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        hasReturned = false
        UnityHostBridge.deliverOpen()
    }

    override fun onResume() {
        super.onResume()
        hasReturned = false
        UnityHostBridge.deliverOpen()
    }

    override fun onBackPressed() {
        UnityHostBridge.requestClose()
    }

    fun markUnityReady() {
        loadingBack?.visibility = View.GONE
    }

    override fun dispatchKeyEvent(event: KeyEvent): Boolean {
        if (event.keyCode == KeyEvent.KEYCODE_BACK) {
            if (event.action == KeyEvent.ACTION_UP) UnityHostBridge.requestClose()
            return true
        }
        return super.dispatchKeyEvent(event)
    }

    fun returnToFlutter() {
        if (hasReturned) return
        hasReturned = true
        startActivity(Intent(this, MainActivity::class.java)
            .addFlags(Intent.FLAG_ACTIVITY_REORDER_TO_FRONT))
    }

    override fun onUnityPlayerUnloaded() {
        returnToFlutter()
    }
}
