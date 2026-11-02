// ================================================================
// RosterImport.cs — 名单导入：表头识别 / 列匹配 / 去重 / 合并 / 备份
//   · 支持 Excel / CSV 原始表格与 JSON 备份两种来源
//   · 导入前自动备份当前名单，替换与追加都不丢原数据
// ================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LuckyPickerWpf
{
    public enum RosterColumnKind { None, Name, Class, Gender, Reading }

    public sealed class RosterColumnMap
    {
        public int NameColumn = -1;
        public int ClassColumn = -1;
        public int GenderColumn = -1;
        public int ReadingColumn = -1;
        public int HeaderRow = -1;
        public string DefaultClassId = "1";
    }

    public sealed class RosterImportBuild
    {
        public List<Student> Accepted = new();
        public int DataRows;
        public int EmptyRows;
        public int EmptyNames;
        public int DuplicateInFile;
        public int DuplicateExisting;
        public List<string> Warnings = new();

        public int Skipped => EmptyRows + EmptyNames + DuplicateInFile + DuplicateExisting;
    }

    public sealed class RosterImportSelection
    {
        public DataFile Data = new();
        public bool Replace;
        public int ImportedCount;
        public int TotalCount;
        public RosterImportBuild Build = new();
    }

    public static class RosterImport
    {
        public const int MaxPreviewColumns = 12;

        public static List<List<string>>? ReadTable(string path)
        {
            if (path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) return XlsxReader.Read(path);
            if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return CsvReader.Read(path);
            return null;
        }

        public static int ColumnCount(List<List<string>> rows)
        {
            int cols = 0;
            foreach (var row in rows) cols = Math.Max(cols, row.Count);
            return cols;
        }

        // ---------- 表头识别 ----------
        public static RosterColumnKind ClassifyHeader(string? cell)
        {
            string h = NormalizeHeader(cell);
            if (h.Length == 0) return RosterColumnKind.None;
            if (h.Contains("读音") || h.Contains("朗读") || h.Contains("发音") || h.Contains("拼音")
                || h == "reading" || h.StartsWith("reading")) return RosterColumnKind.Reading;
            if (h.Contains("性别") || h == "gender" || h.StartsWith("gender") || h == "sex" || h.StartsWith("sex"))
                return RosterColumnKind.Gender;
            if (h.Contains("班级") || h.Contains("班别") || h == "班" || h == "class" || h.StartsWith("class"))
                return RosterColumnKind.Class;
            if (h.Contains("姓名") || h.Contains("名字") || h.Contains("名称") || h == "学生" || h == "name" || h.StartsWith("name"))
                return RosterColumnKind.Name;
            return RosterColumnKind.None;
        }

        public static int DetectHeaderRow(List<List<string>> rows)
        {
            int bestRow = -1, bestScore = 0;
            int limit = Math.Min(rows.Count, 12);
            for (int r = 0; r < limit; r++)
            {
                int recognized = 0;
                bool hasName = false;
                foreach (var cell in rows[r])
                {
                    var kind = ClassifyHeader(cell);
                    if (kind == RosterColumnKind.None) continue;
                    recognized++;
                    if (kind == RosterColumnKind.Name) hasName = true;
                }
                if (!hasName) continue;
                int score = recognized + 3;
                if (score > bestScore) { bestScore = score; bestRow = r; }
                if (recognized >= 2) break;
            }
            return bestRow;
        }

        public static RosterColumnMap Suggest(List<List<string>> rows, int headerRow)
        {
            int cols = ColumnCount(rows);
            var map = new RosterColumnMap { HeaderRow = headerRow };
            if (headerRow >= 0 && headerRow < rows.Count)
            {
                var header = rows[headerRow];
                for (int c = 0; c < header.Count; c++)
                {
                    switch (ClassifyHeader(header[c]))
                    {
                        case RosterColumnKind.Name when map.NameColumn < 0: map.NameColumn = c; break;
                        case RosterColumnKind.Class when map.ClassColumn < 0: map.ClassColumn = c; break;
                        case RosterColumnKind.Gender when map.GenderColumn < 0: map.GenderColumn = c; break;
                        case RosterColumnKind.Reading when map.ReadingColumn < 0: map.ReadingColumn = c; break;
                    }
                }
                if (map.NameColumn < 0) map.NameColumn = 0;
                return map;
            }

            map.NameColumn = GuessNameColumn(rows, cols);
            map.ClassColumn = GuessClassColumn(rows, cols, map.NameColumn);
            map.GenderColumn = GuessGenderColumn(rows, cols);
            return map;
        }

        static int GuessNameColumn(List<List<string>> rows, int cols)
        {
            int best = -1;
            double bestScore = 0;
            int sample = Math.Min(rows.Count, 30);
            for (int c = 0; c < cols; c++)
            {
                int hits = 0, total = 0;
                for (int r = 0; r < sample; r++)
                {
                    string v = CleanCell(Cell(rows[r], c));
                    if (v.Length == 0) continue;
                    total++;
                    if (LooksLikeName(v)) hits++;
                }
                if (total < 2) continue;
                double score = (double)hits / total;
                if (score >= 0.6 && score > bestScore) { bestScore = score; best = c; }
            }
            return best >= 0 ? best : 0;
        }

        static int GuessClassColumn(List<List<string>> rows, int cols, int nameColumn)
        {
            int best = -1;
            double bestScore = 0;
            int sample = Math.Min(rows.Count, 30);
            for (int c = 0; c < cols; c++)
            {
                if (c == nameColumn) continue;
                int hits = 0, total = 0, withSuffix = 0;
                for (int r = 0; r < sample; r++)
                {
                    string v = CleanCell(Cell(rows[r], c));
                    if (v.Length == 0) continue;
                    total++;
                    if (LooksLikeClass(v))
                    {
                        hits++;
                        if (v.Contains("班")) withSuffix++;
                    }
                }
                if (total < 2) continue;
                double score = (double)hits / total + Math.Min(0.3, withSuffix * 0.1);
                if (score >= 0.6 && score >= bestScore) { bestScore = score; best = c; }
            }
            return best;
        }

        static int GuessGenderColumn(List<List<string>> rows, int cols)
        {
            int best = -1;
            double bestScore = 0;
            int sample = Math.Min(rows.Count, 30);
            for (int c = 0; c < cols; c++)
            {
                int hits = 0, total = 0;
                for (int r = 0; r < sample; r++)
                {
                    string v = CleanCell(Cell(rows[r], c));
                    if (v.Length == 0) continue;
                    total++;
                    if (LooksLikeGenderValue(v)) hits++;
                }
                if (total < 2) continue;
                double score = (double)hits / total;
                if (score >= 0.6 && score > bestScore) { bestScore = score; best = c; }
            }
            return best;
        }

        static bool LooksLikeName(string v)
        {
            if (v.Length > 20 || v.All(char.IsDigit)) return false;
            if (v.EndsWith("班", StringComparison.Ordinal)) return false;
            if (LooksLikeGenderValue(v) || ClassifyHeader(v) != RosterColumnKind.None) return false;
            return v.Any(ch => (ch >= 0x4E00 && ch <= 0x9FFF) || char.IsLetter(ch));
        }

        static bool LooksLikeClass(string v)
        {
            if (Regex.IsMatch(v, @"^\d+\s*班?$")) return true;
            if (Regex.IsMatch(v, @"\d+\s*班")) return true;
            string numeral = v.EndsWith("班", StringComparison.Ordinal)
                ? v.Substring(0, v.Length - 1).Trim() : v;
            return Regex.IsMatch(numeral, @"^[零一二两三四五六七八九十]+$");
        }

        static bool LooksLikeGenderValue(string v)
        {
            string t = v.Trim().ToLowerInvariant();
            return t is "男" or "女生" or "男生" or "女" or "male" or "female" or "m" or "f";
        }

        // ---------- 构建导入结果 ----------
        public static RosterImportBuild Build(
            List<List<string>> rows, RosterColumnMap map, IEnumerable<Student>? existing = null)
        {
            var result = new RosterImportBuild();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existingKeys = existing == null
                ? null
                : new HashSet<string>(existing.Select(Key), StringComparer.OrdinalIgnoreCase);

            int start = map.HeaderRow >= 0 ? map.HeaderRow + 1 : 0;
            for (int r = start; r < rows.Count; r++)
            {
                result.DataRows++;
                var row = rows[r];
                bool blank = true;
                foreach (var cell in row)
                    if (CleanCell(cell).Length > 0) { blank = false; break; }
                if (blank) { result.EmptyRows++; continue; }

                string name = CleanCell(Cell(row, map.NameColumn));
                if (name.Length == 0)
                {
                    result.EmptyNames++;
                    result.Warnings.Add("第 " + (r + 1) + " 行：姓名为空，已跳过");
                    continue;
                }

                string classId = map.ClassColumn >= 0
                    ? ImportUtil.NormalizeClass(Cell(row, map.ClassColumn)) : "";
                if (classId.Length == 0) classId = map.DefaultClassId.Length > 0 ? map.DefaultClassId : "1";
                string gender = map.GenderColumn >= 0
                    ? ImportUtil.NormalizeGender(Cell(row, map.GenderColumn)) : "";
                string reading = map.ReadingColumn >= 0 ? CleanCell(Cell(row, map.ReadingColumn)) : "";
                if (string.Equals(reading, name, StringComparison.Ordinal)) reading = "";

                var student = new Student { name = name, classId = classId, gender = gender, reading = reading };
                string key = Key(student);
                if (!seen.Add(key))
                {
                    result.DuplicateInFile++;
                    result.Warnings.Add("第 " + (r + 1) + " 行：" + name + "（" + classId + "班）同班重名，已跳过");
                    continue;
                }
                if (existingKeys != null && existingKeys.Contains(key))
                {
                    result.DuplicateExisting++;
                    result.Warnings.Add("第 " + (r + 1) + " 行：" + name + "（" + classId + "班）已在名单中，已跳过");
                    continue;
                }
                result.Accepted.Add(student);
            }
            return result;
        }

        public static RosterImportBuild BuildFromDataFile(DataFile incoming, IEnumerable<Student>? existing = null)
        {
            var result = new RosterImportBuild();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existingKeys = existing == null
                ? null
                : new HashSet<string>(existing.Select(Key), StringComparer.OrdinalIgnoreCase);

            foreach (var raw in incoming.students ?? new List<Student>())
            {
                result.DataRows++;
                string name = CleanCell(raw.name);
                if (name.Length == 0) { result.EmptyNames++; continue; }
                string classId = ImportUtil.NormalizeClass(raw.classId);
                if (classId.Length == 0) classId = "1";
                string gender = ImportUtil.NormalizeGender(raw.gender);
                string reading = CleanCell(raw.reading);
                if (string.Equals(reading, name, StringComparison.Ordinal)) reading = "";

                var student = new Student
                {
                    name = name,
                    classId = classId,
                    gender = gender,
                    reading = reading,
                    blocked = raw.blocked
                };
                string key = Key(student);
                if (!seen.Add(key)) { result.DuplicateInFile++; continue; }
                if (existingKeys != null && existingKeys.Contains(key)) { result.DuplicateExisting++; continue; }
                result.Accepted.Add(student);
            }
            return result;
        }

        public static DataFile Merge(
            DataFile current, RosterImportBuild build, bool replace,
            IReadOnlyDictionary<string, string>? importedClasses = null)
        {
            var students = new List<Student>();
            if (!replace)
                foreach (var s in current.students ?? new List<Student>()) students.Add(Clone(s));
            foreach (var s in build.Accepted)
            {
                var copy = Clone(s);
                copy.classId = ImportUtil.NormalizeClass(copy.classId);
                if (copy.classId.Length == 0) copy.classId = "1";
                students.Add(copy);
            }

            var classes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var s in students)
            {
                string cid = s.classId;
                if (cid.Length == 0 || classes.ContainsKey(cid)) continue;
                if (importedClasses != null && importedClasses.TryGetValue(cid, out string? imported)
                    && !string.IsNullOrWhiteSpace(imported))
                    classes[cid] = imported;
                else if (current.classes != null && current.classes.TryGetValue(cid, out string? existingName)
                    && !string.IsNullOrWhiteSpace(existingName))
                    classes[cid] = existingName;
                else
                    classes[cid] = cid + "班";
            }
            return new DataFile { classes = classes, students = students };
        }

        // ---------- 备份 / 导出 ----------
        public static string? BackupCurrent(string reason)
        {
            try
            {
                string src = LuckyCore.DataPath();
                if (!File.Exists(src)) return null;
                string dir = Path.Combine(LuckyCore.DataDir(), "backups");
                Directory.CreateDirectory(dir);
                string dst = Path.Combine(dir,
                    "students-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + reason + ".json");
                File.Copy(src, dst, false);
                var old = new DirectoryInfo(dir).GetFiles("students-*.json")
                    .OrderByDescending(f => f.Name).Skip(20);
                foreach (var file in old) { try { file.Delete(); } catch { } }
                return dst;
            }
            catch (Exception ex)
            {
                App.Log("ROSTER-BACKUP-EX: " + ex);
                return null;
            }
        }

        public static void ExportCsv(string path, DataFile data)
        {
            var sb = new StringBuilder();
            sb.AppendLine("姓名,班级,性别,读音");
            var students = (data.students ?? new List<Student>())
                .OrderBy(s => ClassOrder(s.classId))
                .ThenBy(s => s.classId, StringComparer.Ordinal)
                .ThenBy(s => s.name, StringComparer.Ordinal);
            foreach (var s in students)
            {
                sb.Append(CsvCell(CleanCell(s.name))).Append(',')
                  .Append(CsvCell(s.classId)).Append(',')
                  .Append(CsvCell(GenderLabel(s.gender))).Append(',')
                  .Append(CsvCell(CleanCell(s.reading))).AppendLine();
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        static int ClassOrder(string classId) => int.TryParse(classId, out int n) ? n : int.MaxValue;

        static string GenderLabel(string gender) =>
            gender == "male" ? "男" : gender == "female" ? "女" : "";

        static string CsvCell(string value)
        {
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        public static string CleanCell(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var sb = new StringBuilder(value.Length);
            foreach (char ch in value)
            {
                if (ch is '\u200B' or '\u200C' or '\u200D' or '\uFEFF') continue;
                sb.Append(ch is '\r' or '\n' or '\t' ? ' ' : ch);
            }
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        public static string Key(Student s) =>
            ImportUtil.NormalizeClass(s.classId) + "\u0001" + CleanCell(s.name);

        static Student Clone(Student s) => new()
        {
            name = s.name,
            classId = s.classId,
            gender = s.gender,
            reading = s.reading,
            blocked = s.blocked
        };

        static string Cell(List<string> row, int index) =>
            index >= 0 && index < row.Count ? row[index] ?? "" : "";

        static string NormalizeHeader(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var sb = new StringBuilder(value.Length);
            int depth = 0;
            foreach (char ch in value.Trim())
            {
                if (ch is '(' or '（' or '[' or '【') { depth++; continue; }
                if (ch is ')' or '）' or ']' or '】') { if (depth > 0) depth--; continue; }
                if (depth > 0) continue;
                if (char.IsWhiteSpace(ch) || ch is '*' or ':' or '：' or '-' or '_') continue;
                sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }

        // ---------- 自检 ----------
        internal static bool SelfTest(out string detail)
        {
            var fail = new List<string>();
            void Check(bool ok, string name) { if (!ok) fail.Add(name); }

            Check(ImportUtil.NormalizeClass("19班") == "19", "班级 19班");
            Check(ImportUtil.NormalizeClass("高一(3)班") == "3", "班级 高一(3)班");
            Check(ImportUtil.NormalizeClass("2026级3班") == "3", "班级 2026级3班");
            Check(ImportUtil.NormalizeClass("三班") == "3", "班级 三班");
            Check(ImportUtil.NormalizeClass("二十三班") == "23", "班级 二十三班");
            Check(ImportUtil.NormalizeClass("３班") == "3", "班级 全角３班");
            Check(ImportUtil.NormalizeClass("3.0") == "3", "班级 Excel 3.0");
            Check(ImportUtil.NormalizeGender("男生") == "male" && ImportUtil.NormalizeGender("女生") == "female", "性别归一化");
            Check(CleanCell(" 张\u200B伟 ") == "张伟", "零宽字符清理");

            var rows = new List<List<string>>
            {
                new() { "高一年级名单" },
                new() { "序号", "姓名", "性别", "班级", "读音" },
                new() { "1", "张伟", "男", "3班", "" },
                new() { "2", "王芳", "女", "3班", "" },
                new() { "", "", "", "", "" },
                new() { "3", "张伟", "男", "3班", "" },
                new() { "4", "曾露", "女", "3班", "增露" }
            };
            int header = DetectHeaderRow(rows);
            Check(header == 1, "表头跳过标题行");
            var map = Suggest(rows, header);
            Check(map.NameColumn == 1 && map.ClassColumn == 3 && map.GenderColumn == 2 && map.ReadingColumn == 4,
                "列自动匹配");

            var build = Build(rows, map);
            Check(build.DataRows == 5 && build.Accepted.Count == 3 && build.EmptyRows == 1 && build.DuplicateInFile == 1,
                "导入统计（空行/同班重名）");
            Check(build.Accepted.Any(s => s.name == "曾露" && s.reading == "增露" && s.classId == "3"),
                "读音列导入");

            var current = new DataFile
            {
                classes = new() { { "3", "示例三班" } },
                students = new() { new Student { name = "张伟", classId = "3", gender = "male" } }
            };
            var append = Build(rows, map, current.students);
            Check(append.Accepted.Count == 2 && append.DuplicateExisting == 1, "追加时跳过已存在学生");
            var merged = Merge(current, append, false);
            Check(merged.students.Count == 3 && merged.classes["3"] == "示例三班", "追加合并保留班级名");

            var json = new DataFile
            {
                classes = new() { { "5", "示例五班" } },
                students = new()
                {
                    new Student { name = " 曾露 ", classId = "5班", gender = "女生", reading = "增露" }
                }
            };
            var fromJson = BuildFromDataFile(json);
            var restored = Merge(new DataFile(), fromJson, true, json.classes);
            Check(restored.students.Count == 1
                && restored.students[0].classId == "5"
                && restored.students[0].gender == "female"
                && restored.students[0].reading == "增露"
                && restored.classes["5"] == "示例五班", "JSON 备份恢复");

            string csv = Path.Combine(Path.GetTempPath(), "lucky-roster-" + Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                ExportCsv(csv, restored);
                var back = CsvReader.Read(csv);
                Check(back.Count == 2 && back[0][0] == "姓名" && back[1][3] == "增露", "CSV 导出可回读");
            }
            catch (Exception ex) { Check(false, "CSV 导出异常：" + ex.Message); }
            finally { try { File.Delete(csv); } catch { } }

            bool templateOk = RosterTemplate.SelfTest(out string templateDetail);
            Check(templateOk, "模板：" + templateDetail);

            detail = fail.Count == 0
                ? "表头/列匹配/班级归一化/去重/JSON/CSV/模板均通过"
                : string.Join("；", fail);
            return fail.Count == 0;
        }
    }
}
