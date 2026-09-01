# 幸运摇人器 Android APK 构建脚本（无需 Gradle）
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$SDK = Join-Path $root 'sdk'
$BT  = Join-Path $SDK 'build-tools\34.0.0'
$PLATFORM = Join-Path $SDK 'platforms\android-34\android.jar'
$OUT = Join-Path $root 'out'

# ---- JDK 自动探测（避免写死版本号导致 javac/jar 静默失败）----
function Find-JdkBin {
  if ($env:JAVA_HOME) {
    $p = Join-Path $env:JAVA_HOME 'bin\javac.exe'
    if (Test-Path $p) { return (Join-Path $env:JAVA_HOME 'bin') }
  }
  $cmd = Get-Command javac.exe -ErrorAction SilentlyContinue
  if ($cmd) { return (Split-Path -Parent $cmd.Source) }
  $dirs = @(
    'C:\Program Files\Microsoft',
    'C:\Program Files\Java',
    'C:\Program Files\Eclipse Adoptium',
    'C:\Program Files\Android\Android Studio\jbr'
  )
  foreach ($d in $dirs) {
    if (-not (Test-Path $d)) { continue }
    $found = Get-ChildItem -Path $d -Directory -Filter 'jdk*' -ErrorAction SilentlyContinue |
      Sort-Object Name -Descending
    foreach ($c in $found) {
      $p = Join-Path $c.FullName 'bin\javac.exe'
      if (Test-Path $p) { return (Join-Path $c.FullName 'bin') }
    }
  }
  return $null
}

$JAVA = Find-JdkBin
if (-not $JAVA) { Write-Error '未找到 JDK（javac）。请设置 JAVA_HOME 或安装 JDK 17+。'; exit 1 }
Write-Host ('==> JDK: ' + $JAVA)

New-Item -ItemType Directory -Force -Path $OUT | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $OUT 'res') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $OUT 'classes') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $OUT 'dex') | Out-Null

Write-Host '==> aapt2 compile resources'
& (Join-Path $BT 'aapt2.exe') compile --dir res -o (Join-Path $OUT 'res\res.zip')

Write-Host '==> aapt2 link (base apk)'
& (Join-Path $BT 'aapt2.exe') link -o (Join-Path $OUT 'base.apk') --manifest AndroidManifest.xml -I $PLATFORM (Join-Path $OUT 'res\res.zip') --min-sdk-version 24 --target-sdk-version 34 --version-code 13 --version-name '26H2.13'

Write-Host '==> javac MainActivity / BootReceiver'
# 清掉旧 class，避免 javac 失败时沿用陈旧产物
if (Test-Path (Join-Path $OUT 'classes')) { Remove-Item (Join-Path $OUT 'classes') -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $OUT 'classes') | Out-Null
& (Join-Path $JAVA 'javac.exe') --release 8 -classpath $PLATFORM -d (Join-Path $OUT 'classes') (Join-Path $root 'src\com\luckypicker\app\MainActivity.java') (Join-Path $root 'src\com\luckypicker\app\Bridge.java') (Join-Path $root 'src\com\luckypicker\app\BootReceiver.java')
if ($LASTEXITCODE -ne 0) { Write-Error 'javac 编译失败'; exit $LASTEXITCODE }
$classCount = (Get-ChildItem (Join-Path $OUT 'classes') -Recurse -Filter '*.class').Count
Write-Host ('   编译产出 class: ' + $classCount)
if ($classCount -lt 3) { Write-Error 'class 数量异常（应 MainActivity + Bridge + BootReceiver）'; exit 1 }

Write-Host '==> d8 dex'
if (Test-Path (Join-Path $OUT 'dex')) { Remove-Item (Join-Path $OUT 'dex') -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $OUT 'dex') | Out-Null
& (Join-Path $BT 'd8.bat') --release --lib $PLATFORM --output (Join-Path $OUT 'dex') (Get-ChildItem (Join-Path $OUT 'classes') -Recurse -Filter '*.class' | ForEach-Object { $_.FullName })
if ($LASTEXITCODE -ne 0) { Write-Error 'd8 转换失败'; exit $LASTEXITCODE }
if (-not (Test-Path (Join-Path $OUT 'dex\classes.dex'))) { Write-Error '未生成 classes.dex'; exit 1 }

Write-Host '==> add dex + assets into apk (aapt add)'
Push-Location (Join-Path $OUT 'dex')
& (Join-Path $BT 'aapt.exe') add (Join-Path $OUT 'base.apk') 'classes.dex' | Out-Null
if ($LASTEXITCODE -ne 0) { Pop-Location; Write-Error 'aapt add classes.dex 失败'; exit 1 }
Pop-Location
Push-Location $root
& (Join-Path $BT 'aapt.exe') add (Join-Path $OUT 'base.apk') 'assets/app.js' 'assets/index.html' 'assets/pako.min.js' | Out-Null
if ($LASTEXITCODE -ne 0) { Pop-Location; Write-Error 'aapt add assets 失败'; exit 1 }
Pop-Location
Write-Host '   dex 与 assets 已打包（内容由 tools/verify_apk.py 复核）'

Write-Host '==> zipalign'
& (Join-Path $BT 'zipalign.exe') -f 4 (Join-Path $OUT 'base.apk') (Join-Path $OUT 'aligned.apk')

Write-Host '==> keytool debug keystore'
$ks = Join-Path $root 'debug.keystore'
if (-not (Test-Path $ks)) {
  & (Join-Path $JAVA 'keytool.exe') -genkeypair -keystore $ks -alias lucky -keyalg RSA -keysize 2048 -validity 10000 -storepass android -keypass android -dname 'CN=LuckyPicker, O=LuckyPicker'
}

Write-Host '==> apksigner sign'
& (Join-Path $BT 'apksigner.bat') sign --ks $ks --ks-pass pass:android --key-pass pass:android --out (Join-Path $OUT 'LuckyPicker.apk') (Join-Path $OUT 'aligned.apk')

Write-Host '==> verify'
& (Join-Path $BT 'apksigner.bat') verify --print-certs (Join-Path $OUT 'LuckyPicker.apk')
& (Join-Path $BT 'aapt.exe') dump badging (Join-Path $OUT 'LuckyPicker.apk') | Select-Object -First 8
Get-Item (Join-Path $OUT 'LuckyPicker.apk') | Select-Object FullName,Length | Format-List
Write-Host 'BUILD OK'
