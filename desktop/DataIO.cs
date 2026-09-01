// ================================================================
// DataIO.cs — 名单导入解析
//   1) XlsxReader：原生解析 .xlsx（ZIP + OOXML，无第三方依赖）
//   2) CsvReader ：CSV 解析，自动识别 UTF-8 / GBK 编码，支持引号转义
//   3) ImportUtil：班级号/性别规范化、表头列自动识别
// ================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace LuckyPicker
{
    public static class XlsxReader
    {
        static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        static readonly XNamespace PkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        // 读取第一个工作表的全部数据行（字符串二维表），失败返回 null
        public static List<List<string>> Read(string path)
        {
            try
            {
                using (var zip = new ZipArchive(File.OpenRead(path), ZipArchiveMode.Read))
                {
                    var shared = new List<string>();
                    var ss = zip.GetEntry("xl/sharedStrings.xml");
                    if (ss != null)
                    {
                        using (var s = ss.Open())
                        {
                            var doc = XDocument.Load(s);
                            foreach (var si in doc.Root.Elements(MainNs + "si"))
                            {
                                var sb = new StringBuilder();
                                foreach (var t in si.Descendants(MainNs + "t")) sb.Append(t.Value);
                                shared.Add(sb.ToString());
                            }
                        }
                    }

                    var wbEntry = zip.GetEntry("xl/workbook.xml");
                    if (wbEntry == null) return null;
                    XDocument wb;
                    using (var s = wbEntry.Open()) wb = XDocument.Load(s);
                    var sheet = wb.Root.Elements(MainNs + "sheets").Elements(MainNs + "sheet").FirstOrDefault();
                    if (sheet == null) return null;
                    var rid = (string)sheet.Attribute(RelNs + "id");
                    if (rid == null) return null;

                    string target = null;
                    var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
                    if (relsEntry != null)
                    {
                        using (var s = relsEntry.Open())
                        {
                            var rels = XDocument.Load(s);
                            var rel = rels.Root.Elements(PkgRelNs + "Relationship")
                                .FirstOrDefault(r => (string)r.Attribute("Id") == rid);
                            if (rel != null) target = (string)rel.Attribute("Target");
                        }
                    }
                    if (target == null) return null;
                    string sheetPath = target.StartsWith("/") ? target.TrimStart('/') : "xl/" + target;
                    var sheetEntry = zip.GetEntry(sheetPath);
                    if (sheetEntry == null) return null;

                    var rows = new List<List<string>>();
                    using (var s = sheetEntry.Open())
                    {
                        var doc = XDocument.Load(s);
                        var sheetData = doc.Root.Element(MainNs + "sheetData");
                        if (sheetData == null) return null;
                        foreach (var row in sheetData.Elements(MainNs + "row"))
                        {
                            var cells = new List<string>();
                            foreach (var c in row.Elements(MainNs + "c"))
                            {
                                int idx = ColIndex((string)c.Attribute("r"));
                                if (idx < 0) continue;
                                string type = (string)c.Attribute("t");
                                string val = "";
                                var v = c.Element(MainNs + "v");
                                if (type == "s" && v != null)
                                {
                                    int i;
                                    if (int.TryParse(v.Value, out i) && i >= 0 && i < shared.Count) val = shared[i];
                                }
                                else if (type == "inlineStr")
                                {
                                    var sb = new StringBuilder();
                                    foreach (var t in c.Descendants(MainNs + "t")) sb.Append(t.Value);
                                    val = sb.ToString();
                                }
                                else if (v != null) val = v.Value;
                                while (cells.Count <= idx) cells.Add("");
                                cells[idx] = val;
                            }
                            if (cells.Any(x => x != null && x.Length > 0)) rows.Add(cells);
                        }
                    }
                    return rows;
                }
            }
            catch { return null; }
        }

        static int ColIndex(string refAttr)
        {
            if (string.IsNullOrEmpty(refAttr)) return -1;
            int col = 0;
            foreach (char ch in refAttr)
            {
                if (ch >= 'A' && ch <= 'Z') col = col * 26 + (ch - 'A' + 1);
                else if (ch >= 'a' && ch <= 'z') col = col * 26 + (ch - 'a' + 1);
                else break;
            }
            return col - 1;
        }
    }

    public static class CsvReader
    {
        const char CR = (char)13;
        const char LF = (char)10;

        public static List<List<string>> Read(string path)
        {
            byte[] raw = File.ReadAllBytes(path);
            string text = Decode(raw);
            var rows = new List<List<string>>();
            var row = new List<string>();
            var sb = new StringBuilder();
            bool inQ = false;
            int i = 0;
            while (i < text.Length)
            {
                char ch = text[i];
                if (inQ)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                        inQ = false; i++; continue;
                    }
                    sb.Append(ch); i++; continue;
                }
                if (ch == '"') { inQ = true; i++; continue; }
                if (ch == ',') { row.Add(sb.ToString()); sb.Clear(); i++; continue; }
                if (ch == CR || ch == LF)
                {
                    if (ch == CR && i + 1 < text.Length && text[i + 1] == LF) i++;
                    i++;
                    row.Add(sb.ToString()); sb.Clear();
                    if (row.Any(x => x != null && x.Length > 0)) rows.Add(row);
                    row = new List<string>();
                    continue;
                }
                sb.Append(ch); i++;
            }
            if (sb.Length > 0 || row.Count > 0)
            {
                row.Add(sb.ToString());
                rows.Add(row);
            }
            return rows;
        }

        // 编码探测：BOM 优先，其次严格 UTF-8，失败回退 GBK(936)
        static string Decode(byte[] raw)
        {
            if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
                return Encoding.UTF8.GetString(raw, 3, raw.Length - 3);
            if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE)
                return Encoding.Unicode.GetString(raw, 2, raw.Length - 2);
            if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(raw, 2, raw.Length - 2);
            try
            {
                var strict = new UTF8Encoding(false, true);
                return strict.GetString(raw);
            }
            catch (DecoderFallbackException)
            {
                try { return Encoding.GetEncoding(936).GetString(raw); }
                catch { return Encoding.Default.GetString(raw); }
            }
        }
    }

    public static class ImportUtil
    {
        // 规范化班级号：优先提取连续数字（"19班" -> "19"），否则保留原文
        public static string NormalizeClass(string s)
        {
            if (s == null) return "";
            s = s.Trim();
            if (s.Length == 0) return "";
            var digits = new StringBuilder();
            foreach (char ch in s)
                if (ch >= '0' && ch <= '9') digits.Append(ch);
            if (digits.Length > 0) return digits.ToString();
            return s;
        }

        public static string NormalizeGender(string s)
        {
            if (s == null) return "";
            if (s.Contains("男")) return "男";
            if (s.Contains("女")) return "女";
            return "";
        }

        // 在首行中查找包含关键字的列
        public static int FindColumn(List<List<string>> rows, params string[] keys)
        {
            if (rows == null || rows.Count == 0) return -1;
            var first = rows[0];
            for (int c = 0; c < first.Count; c++)
            {
                string v = (first[c] ?? "").Trim();
                foreach (var k in keys)
                    if (v.Length > 0 && v.Contains(k)) return c;
            }
            return -1;
        }
    }
}
