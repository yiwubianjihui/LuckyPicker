#!/usr/bin/env python3
# ================================================================
# embed_roster.py — 私有构建名单注入工具
#
# 仓库默认内置「虚构示例名单」(students.demo.json)，保证开源仓库
# 不含任何真实学生个人信息。如果你要在班级内部署带真实名单的版本：
#
#   1. 把真实名单导出为 students.json（与 students.demo.json 同格式）
#      放在仓库根目录（该文件已被 .gitignore 忽略，不会进入版本库）；
#   2. 运行:  python tools/embed_roster.py
#   3. 再执行 desktop/build.ps1 或 android/build-apk.ps1，
#      构建产物即内嵌你的真实名单。
#
# 本脚本把 students.json 注入到 web/app.js 的 DEFAULT_DATA 与
# desktop 构建所用的 students.json 资源位（build.ps1 自动完成 C# 侧）。
# ================================================================
import json
import io
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP_JS_PATHS = [
    os.path.join(ROOT, "web", "app.js"),
]
MARK = "var DEFAULT_DATA = "


def fail(msg):
    print("ERROR:", msg)
    sys.exit(1)


def main():
    src = os.path.join(ROOT, "students.json")
    if not os.path.exists(src):
        fail("未找到 students.json（真实名单）。请先把它放在仓库根目录。")
    try:
        data = json.load(io.open(src, encoding="utf-8"))
        students = data.get("students") or []
        classes = data.get("classes") or {}
    except Exception as e:
        fail("students.json 解析失败: %s" % e)
    if not students:
        fail("students.json 中没有学生数据。")

    line = "  var DEFAULT_DATA = " + json.dumps(
        data, ensure_ascii=False, separators=(",", ":")) + ";\n"

    for path in APP_JS_PATHS:
        if not os.path.exists(path):
            fail("缺少 %s" % path)
        s = io.open(path, encoding="utf-8", newline="").read()
        lines = s.splitlines(keepends=True)
        hit = -1
        for i, l in enumerate(lines):
            if l.lstrip().startswith(MARK):
                hit = i
                break
        if hit < 0:
            fail("%s 中找不到 DEFAULT_DATA 行" % path)
        eol = "\r\n" if lines[hit].endswith("\r\n") else "\n"
        indent = lines[hit][: len(lines[hit]) - len(lines[hit].lstrip())]
        lines[hit] = indent + line.rstrip("\r\n") + eol
        io.open(path, "w", encoding="utf-8", newline="").write("".join(lines))
        print("OK 注入 %s（%d 名学生 / %d 个班级）" % (path, len(students), len(classes)))

    print("完成。现在可以运行 desktop/build.ps1 与 android/build-apk.ps1。")
    print("注意：构建完成后请勿把含真实名单的产物上传或开源。")


if __name__ == "__main__":
    main()
