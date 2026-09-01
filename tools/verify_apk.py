#!/usr/bin/env python3
# ================================================================
# verify_apk.py — APK 构建产物校验
#
# 用途：检查 android/out/LuckyPicker.apk 是否完整包含 classes.dex 与
#       assets（app.js / index.html / pako.min.js），以及版本信息。
#       （历史版本中曾因 JDK 路径写死导致 dex 与 assets 未打进 APK
#         却仍能签名产出，故增加该独立校验步骤。）
# 用法：python tools/verify_apk.py [apk路径]
# ================================================================
import os
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APK = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    ROOT, "android", "out", "LuckyPicker.apk")

REQUIRED = [
    "classes.dex",
    "assets/app.js",
    "assets/index.html",
    "assets/pako.min.js",
]


def main():
    if not os.path.exists(APK):
        print("FAIL: 未找到 %s" % APK)
        sys.exit(1)
    size = os.path.getsize(APK)
    with zipfile.ZipFile(APK) as z:
        names = z.namelist()
        missing = [n for n in REQUIRED if n not in names]
        # dex 中应同时包含 MainActivity 与 BootReceiver 的类
        dex_ok = False
        if "classes.dex" in names:
            data = z.read("classes.dex")
            dex_ok = (b"MainActivity" in data) and (b"BootReceiver" in data)
        # 名单脱敏复核：不得包含真实学生姓名
        assets_blob = b"".join(z.read(n) for n in names if n.startswith("assets/"))
        leak = [w for w in (b"\xe5\x94\x90\xe6\xb5\xa9\xe5\xb3\xbb", b"\xe5\xbf\x97\xe6\x88\x90\xe5\x8d\x81\xe4\xb9\x9d")
                if w in assets_blob]

    print("APK: %s (%.1f KB)" % (APK, size / 1024.0))
    print("entries: %d" % len(names))
    for n in REQUIRED:
        print("  %-22s %s" % (n, "OK" if n in names else "MISSING"))
    print("  %-22s %s" % ("dex has both classes", "OK" if dex_ok else "FAIL"))
    print("  %-22s %s" % ("roster sanitized", "OK" if not leak else "FAIL(含真实姓名)"))

    ok = not missing and dex_ok and not leak and size > 40 * 1024
    print("----")
    print("APK OK" if ok else "APK VERIFY FAILED")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
