# -*- coding: utf-8 -*-
"""生成名单导入模板（xlsx + csv）。

模板中的姓名全部来自 students.demo.json 的虚构示例，不含真实学生信息。
重新生成：python tools/make_roster_template.py
"""
import csv
import os

from openpyxl import Workbook
from openpyxl.styles import Alignment, Border, Font, PatternFill, Side
from openpyxl.worksheet.datavalidation import DataValidation

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(ROOT, "docs", "templates")

HEADERS = ["姓名", "班级", "性别", "读音"]
ROWS = [
    ["张伟", "1", "男", ""],
    ["王芳", "1", "女", ""],
    ["曾露", "1", "女", "增露"],
    ["高翔", "2", "男", ""],
    ["罗雪", "2", "女", ""],
]

NOTES = [
    ("姓名（必填）", "填写学生姓名。同班同名会被视为重复，导入时只保留第一条。"),
    ("班级", "填 1、2、3 或 3班，程序会自动归一为班级号；留空默认 1 班。"),
    ("性别", "填「男」或「女」；留空表示不参与性别筛选。"),
    ("读音", "可选。多音字可填同音字纠正朗读，例如「曾露」填「增露」；只影响语音播报，显示仍是原名。"),
    ("填写位置", "第 1 行是表头，请勿修改；从第 2 行开始逐行填写，不要留空行。"),
    ("文件格式", "支持 .xlsx 和 .csv。CSV 请用「CSV UTF-8」保存，避免中文乱码。"),
    ("导入方式", "在「名单管理 → 导入名单」中选择替换或追加；导入前程序会自动备份当前名单。"),
    ("隐私提示", "模板中的姓名均为虚构示例，可直接替换。请勿把真实名单上传到公开仓库或公开发布。"),
]

BRAND = "3F6B58"
LINE = "D9D9D9"
SOFT = "EDF3EF"


def build_xlsx(path):
    wb = Workbook()
    ws = wb.active
    ws.title = "学生名单"

    header_fill = PatternFill("solid", fgColor=BRAND)
    header_font = Font(name="Microsoft YaHei", size=11, bold=True, color="FFFFFF")
    body_font = Font(name="Microsoft YaHei", size=11)
    thin = Side(style="thin", color=LINE)
    border = Border(left=thin, right=thin, top=thin, bottom=thin)

    for col, text in enumerate(HEADERS, 1):
        cell = ws.cell(row=1, column=col, value=text)
        cell.fill = header_fill
        cell.font = header_font
        cell.border = border
        cell.alignment = Alignment(horizontal="center", vertical="center")

    for r, row in enumerate(ROWS, 2):
        for c, value in enumerate(row, 1):
            cell = ws.cell(row=r, column=c, value=value)
            cell.font = body_font
            cell.border = border
            cell.alignment = Alignment(
                horizontal="center" if c in (2, 3) else "left", vertical="center"
            )

    ws.row_dimensions[1].height = 26
    for r in range(2, 2 + len(ROWS)):
        ws.row_dimensions[r].height = 22

    ws.column_dimensions["A"].width = 16
    ws.column_dimensions["B"].width = 10
    ws.column_dimensions["C"].width = 8
    ws.column_dimensions["D"].width = 18
    ws.freeze_panes = "A2"
    ws.auto_filter.ref = "A1:D%d" % (1 + len(ROWS))

    gender = DataValidation(
        type="list",
        formula1='"男,女"',
        allow_blank=True,
        showInputMessage=True,
        showErrorMessage=True,
        promptTitle="性别",
        prompt="请选择「男」或「女」，也可以留空。",
        errorTitle="性别填写有误",
        error="请填写「男」或「女」，也可以留空。",
    )
    ws.add_data_validation(gender)
    gender.add("C2:C500")

    ws2 = wb.create_sheet("填写说明")
    ws2.column_dimensions["A"].width = 20
    ws2.column_dimensions["B"].width = 96
    for c, text in enumerate(["项目", "说明"], 1):
        cell = ws2.cell(row=1, column=c, value=text)
        cell.fill = header_fill
        cell.font = header_font
        cell.border = border
        cell.alignment = Alignment(horizontal="center", vertical="center")
    for r, (key, value) in enumerate(NOTES, 2):
        a = ws2.cell(row=r, column=1, value=key)
        b = ws2.cell(row=r, column=2, value=value)
        a.font = Font(name="Microsoft YaHei", size=11, bold=True)
        b.font = body_font
        a.fill = PatternFill("solid", fgColor=SOFT)
        for cell in (a, b):
            cell.border = border
            cell.alignment = Alignment(vertical="center", wrap_text=True)
        ws2.row_dimensions[r].height = 36
    ws2.freeze_panes = "A2"

    wb.save(path)


def build_csv(path):
    with open(path, "w", encoding="utf-8-sig", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(HEADERS)
        writer.writerows(ROWS)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    xlsx = os.path.join(OUT_DIR, "roster-import-template.xlsx")
    csv_path = os.path.join(OUT_DIR, "roster-import-template.csv")
    build_xlsx(xlsx)
    build_csv(csv_path)
    print("written:", xlsx)
    print("written:", csv_path)


if __name__ == "__main__":
    main()
