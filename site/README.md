# 幸运摇人器 · 下载中心

一个零依赖的单页下载站：选择平台版本 → 查看说明 → 一键下载。

## 使用方式

### 方式一：本地直接打开（最简单）
双击 `index.html` 即可在浏览器中使用（下载链接为相对路径，直接可用）。

### 方式二：内网/公网部署
把整个 `downloads` 目录原样上传到任意静态服务器（Nginx / 宝塔 / GitHub Pages 等），
访问其中的 `index.html` 即可。目录结构即 URL 结构：

```
downloads/
├── index.html                      # 下载页
└── files/
    ├── Windows/Setup.exe           # Windows 安装版
    ├── Windows/LuckyPicker-win-portable.zip   # Windows 便携版
    ├── Android/LuckyPicker.apk     # Android 版
    ├── Linux/LuckyPicker-linux-x64.zip        # Linux 版（约 186MB）
    └── Web/lucky-picker-web.zip    # Web 版
```

## 页面说明
- 五个版本卡片：Windows 安装版 / Windows 便携版 / Android / Linux / Web 版
- 每版显示：大小、功能描述、安装步骤、适用提示
- 测试：`test-page.cjs`（jsdom 冒烟测试，10 项全过）
- 更新版本后：替换 `files/` 下对应文件，并同步 `index.html` 里 VERSIONS 中的 `size` 字段即可
