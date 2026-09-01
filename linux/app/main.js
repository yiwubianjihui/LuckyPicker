// YBH幸运摇人器 Linux 桌面应用（Electron 壳）
const { app, BrowserWindow } = require('electron');
const path = require('path');

function createWindow() {
  const win = new BrowserWindow({
    width: 920,
    height: 860,
    minWidth: 640,
    minHeight: 700,
    autoHideMenuBar: true,
    title: 'YBH幸运摇人器',
    icon: path.join(__dirname, 'icon.png'),
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false
    }
  });
  win.loadFile(path.join(__dirname, 'index.html'));
}

app.whenReady().then(createWindow);

app.on('window-all-closed', function () {
  app.quit();
});
