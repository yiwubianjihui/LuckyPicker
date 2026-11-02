// ================================================================
// DataIO.cs — 名单导入解析
//   1) XlsxReader：原生解析 .xlsx（ZIP + OOXML，无第三方依赖）
//   2) CsvReader ：CSV 解析，自动识别 UTF-8 / GBK 编码，支持引号转义
//   3) ImportUtil：班级号/性别规范化、表头列自动识别
// ================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LuckyPickerWpf
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
        // 规范化班级号：优先提取「班」前的数字（"19班" -> "19"），
        // 支持 "高一(3)班"、"2026级3班"、"三班"、"３班" 与 Excel 的 "3.0"。
        public static string NormalizeClass(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s.Trim())
                sb.Append(ch >= '０' && ch <= '９' ? (char)('0' + (ch - '０')) : ch);
            string text = sb.ToString().Trim();
            if (text.Length == 0) return "";

            if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal dec)
                && dec == decimal.Truncate(dec) && dec >= 0 && dec <= 999999)
                return ((long)dec).ToString(CultureInfo.InvariantCulture);

            var beforeClass = Regex.Match(text, @"(\d+)\s*班");
            if (beforeClass.Success) return TrimLeadingZeros(beforeClass.Groups[1].Value);

            string numeral = text.EndsWith("班", StringComparison.Ordinal)
                ? text.Substring(0, text.Length - 1).Trim() : text;
            if (TryChineseNumber(numeral, out int chinese)) return chinese.ToString(CultureInfo.InvariantCulture);

            var matches = Regex.Matches(text, @"\d+");
            if (matches.Count > 0) return TrimLeadingZeros(matches[matches.Count - 1].Value);
            return text;
        }

        static string TrimLeadingZeros(string digits)
        {
            string trimmed = digits.TrimStart('0');
            return trimmed.Length == 0 ? "0" : trimmed;
        }

        static bool TryChineseNumber(string s, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(s) || s.Length > 3) return false;
            var digits = new Dictionary<char, int>
            {
                { '零', 0 }, { '一', 1 }, { '二', 2 }, { '两', 2 }, { '三', 3 }, { '四', 4 },
                { '五', 5 }, { '六', 6 }, { '七', 7 }, { '八', 8 }, { '九', 9 }
            };
            if (s.Length == 1)
            {
                if (s[0] == '十') { value = 10; return true; }
                if (digits.TryGetValue(s[0], out int single)) { value = single; return true; }
                return false;
            }
            int ten = s.IndexOf('十');
            if (ten < 0 || s.LastIndexOf('十') != ten) return false;
            int tens = 1;
            if (ten > 0)
            {
                if (!digits.TryGetValue(s[0], out tens) || ten != 1) return false;
            }
            int ones = 0;
            string tail = s.Substring(ten + 1);
            if (tail.Length > 0)
            {
                if (tail.Length != 1 || !digits.TryGetValue(tail[0], out ones)) return false;
            }
            value = tens * 10 + ones;
            return value > 0 && value <= 99;
        }

        public static string NormalizeGender(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            string text = s.Trim();
            string lower = text.ToLowerInvariant();
            if (lower is "男" or "男生" or "male" or "m" or "boy" or "man") return "male";
            if (lower is "女" or "女生" or "female" or "f" or "girl" or "woman") return "female";
            if (text.Contains("男")) return "male";
            if (text.Contains("女")) return "female";
            return "";
        }
    }
}
