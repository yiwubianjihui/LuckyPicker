# YBH幸运摇人器 · 开发记录

版本号规则：**26H2 Build ×**,每发布一次 × 递增 1。
本记录整理自项目开发过程中的会话档案(2026-08)。

## 2026-08-14 · 起点:LuckyRandom 网页原型

- 最早的「幸运摇人器」是一个网页版原型(`data.js + index.html`),
  配套 Python TTS 服务(`TTS_server`,输出 mp3 语音播报)。
- 功能:班级/性别抽取、不重复模式、连抽、屏蔽名单。
- 该原型保留在工作区 `LuckyRandom/` 目录,作为后续版本的效果参考。

## 2026-08-14 · LuckyPicker(Win32)诞生

- 参考 LuckyRandom 用 C# WinForms 重写为原生 Win32 桌面应用,
  零第三方依赖,Windows 10/11 开箱即用。
- 自绘圆角卡片 UI、随机滚动动画、班级选择弹窗、快捷键体系。
- 同步交付 `Setup.exe` 安装程序(注册 HKCU 卸载项、快捷方式、可选预装名单)。

## 2026-08-14 · 更自然的语音 + 名单编辑

- 语音升级:微软翻译令牌接口换取 Azure Speech 令牌,**内置直连神经语音**
  (晓晓/云希/云扬,与 Edge TTS 同源音色),国内网络可用;
  多层降级:微软神经语音 → Edge 直连 → 百度翻译在线语音 → 本地 SAPI5。
- 兼容性保障:Windows 10 内置 Media Foundation 播放 MP3,无需解码器;
  全链路后台线程 + 超时控制,低配设备不卡顿。
- 智能缓存:合成结果落盘,听过的名字秒播;后台预热整班名单。
- 新增程序内名单管理:表格增删改、导入 Excel(.xlsx,内置 OOXML 解析)/
  CSV(GBK/UTF-8 自动识别)、导入预览与列匹配。

## 2026-08-14 · 走向多端:Android / Linux / Web

- 抽出跨平台 Web 核心(`web/index.html + app.js`):同一套摇人逻辑、
  纯 JS SHA-256/HMAC、微软神经语音直连 TTS、xlsx 解压(pako)。
- Android:无 Gradle 纯命令行构建(aapt2 + d8 + apksigner),WebView 原生壳。
- Linux:Electron 壳,解压即用。
- 配套单元测试:核心逻辑/解析/真实 TTS 链路 13 项 + jsdom 界面冒烟 11 项。
- 上线简单下载页,提供多版本选择下载。

## 2026-08-15 · 26H2 版本体系(Build 11)

- 更名「**YBH幸运摇人器**」,统一品牌。
- 新增版本查看窗口:当前版本、更新通道、内部构建号、一键复制。
- 新增检查更新接口协议(`update.json`):比较 build 号,
  提示新版本并可打开下载页;适配 JS 访客验证站点(toNumbers/slowAES)。
- 安装程序新增「预装名单」选项。

## 2026-08-31 · Build 12:悬浮球 + 开机自启动 + 开源

- **桌面悬浮球**:置顶小圆球,单击立即抽一人(语音播报与主界面一致),
  拖动移动并记忆位置,右键菜单(显示主窗口/抽一人/连抽五人/重置候选池/
  开机自启动/隐藏);抽中姓名在球面短暂显示,投屏场景远距离可见。
- **开机自启动**:当前用户 Run 注册表项,无需管理员权限;
  「版本与更新 → 偏好设置」与悬浮球菜单均可开关;开机启动带 `/min`
  参数——不弹主窗口,只显示悬浮球;卸载自动清理。
- Web 核心同步:页面内悬浮球(单击抽取/拖动/长按菜单),
  Android 增加开机自启动桥接与 BOOT_COMPLETED 接收器
  (遵循用户开关,默认关闭)。
- 界面优化:标题统一品牌、版本号上屏、设置面板分组更清晰。
- 文件整理:仓库结构化(desktop/ web/ android/ linux/ tools/ site/ docs/),
  内嵌名单默认替换为**虚构示例名单**,真实名单经 `tools/embed_roster.py`
  本地注入、永不入库。
- 正式开源:GitHub [Yibianhui/LuckyPicker](https://github.com/Yibianhui/LuckyPicker)(MIT)。
- 下载站改版:`lr.yibianhui.cn/`,与 YBH Blog App 下载页互设入口。




## 2026-09-02 · WPF 版重构（现代界面，wpf/ 目录）

- **技术路线**：WinUI 3 因系统 WindowsAppRuntime 异常无法启动，且根因是
  manifest `dpiAware(PerMonitorV2)` 在无头/远程会话触发 0xC0000409(BEX64)；
  改用 **WPF（.NET 8，系统原生，不依赖 WindowsAppSDK）**，manifest 去掉 dpiAware。
- **功能完整**：主窗口（筛选/抽一人动画/连抽/屏蔽）、悬浮球（WPF 透明圆球，
  拖动/单击/右键/球面结果）、班级小窗、托盘、单实例、/min 开机悬浮球、
  名单导入导出、抽选记录、版本更新（静默 + 打开即查）。
- **发布**：self-contained 发布（162MB，免装 .NET runtime），zip 66MB，
  已部署 lr.yibianhui.cn 下载页「Windows 新版（WPF 重构）」。
- 逻辑层复用（Core/DataIO/TTS/UpdateManager/AutoStart，System.Text.Json）。
## 2026-09-01 · Build 12 完善:开机悬浮球 → 班级小窗 → 直接摇人

- **开机自启动完整链路**:开机自启(带 /min)时主窗口完全隐藏(启动零闪窗),
  仅桌面悬浮球常驻;单击球弹出**「选择班级」小窗**(ClassMiniForm:置顶圆角卡片、
  悬浮球旁自动防出屏、点选立即抽取、失焦即关),选完即可连续摇人。
- 悬浮球菜单/托盘菜单/设置菜单均提供显示主窗口入口。
## 2026-09-01 · Build 12 打磨:托盘 / 悬浮球 / 设置菜单 / 系统目录安装

- **关闭最小化到托盘**:点关闭不退出,托盘图标常驻(双击恢复、右键退出)。
- **悬浮球重做**:简约单色,独立于主窗口(最小化/隐藏时仍常驻);
  抽中结果单行显示(单人显姓名 / 多人显「N 人」,修复换行错位);
  开机自启仅悬浮球时,点按弹出**班级选择菜单**(替代全屏弹窗)。
- **菜单布局**:右上角「设置 ▼」集成名单管理 / 抽选记录 / 版本更新 /
  开机自启动开关 / 桌面悬浮球开关。
- **安装器**:修复「找不到名单文件」bug(资源名统一 students.json);
  默认安装到**系统目录**(Program Files,UAC requireAdministrator),
  卸载注册表 HKLM;名单数据写入 %ProgramData%\LuckyPicker(系统目录下可写)。
- **界面**:字号整体加大(标题 27pt / 按钮 14.5pt),顶部集成设置按钮更简洁;
  评估 WinUI 3 但因其需 WinAppSDK runtime 部署复杂,改用 GDI 自绘现代风
  (零第三方依赖,Windows 10/11 兼容)。

## 更新接口协议(部署示例)

```json
{
  "product": "YBH幸运摇人器",
  "version": "26H2 Build 12",
  "build": 12,
  "channel": "26H2",
  "url": "https://lr.yibianhui.cn/",
  "notes": "1. ...",
  "releaseDate": "2026-08-31",
  "mandatory": false
}
```

程序以 `build` 号与当前 Build 比较;`url` 存在时「发现新版本」可一键打开下载页。
