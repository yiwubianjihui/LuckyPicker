# 幸运摇人器 · Linux 发行版

基于 Electron 的 Linux 桌面版，与 Windows 版同款界面与功能：
班级/性别筛选、不重复模式、抽一人/连抽五人、屏蔽名单、名单管理（导入 Excel/CSV）、
微软神经语音在线播报（内置直连）+ 百度 + 系统语音降级。

## 文件

- `LuckyPicker-linux-x64.zip` —— 完整发行包（解压即用，含 Electron 运行时）
- 解压后目录：`lucky-picker`（主程序）、`start.sh`（启动脚本）、`lucky-picker.desktop`（桌面入口）、`resources/app/`（应用本体：界面与名单逻辑）

## 运行

```bash
unzip LuckyPicker-linux-x64.zip
cd LuckyPicker-linux-x64
chmod +x lucky-picker start.sh   # zip 解压后可能需要恢复执行权限
./start.sh
```

> `start.sh` 使用 `--no-sandbox` 启动（避免部分发行版缺少 setuid sandbox 的问题）。

## 安装到系统（可选）

```bash
sudo mkdir -p /opt/lucky-picker
sudo cp -r LuckyPicker-linux-x64/* /opt/lucky-picker/
sudo chmod +x /opt/lucky-picker/lucky-picker
# 桌面入口（按实际路径调整 Exec/Icon 行）
cp /opt/lucky-picker/lucky-picker.desktop ~/.local/share/applications/
```

## 说明

- 名单数据保存在应用内（localStorage），可在「名单管理」中编辑、导入 .xlsx/.csv、导出 JSON。
- 在线语音：微软神经语音（Azure Speech REST，内置直连，无需密钥）→ 百度翻译 → 系统语音；
  「语音设置」里可切换音色（晓晓/云希/云扬）与在线源。
- 系统语音依赖 Chromium 的语音合成（可能需要 speech-dispatcher 等组件）；在线语音无需任何系统组件。
- 在无桌面环境的服务器上请使用 `./lucky-picker --no-sandbox --disable-gpu` 加 X 转发或 Wayland。
