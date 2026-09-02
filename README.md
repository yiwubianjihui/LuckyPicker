# YBH幸运摇人器

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Version](https://img.shields.io/badge/version-26H2%20Build%2014-blue.svg)](docs/CHANGELOG.md)
[![GitHub](https://img.shields.io/badge/GitHub-Yibianhui%2FLuckyPicker-black.svg)](https://github.com/Yibianhui/LuckyPicker)

多端班级随机摇人工具：**Windows（WPF 重构版为主）+ Android + Linux + Web**。
按班级 / 性别抽取学生，固定不重复模式、连抽五人、屏蔽名单、
**自然语音播报**（微软神经语音 + 本地 SAPI 自动降级）、**Excel/CSV 名单导入**、
**抽选记录**、**桌面悬浮球**与**开机自启动**、**单实例保护**与**托盘新版本提醒**。

> Windows 新版采用 **WPF 重构**（`wpf/`，现代界面、悬浮球/班级小窗/托盘，解压即用）；
> 经典 WinForms 版（`desktop/`，Setup 安装版）保留可用，不再演进。

- 📥 下载页：<https://lr.yibianhui.cn/>
- 📋 开发记录：[docs/CHANGELOG.md](docs/CHANGELOG.md)

## 功能特性

### 摇人
- **班级选择**：启动时弹出班级选择窗口，之后可随时切换。
- **性别筛选**：都抽 / 男生 / 女生。
- **不重复模式**：默认开启，被抽中者不再参与，直到重置池。
- **抽一人 / 连抽五人**：随机滚动动画，抽中自动语音播报。
- **屏蔽名单**：输入姓名临时屏蔽（不改数据文件），一键移除。
- **桌面悬浮球**（简约单色）：置顶小圆球，单击立即抽一人；独立于主窗口常驻，
  拖动移位并记忆，右键菜单速控；抽中结果球面显示（单人显姓名 / 多人显「N 人」）。
  **开机自启时只显示悬浮球**（零闪窗），单击弹出「选择班级」小窗，点选立即摇人。
- **快捷键**（经典版）：空格 = 抽一人；Ctrl+M = 连抽五人；Ctrl+R = 重置池；
  Ctrl+E = 名单管理；Ctrl+H = 抽选记录；Ctrl+U = 版本与更新；Ctrl+V = 重播结果。

### 托盘与关闭
- 点击关闭按钮**最小化到托盘**（不退出），托盘图标常驻，双击恢复主窗口，
  右键菜单含开机自启开关与退出。

### 开机自启动（新增）
- 「设置」菜单、版本窗口偏好设置或悬浮球右键菜单均可开关；
  写入当前用户 Run 注册表项（HKCU），**无需管理员权限**。
- 开机自启带 `/min` 参数：登录后不弹主窗口，只显示悬浮球。
- 卸载自动清理注册表项；Android 端在「语音设置」中开关（默认关闭）。

### 自然语音（微软神经语音直连 + 多层降级，Windows 10 兼容）
- **微软神经语音（内置直连，默认）**：微软翻译令牌接口换取 Azure Speech
  令牌，直连合成接口——与 Edge TTS 同源音色（晓晓/云希/云扬），国内网络可用。
- **百度翻译在线语音**（免密钥）→ **本地 SAPI5** 兜底，断网零延迟播报。
- 智能缓存（听过的名字秒播）+ 整班后台预热 + 全链路超时控制，低配友好。
- 语音源可在「名单管理 → 语音设置」中选择。

### 抽选记录
- 每次抽取自动写入本机历史（保留最近 500 条），支持导出 CSV、
  复制、清空，并统计累计人次与去重人数。

### 名单管理（程序内编辑 + 导入 Excel）
- 表格内直接增删改（姓名 / 班级 / 性别），维护班级显示名称。
- 导入 .xlsx / .csv：自动识别表头与列，预览 + 列匹配，替换或追加；
  兼容 GBK / UTF-8，"19班" 自动归一化为 "19"。

## 多端与下载

| 平台 | 产物 | 获取 |
| --- | --- | --- |
| Windows | `Setup.exe`（安装版）/ `LuckyPicker.exe`（便携版）/ **`LuckyPicker-win-wpf-*.zip`（新版 WPF 界面，自带运行时）** | [下载页](https://lr.yibianhui.cn/) 或 [Releases](https://github.com/Yibianhui/LuckyPicker/releases) |
| Android | `LuckyPicker.apk`（WebView 壳，已签名） | 同上 |
| Linux | `LuckyPicker-linux-x64.zip`（Electron，解压即用） | 同上 |
| Web | 纯静态页面，任意现代浏览器打开 | `web/index.html` |

检查更新：程序内「版本与更新」读取
`https://lr.yibianhui.cn/update.json`（默认），
旧地址 `yibianhui.cn/LuckyPicker/update.json` 失效时自动回退尝试。

## 目录结构

```
LuckyPicker/
├── desktop/           Win32 桌面版（C# WinForms）
│   ├── LuckyPicker.cs 主界面（摇人逻辑、自绘 UI、悬浮球接入）
│   ├── FloatingBall.cs 桌面悬浮球窗口
│   ├── AutoStart.cs   开机自启动（HKCU Run）
│   ├── Tts.cs         TTS 引擎（神经语音直连 + 降级）+ AppConfig
│   ├── Update.cs      版本/检查更新/偏好设置窗口
│   ├── History.cs     抽选记录
│   ├── DataIO.cs      Excel(.xlsx)/CSV 导入解析
│   ├── Editor.cs      名单管理窗口
│   ├── Setup.cs       安装/卸载程序（含开机自启动选项）
│   ├── ConsoleTest.cs 离线单元测试
│   └── build.ps1      桌面版一键构建
├── web/               跨平台 Web 核心（悬浮球/Android/Linux 共用）+ 测试
├── android/           安卓工程（WebView 壳 + 开机自启桥接 + 无 Gradle 构建）
├── linux/             Electron 壳
├── site/              下载页源码（部署于 lr.yibianhui.cn）
├── tools/             embed_roster.py 私有名单注入工具
├── docs/              CHANGELOG.md 开发记录
├── students.demo.json 内置虚构示例名单（仓库默认）
└── update.sample.json 更新接口返回示例
```

## 从源码构建

**Windows 桌面版**：双击 `desktop/build.ps1`。使用系统自带 csc.exe，
无需 IDE / SDK；构建后运行 `desktop/dist/ConsoleTest.exe` 跑离线测试。

**Android**：配置 JDK 21 + Android SDK(34) 后运行 `android/build-apk.ps1`（无 Gradle）。

**Web / Linux**：`web/` 即用；Linux 用 Electron 打包 `linux/app/`。

**Web 核心测试**：`web/test-core.cjs`（核心逻辑）+ `web/smoke-ui.cjs`（界面冒烟）。

## 名单与隐私（重要）

- 仓库默认内置**虚构示例名单**（张伟、王芳……），**不含任何真实个人信息**。
- 如需在班级内部署真实名单：把 `students.json`（与
  `students.demo.json` 同格式）放在仓库根目录——它已被 `.gitignore`
  忽略，**永不进入版本库**；运行 `python tools/embed_roster.py`
  注入 Web 核心后构建即可。
- 含真实名单的构建产物仅限内部使用，请勿公开分发。

## 名单文件格式

```json
{
  "classes": { "1": "示例一班", "2": "示例二班" },
  "students": [
    { "name": "张伟", "classId": "1", "gender": "男" },
    { "name": "王芳", "classId": "1", "gender": "女" }
  ]
}
```

- `gender` 可填 `"男"`、`"女"` 或 `""`（未知，按"都抽"包含）。
- 推荐在程序内「名单管理」编辑或导入，避免手改 JSON。

## 技术栈

- Windows：C# / WinForms（.NET Framework 4.x，系统自带运行时），GDI+ 自绘 UI
- Web 核心：原生 HTML/JS（零依赖），纯 JS SHA-256/HMAC，pako 解压 .xlsx
- Android：无 Gradle 命令行构建（aapt2 + d8 + apksigner），WebView 壳
- Linux：Electron 壳
- 安装程序同为 C# 编写，主程序作为内嵌资源打包，HKCU 卸载项

## License

[MIT](LICENSE) © 2026 Yibianhui

### Windows 新版（WPF，主路线）
要求：.NET 8 SDK（无 VS 亦可，命令行构建）。

```bash
cd wpf
dotnet publish -c Release -r win-x64 --self-contained true   # 输出 self-contained 目录（免装 .NET）
# 或 dotnet build -c Release                                # 框架依赖（本机需装 .NET 8 Desktop Runtime）
```

> 注意：`app.manifest` 请勿添加 `dpiAware` 声明——在无头 / 远程会话下会导致
> WPF/WinUI 应用启动即 0xC0000409（BEX64）崩溃。

