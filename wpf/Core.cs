// ================================================================
// Core.cs — 抽取核心逻辑（WinUI 版复用，无 UI 依赖）
//
//   · Student / DataFile     名单数据结构
//   · LuckyCore              候选池 / 抽取 / 屏蔽 / 不重复模式
//   · HistoryStore           抽选记录持久化（%LOCALAPPDATA%\LuckyPicker）
// ================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LuckyPickerWpf
{
    public class Student
    {
        public string name { get; set; } = "";
        public string classId { get; set; } = "";
        public string gender { get; set; } = "";   // "" | male | female
        public bool blocked { get; set; } = false; // 屏蔽（临时）
        public string reading { get; set; } = "";  // 朗读覆盖（同音字），空则按 name 朗读
    }

    public class DataFile
    {
        public Dictionary<string, string> classes { get; set; } = new();
        public List<Student> students { get; set; } = new();
    }

    public static class JsonStorage
    {
        public static void WriteAtomic(string path, string json)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }

    public class LuckyCore
    {
        public List<Student> allStudents = new();
        public Dictionary<string, string> classNames = new();
        public List<string> classIds = new();
        public string currentClassId = "";
        public string genderFilter = "all";     // all | male | female
        public bool noRepeat = true;
        public List<string> blockNames = new(); // 屏蔽名单
        public List<Student> remainPool = new();
        public Student? lastPicked;
        public List<Student> lastMulti = new();

        Random rnd = new();

        // ---------- 名单数据目录（与 WinForms 版一致：%ProgramData%\LuckyPicker） ----------
        public static string DataDir()
        {
            try
            {
                string pd = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "LuckyPicker");
                Directory.CreateDirectory(pd);
                return pd;
            }
            catch { return AppContext.BaseDirectory; }
        }

        public static string DataPath() => Path.Combine(DataDir(), "students.json");

        public static DataFile LoadDataFile() => LoadDataFile(out _);

        public static DataFile LoadDataFile(out string? error)
        {
            error = null;
            string pdPath = DataPath();
            string path = File.Exists(pdPath) ? pdPath : Path.Combine(AppContext.BaseDirectory, "students.json");
            if (!File.Exists(path)) return new DataFile();
            try
            {
                string json = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<DataFile>(json) ?? throw new JsonException("名单文件内容为空。");
                if (path != pdPath)
                {
                    try { JsonStorage.WriteAtomic(pdPath, json); }
                    catch (Exception ex) { App.Log("ROSTER-COPY-EX: " + ex); }
                }
                return data;
            }
            catch (Exception ex)
            {
                error = "名单读取失败：" + ex.Message + "\n原文件未删除；请通过名单管理重新导入或恢复备份。";
                App.Log("ROSTER-LOAD-EX: " + ex);
                try
                {
                    string backup = Path.Combine(DataDir(), "students.corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                    File.Copy(path, backup, false);
                }
                catch (Exception backupEx) { App.Log("ROSTER-BACKUP-EX: " + backupEx); }
                return new DataFile();
            }
        }

        public static void SaveDataFile(DataFile data)
        {
            JsonStorage.WriteAtomic(DataPath(), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static string NormalizeGender(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string gender = value.Trim();
            if (gender == "男" || string.Equals(gender, "male", StringComparison.OrdinalIgnoreCase)) return "male";
            if (gender == "女" || string.Equals(gender, "female", StringComparison.OrdinalIgnoreCase)) return "female";
            return "";
        }

        public void Load(DataFile data)
        {
            allStudents = data.students ?? new List<Student>();
            foreach (var student in allStudents) student.gender = NormalizeGender(student.gender);
            classNames = data.classes ?? new Dictionary<string, string>();
            classIds = BuildClassIds();
            if (classIds.Count == 0) classIds.Add("1");
            if (!classIds.Contains(currentClassId)) currentClassId = classIds[0];
            ResetPool();
        }

        public void Reload()
        {
            Load(LoadDataFile());
        }

        List<string> BuildClassIds()
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var s in allStudents)
                if (!string.IsNullOrEmpty(s.classId)) set.Add(s.classId);
            foreach (var k in classNames.Keys) set.Add(k);
            return set.ToList();
        }

        public string ClassName(string id)
        {
            return classNames != null && classNames.ContainsKey(id) ? classNames[id] : id + "班";
        }

        public void SetClass(string id)
        {
            if (classIds.Contains(id)) currentClassId = id;
            ResetPool();
        }

        public void SetGender(string g)
        {
            genderFilter = g;
            ResetPool();
        }

        public List<Student> GetCandidates()
        {
            var list = allStudents
                .Where(s => s.classId == currentClassId)
                .Where(s => !s.blocked)
                .Where(s => !blockNames.Contains(s.name))
                .Where(s => genderFilter == "all" ||
                            (genderFilter == "male" && s.gender == "male") ||
                            (genderFilter == "female" && s.gender == "female"))
                .ToList();
            return list;
        }

        List<Student> ShuffledCandidates()
        {
            var pool = GetCandidates();
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            return pool;
        }

        public void ResetPool()
        {
            remainPool = ShuffledCandidates();
            lastPicked = null;
            lastMulti = new List<Student>();
        }

        public void ResetPoolKeepLast()
        {
            remainPool = ShuffledCandidates();
        }

        // 抽一人：返回 null 表示无可抽取（候选为空）
        public Student? PickOne()
        {
            var cands = GetCandidates();
            if (cands.Count == 0)
            {
                lastPicked = null;
                lastMulti = new List<Student>();
                return null;
            }
            Student? picked;
            if (noRepeat)
            {
                if (remainPool.Count == 0) remainPool = ShuffledCandidates();
                int idx = rnd.Next(remainPool.Count);
                picked = remainPool[idx];
                remainPool.RemoveAt(idx);
            }
            else
            {
                picked = cands[rnd.Next(cands.Count)];
            }
            lastPicked = picked;
            lastMulti = new List<Student>();
            return picked;
        }

        public List<Student> PickMulti(int n)
        {
            var result = new List<Student>();
            var cands = GetCandidates();
            if (cands.Count == 0) return result;
            if (noRepeat)
            {
                var pool = remainPool.Count > 0 ? remainPool : ShuffledCandidates();
                for (int i = pool.Count - 1; i > 0; i--)
                {
                    int j = rnd.Next(i + 1);
                    (pool[i], pool[j]) = (pool[j], pool[i]);
                }
                var picked = pool.Take(Math.Min(n, pool.Count)).ToList();
                foreach (var p in picked) { result.Add(p); pool.Remove(p); }
                remainPool = pool;
            }
            else
            {
                for (int i = 0; i < n; i++) result.Add(cands[rnd.Next(cands.Count)]);
            }
            lastMulti = result;
            lastPicked = null;
            return result;
        }

        public void AddBlock(string name)
        {
            if (!string.IsNullOrWhiteSpace(name) && !blockNames.Contains(name))
                blockNames.Add(name);
        }

        public void RemoveBlock(string name) => blockNames.Remove(name);
    }

    // ---------------- 抽选记录持久化 ----------------
    public static class HistoryStore
    {
        public static string Dir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LuckyPicker");
            }
        }

        public static string Path_ => Path.Combine(Dir, "history.json");

        public static string? LastError { get; private set; }

        public static List<HistoryEntry> Load()
        {
            LastError = null;
            try
            {
                if (!File.Exists(Path_)) return new List<HistoryEntry>();
                return JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(Path_)) ?? new List<HistoryEntry>();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                App.Log("HISTORY-LOAD-EX: " + ex);
                return new List<HistoryEntry>();
            }
        }

        public static bool Add(HistoryEntry entry)
        {
            var list = Load();
            if (LastError != null) return false;
            try
            {
                list.Insert(0, entry);
                if (list.Count > 500) list = list.Take(500).ToList();
                JsonStorage.WriteAtomic(Path_, JsonSerializer.Serialize(list));
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                App.Log("HISTORY-SAVE-EX: " + ex);
                return false;
            }
        }

        public static void Clear()
        {
            try { if (File.Exists(Path_)) File.Delete(Path_); LastError = null; }
            catch (Exception ex) { LastError = ex.Message; App.Log("HISTORY-CLEAR-EX: " + ex); }
        }
    }

    public class HistoryEntry
    {
        public string time { get; set; } = "";
        public string text { get; set; } = "";
        public string classId { get; set; } = "";
    }
}
