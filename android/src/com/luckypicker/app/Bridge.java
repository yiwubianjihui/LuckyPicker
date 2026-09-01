package com.luckypicker.app;

import android.content.SharedPreferences;
import android.webkit.JavascriptInterface;
import android.webkit.WebView;
import android.widget.Toast;

/**
 * 供网页 JS 调用的原生桥（window.LuckyBridge）：
 *   LuckyBridge.isBootEnabled()  -> "1"/"0"
 *   LuckyBridge.setBootEnabled(b) -> 持久化开机自启动开关
 * 说明：独立静态类，避免内部类在 d8 打包时触发兼容问题。
 */
public class Bridge {
    private final MainActivity activity;
    private final WebView web;

    public Bridge(MainActivity activity, WebView web) {
        this.activity = activity;
        this.web = web;
    }

    @JavascriptInterface
    public String isBootEnabled() {
        SharedPreferences sp = activity.getSharedPreferences(MainActivity.PREFS, activity.MODE_PRIVATE);
        return sp.getBoolean(MainActivity.KEY_BOOT, false) ? "1" : "0";
    }

    @JavascriptInterface
    public void setBootEnabled(final boolean enabled) {
        SharedPreferences sp = activity.getSharedPreferences(MainActivity.PREFS, activity.MODE_PRIVATE);
        sp.edit().putBoolean(MainActivity.KEY_BOOT, enabled).apply();
        web.post(() -> Toast.makeText(activity,
                enabled ? "已开启开机自启动" : "已关闭开机自启动",
                Toast.LENGTH_SHORT).show());
    }
}
