# Windows WPF 版发布流程（LuckyPicker）

> 本文档沉淀自 26H2 Build 13 的 WPF 重构发布实战。适用于 `wpf/` 项目的任何迭代发布。

## 1. 构建

```bash
cd wpf
dotnet publish -c Release -r win-x64 --self-contained true -o <发布输出目录>
```

- self-contained：自带 .NET 8 运行时（约 162MB / zip 约 66MB），用户机免装依赖。
- 构建前确保**项目目录内没有 `obj_*` / `bin_*` 残留目录**——WPF 的 `_wpftmp` 临时项目
  与 XamlCompiler 会把它们扫进编译，导致产物污染（运行时诡异崩溃）。
  旧目录请 `mv` **移出项目目录**。
- 构建后**确认 exe 真实存在**再运行冒烟——"运行即退出"很多时候是构建失败 +
  exe 缺失的假象。

## 2. 冒烟

- 正常启动（进程存活 ≥6s 且崩溃日志 `%TEMP%\lucky_crash.log` 走到 S5）
- `/min`（悬浮球模式）、第二实例（单实例提示）
- 逻辑层可用独立 console 工程引用 `wpf/Core.cs` 做断言测试（抽取/不重复/屏蔽/筛选）

## 3. 打包 zip

```python
# 顶层目录名 LuckyPicker/，DEFLATED 压缩
zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED)  # 遍历发布目录写入 'LuckyPicker/<相对路径>'
```

## 4. 部署 lr.yibianhui.cn

FTP：`47.238.193.61`，上传到 `/LuckyRandom/download/<文件名>`。

- **66MB 大文件经常超时**：`storbinary` 失败后**重试**，且上传后必须用
  `ftp.size(远程路径)` 校验**远端大小 == 本地大小**，不一致则重传。
- 下载页 `site/index.html` 改动后（SHA 回填等）上传到 `/LuckyRandom/index.html`。

## 5. GitHub Release 资产同步

每次重新 publish 后，**必须**同步 Release 资产（否则用户从 Release 拿到旧包）：

```python
# 1) GET  /repos/Yibianhui/LuckyPicker/releases/<rid>/assets  → 找同名资产 id
# 2) DELETE /repos/.../releases/assets/<id>
# 3) POST https://uploads.github.com/repos/Yibianhui/LuckyPicker/releases/<rid>/assets?name=<名>
#    (binary, Authorization: token <ghp>, Content-Type: application/octet-stream)
```

## 6. 版本号与文案

- 版本规则：**每次正式发布 bump 一次**（26H2 Build ×，× 递增 1）。
- 发布涉及：代码内版本文案（`SubText`）、`app.manifest` assemblyIdentity、
  下载文件名、`update.json`（build/notes）、下载页徽章。

## 7. 已踩过的大坑（务必规避）

| 坑 | 现象 / 根因 | 对策 |
| --- | --- | --- |
| **app.manifest 加 `dpiAware`(PerMonitorV2)** | 无头/远程会话下 WPF/WinUI 应用启动即 `0xC0000409`(BEX64)，Main 前无任何日志 | manifest 保留 supportedOS + asInvoker，**删掉 `<application>` 段** |
| WPF `ContextMenu` 默认仅右键触发 | 按钮"点了没反应" | 左键 Click 中 `ctx.PlacementTarget=btn; ctx.Placement=Bottom; ctx.IsOpen=true` |
| `DragMove()` 阻塞至释放 | 拖动后 `MouseUp` 不触发 → 位置不保存 | `DragMove()` 返回后**立即 SavePosition** |
| XAML 加载顺序触发事件 | `IsChecked=True` 在后续控件初始化前触发 handler → NullReference | handler 内对未初始化控件判空 |
| WinForms 类型歧义 | `Application/MessageBox` 在 WPF+WinForms 双启用时 CS0104 | 全限定 `System.Windows.Application` 等 |
| WinUI 3（若未来迁移） | 系统 WindowsAppRuntime 栈异常 + dpiAware 同样致命 | 先修 dpiAware；框架迁移前冒烟模板应用 |
| 样式 TargetType 不匹配 | `Button` 的 Style 套在 `ToggleButton` → XamlParseException | Style 与元素类型一一对应 |
| SAPI 兜底未初始化 | 在线 TTS 失败后无声（`sapi == null` → 直接 return） | 启动/切语音时调用 `tts.InitStatus()` |

## 8. 收尾

`git add -A && git commit` → `gh_api_push.py`（PROJECT_DIR=仓库根）推送。

## 9. Build 15 补充经验（2026-09-04）

### 版本矩阵（下载页定版）
| 位置 | 文件 | 说明 |
| --- | --- | --- |
| 主推 | `LuckyPicker-win-wpf-26H2-buildN.zip` | WPF 绿色版 |
| 选项1 | `LuckyPicker-win-setup-26H2-buildN.exe` | WPF 安装版（Program Files / 管理员 / 覆盖升级 / 卸载） |
| 选项2/3 | Android / Linux `buildN` | 同步 bump |
| 选项4 | `portable-26H2-build14.zip` | 经典便携（冻结在 14，不再更新；勿再打新标） |
| 历史 | GitHub Releases | 全部旧版 |

注意：经典 WinForms 版已冻结——不要再为它打新 build 标（内容与版本号必须一致）。

### 发布流水线（脚本化）
- 内部版：`E:\Files\内部版\build-internal.ps1`（Excel 名单 → publish → 打包 → 安装器 → 静默安装校验）
- 正式版：publish → pack（交付 zip / app.zip / update.json）→ compile.ps1（安装器）→ lr（size 校验）→ Release → 线上核验（下载页 / update.json / SHA）

### 再次踩过/防住的坑
- **Release 资产脚本**：api() 函数签名必须含 hdr 参数，否则 POST 带 Content-Type 会 TypeError，表现为后续 404，极难察觉。
- **PowerShell 5.1**：.ps1 含中文必须存 UTF-8 with BOM + CRLF；读 JSON 用 [IO.File]::ReadAllText($p, UTF8)。
- **窗口关闭期间 Show 异常**：弹出面板类窗口一律「静态单例 + 先关旧再开新 + 回调 Dispatcher.BeginInvoke 延后」。
- **单实例保护**：App.OnStartup 的 Mutex 检测严禁注释禁用（曾导致自启双悬浮球）；调试需要多实例时用环境变量开关而非删代码。
- **网络抖动**：lr（阿里云海外）与 GitHub 会间歇超时——部署脚本全部带重试；GitHub 断连时先做本地任务再补推。
- **上传后校验**：FTP 大文件必用 ftp.size() 比对；小文件批传易在长连接尾部超时，单独补传即可。

