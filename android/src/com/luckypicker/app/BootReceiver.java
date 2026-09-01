package com.luckypicker.app;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;

/**
 * 开机自启动接收器：
 * 仅当用户在「语音设置 → 开机自启动」中开启（SharedPreferences 开关）
 * 时才拉起主界面；默认关闭，尊重用户选择。
 */
public class BootReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context context, Intent intent) {
        if (intent == null) return;
        String action = intent.getAction();
        if (action == null) return;
        if (!Intent.ACTION_BOOT_COMPLETED.equals(action)
                && !"android.intent.action.QUICKBOOT_POWERON".equals(action)) {
            return;
        }
        SharedPreferences sp = context.getSharedPreferences("luckypicker", Context.MODE_PRIVATE);
        if (!sp.getBoolean("boot_enabled", false)) return;

        Intent launch = new Intent(context, MainActivity.class);
        launch.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        try {
            context.startActivity(launch);
        } catch (Exception ignored) {
        }
    }
}
