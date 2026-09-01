# YBH幸运摇人器 - 构建脚本（桌面 Win32）
# 使用 .NET Framework 自带的 csc.exe 编译，无需安装 Visual Studio / .NET SDK。
# 前置条件：Windows 10/11（已内置 .NET Framework 4.x）。
#
# 名单数据：构建优先使用仓库根目录的 students.json（真实名单，已被
# .gitignore 忽略）；若不存在则自动回退到 students.demo.json（虚构示例名单），
# 保证开源克隆直接构建即可得到干净的演示版本。
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path           # desktop/
$repo = Split-Path -Parent $root                                   # 仓库根
Set-Location $root

$csc    = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$speech = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF\System.Speech.dll'
$webx   = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\System.Web.Extensions.dll'

# ---- 名单解析：students.json > students.demo.json ----
$students = Join-Path $repo 'students.json'
$demo     = Join-Path $repo 'students.demo.json'
if (-not (Test-Path $students)) {
    if (-not (Test-Path $demo)) { Write-Error '缺少 students.json 与 students.demo.json'; exit 1 }
    $students = $demo
    Write-Host '==> 未找到 students.json，使用示例名单 students.demo.json（演示版）'
} else {
    Write-Host '==> 使用本地 students.json（真实名单，仅限内部使用）'
}
$studentsName = Split-Path -Leaf $students
$updateSample = Join-Path $repo 'update.sample.json'
$icon = Join-Path $repo 'icon.ico'

New-Item -ItemType Directory -Force -Path dist | Out-Null

Write-Host '==> 编译 主程序 LuckyPicker.exe'
& $csc /nologo /target:winexe /optimize+ /win32icon:$icon /out:dist\LuckyPicker.exe "/res:$students,LuckyPicker.students.json" /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:$speech /r:$webx /r:System.IO.Compression.dll /r:System.Xml.dll /r:System.Xml.Linq.dll LuckyPicker.cs Tts.cs AutoStart.cs FloatingBall.cs DataIO.cs Editor.cs Update.cs History.cs
if ($LASTEXITCODE -ne 0) { Write-Error '主程序编译失败'; exit $LASTEXITCODE }

Write-Host '==> 编译 安装程序 Setup.exe（含 UAC requireAdministrator 清单）'
# 注意：名单资源名固定为 students.json，与 Setup.cs ExtractResource(DataFile) 一致
& $csc /nologo /target:winexe /optimize+ /win32icon:$icon /win32manifest:setup.manifest /out:dist\Setup.exe /res:dist\LuckyPicker.exe,LuckyPicker.exe "/res:$students,students.json" "/res:$updateSample,update.sample.json" /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll Setup.cs
if ($LASTEXITCODE -ne 0) { Write-Error '安装程序编译失败'; exit $LASTEXITCODE }

Write-Host '==> 编译 离线单元测试 ConsoleTest.exe'
& $csc /nologo /out:dist\ConsoleTest.exe /r:dist\LuckyPicker.exe /r:System.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.Xml.dll /r:System.Xml.Linq.dll /r:$webx ConsoleTest.cs
if ($LASTEXITCODE -ne 0) { Write-Error '测试编译失败'; exit $LASTEXITCODE }

Copy-Item $students dist\students.json -Force
Copy-Item $updateSample dist\update.sample.json -Force
Write-Host ''
Write-Host '构建完成：'
Write-Host '  dist\LuckyPicker.exe   便携版主程序（含悬浮球 / 开机自启动）'
Write-Host '  dist\Setup.exe         安装程序（内嵌主程序与名单，可选开机自启动）'
Write-Host '  dist\students.json     名单数据文件'
Write-Host '  dist\update.sample.json  更新接口示例（部署到下载站）'
Write-Host '  dist\ConsoleTest.exe   离线单元测试（& dist\ConsoleTest.exe）'
