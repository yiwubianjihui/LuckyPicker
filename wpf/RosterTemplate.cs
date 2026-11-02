// ================================================================
// RosterTemplate.cs — 内置名单导入模板（Excel / CSV）
//   模板文件由 tools/make_roster_template.py 生成，构建时嵌入程序。
// ================================================================
using System;
using System.IO;
using System.Reflection;
using System.Windows;

namespace LuckyPickerWpf
{
    public static class RosterTemplate
    {
        const string XlsxResource = "LuckyPickerWpf.RosterTemplate.xlsx";
        const string CsvResource = "LuckyPickerWpf.RosterTemplate.csv";

        public const string DefaultXlsxName = "YBH幸运摇人器-名单导入模板.xlsx";
        public const string DefaultCsvName = "YBH幸运摇人器-名单导入模板.csv";

        public static void Save(string path)
        {
            bool csv = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
            using var input = Open(csv ? CsvResource : XlsxResource);
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }

        static Stream Open(string name)
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (stream == null) throw new InvalidOperationException("内置名单模板缺失，请重新安装程序。");
            return stream;
        }

        public static bool SaveInteractive(Window owner)
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "保存名单导入模板",
                Filter = "Excel 模板 (*.xlsx)|*.xlsx|CSV 模板 (*.csv)|*.csv",
                FilterIndex = 1,
                FileName = DefaultXlsxName,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                AddExtension = true,
                OverwritePrompt = true
            };
            if (sfd.ShowDialog(owner) != true) return false;

            bool wantCsv = sfd.FilterIndex == 2;
            string target = sfd.FileName;
            string ext = Path.GetExtension(target);
            if (wantCsv && !ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
                target = Path.ChangeExtension(target, ".csv");
            else if (!wantCsv && !ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
                target = Path.ChangeExtension(target, ".xlsx");

            if (File.Exists(target) &&
                MessageBox.Show(owner, "文件已存在，是否覆盖？\n" + target, "保存模板",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;

            try
            {
                Save(target);
                MessageBox.Show(owner,
                    "模板已保存到：\n" + target + "\n\n按「填写说明」填好后，回到这里选择该文件即可导入。",
                    "导入模板", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "模板保存失败：" + ex.Message, "导入模板",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        internal static bool SelfTest(out string detail)
        {
            string dir = Path.Combine(Path.GetTempPath(), "lucky-template-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                string xlsx = Path.Combine(dir, "template.xlsx");
                string csv = Path.Combine(dir, "template.csv");
                Save(xlsx);
                Save(csv);
                var xrows = XlsxReader.Read(xlsx);
                var crows = CsvReader.Read(csv);
                bool ok = xrows != null && xrows.Count >= 6
                    && xrows[0].Count >= 4
                    && xrows[0][0] == "姓名" && xrows[0][1] == "班级" && xrows[0][2] == "性别" && xrows[0][3] == "读音"
                    && xrows.Exists(r => r.Count >= 4 && r[0] == "曾露" && r[3] == "增露")
                    && crows.Count >= 6
                    && crows[0][0] == "姓名"
                    && crows.Exists(r => r.Count >= 4 && r[0] == "曾露" && r[3] == "增露");
                detail = ok ? "内置 Excel/CSV 模板可读，示例与读音列完整" : "模板内容或资源缺失";
                return ok;
            }
            catch (Exception ex) { detail = ex.Message; return false; }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
