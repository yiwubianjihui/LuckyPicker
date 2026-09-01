// 离线单元测试：CSV/XLSX 解析、WAV 组装、SSML/URL 构造、JSON 序列化
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using LuckyPicker;

class Harness
{
    static int failures = 0;
    static void Check(string name, bool ok)
    {
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name);
        if (!ok) failures++;
    }

    static void Main()
    {
        string tmp = Path.GetTempPath();

        // ---------- CSV (GBK 无 BOM) ----------
        string csv1 = Path.Combine(tmp, "t_roster_gbk.csv");
        File.WriteAllBytes(csv1, Encoding.GetEncoding(936).GetBytes("姓名,班级,性别\r\n张三,19,男\r\n李四,18,女\r\n"));
        var rows = CsvReader.Read(csv1);
        Check("csv gbk rows", rows != null && rows.Count == 3);
        Check("csv gbk zh", rows != null && rows.Count == 3 && rows[1][0] == "张三" && rows[1][1] == "19" && rows[1][2] == "男");
        Check("csv detect cols", ImportUtil.FindColumn(rows, "姓名") == 0 && ImportUtil.FindColumn(rows, "性别") == 2);
        Check("csv normalize", ImportUtil.NormalizeClass("19班") == "19" && ImportUtil.NormalizeClass(" 20 ") == "20" && ImportUtil.NormalizeGender("男生") == "男" && ImportUtil.NormalizeGender("") == "");

        // ---------- CSV (UTF-8 BOM) ----------
        string csv2 = Path.Combine(tmp, "t_roster_utf8.csv");
        File.WriteAllText(csv2, "名字,班级\r\n王五,20\r\n", new UTF8Encoding(true));
        var rows2 = CsvReader.Read(csv2);
        Check("csv utf8 bom", rows2 != null && rows2.Count == 2 && rows2[1][0] == "王五" && rows2[1][1] == "20");

        // ---------- CSV 带引号 ----------
        string csv3 = Path.Combine(tmp, "t_quoted.csv");
        File.WriteAllText(csv3, "姓名,备注\r\n\"赵,六\",备注甲\r\n", new UTF8Encoding(true));
        var rows3 = CsvReader.Read(csv3);
        Check("csv quotes", rows3 != null && rows3.Count == 2 && rows3[1][0] == "赵,六" && rows3[1][1] == "备注甲");

        // ---------- XLSX ----------
        string xlsx = Path.Combine(tmp, "t_roster.xlsx");
        CreateXlsx(xlsx);
        var xrows = XlsxReader.Read(xlsx);
        Check("xlsx rows", xrows != null && xrows.Count == 3);
        if (xrows != null && xrows.Count >= 2)
        {
            Check("xlsx header+data", xrows[0][0] == "姓名" && xrows[1][0] == "张三" && xrows[1][1] == "19" && xrows[1][2] == "男");
            Check("xlsx inlineStr", xrows.Count == 3 && xrows[2][0] == "李四" && xrows[2][1] == "18" && xrows[2][2] == "女");
        }

        // ---------- WAV 组装 ----------
        var c1 = MakeRiffChunk(24000, 16, 1, 100);
        var c2 = MakeRiffChunk(24000, 16, 1, 50);
        var wav = EdgeTts.AssembleWav(new List<byte[]> { c1, c2 });
        Check("wav riff size", wav != null && wav.Length == 44 + 150 && wav[44] == 0 && wav[44 + 99] == 99 && wav[44 + 149] == 49);
        if (wav != null)
            Check("wav header fields", ReadU32(wav, 24) == 24000 && ReadU16(wav, 22) == 1 && ReadU16(wav, 34) == 16 && ReadU32(wav, 40) == 150);
        var raw = new byte[80];
        for (int i = 0; i < 80; i++) raw[i] = (byte)i;
        var wav2 = EdgeTts.AssembleWav(new List<byte[]> { raw });
        Check("wav raw pcm", wav2 != null && ReadU32(wav2, 24) == 24000 && ReadU32(wav2, 40) == 80 && wav2[44] == 0);
        Check("wav empty", EdgeTts.AssembleWav(new List<byte[]>()) == null);

        // ---------- Edge 协议构造 ----------
        string url = EdgeTts.BuildUrl();
        Check("url token+version", url.Contains("TrustedClientToken=6A5AA1D4EAFF4E9FB37E23D68491D6F4") && url.Contains("Sec-MS-GEC-Version=1-130.0.2849.68") && url.Contains("ConnectionId="));
        string ssml = EdgeTts.BuildSsml("张<三>&", "zh-CN-XiaoxiaoNeural");
        Check("ssml escape", ssml.Contains("&lt;") && ssml.Contains("&amp;") && ssml.Contains("zh-CN-XiaoxiaoNeural") && ssml.Contains("<speak"));

        // ---------- 版本与更新接口（随 AppVersion 自适应，升版无需再改） ----------
        int curBuild = AppVersion.Build;
        string nextVer = "26H2 Build " + (curBuild + 1);
        string oldVer = "26H2 Build " + (curBuild - 1);
        Check("app version", AppVersion.ProductName == "YBH幸运摇人器" &&
            AppVersion.Display == "26H2 Build " + curBuild && AppVersion.Build == curBuild);
        Check("extract last int", UpdateManager.ExtractLastInt("26H2 Build 12") == 12 && UpdateManager.ExtractLastInt("v3.2.9") == 9 && UpdateManager.ExtractLastInt("abc") == 0);

        var ur1 = UpdateManager.ParseJson(
            "{\"version\":\"" + nextVer + "\",\"build\":" + (curBuild + 1) + ",\"channel\":\"26H2\",\"url\":\"https://example.com/dl.zip\",\"notes\":\"新增功能\",\"releaseDate\":\"2025-06-01\",\"mandatory\":false}");
        Check("update parse new", ur1.Success && ur1.HasUpdate && ur1.LatestVersion == nextVer && ur1.LatestBuild == curBuild + 1 && ur1.DownloadUrl == "https://example.com/dl.zip" && ur1.Notes == "新增功能" && !ur1.Mandatory);
        Check("update parse same", !UpdateManager.ParseJson("{\"version\":\"26H2 Build " + curBuild + "\",\"build\":" + curBuild + "}").HasUpdate);
        Check("update parse older", !UpdateManager.ParseJson("{\"version\":\"" + oldVer + "\",\"build\":" + (curBuild - 1) + "}").HasUpdate);
        Check("update parse version-only", UpdateManager.ParseJson("{\"version\":\"26H2 Build " + (curBuild + 2) + "\"}").HasUpdate);
        Check("update parse missing version", !UpdateManager.ParseJson("{}").Success);

        string updateFile = Path.Combine(tmp, "t_update.json");
        File.WriteAllText(updateFile,
            "{\"product\":\"YBH幸运摇人器\",\"version\":\"" + nextVer + "\",\"build\":" + (curBuild + 1) + ",\"url\":\"https://example.com/dl.zip\",\"notes\":\"1.测试\\n2.测试\"}",
            new UTF8Encoding(false));
        var urLocal = UpdateManager.Check(updateFile);
        Check("update local file", urLocal.Success && urLocal.HasUpdate && urLocal.LatestVersion == nextVer);

        // ---------- 网站 JS 挑战 cookie 求解 ----------
        string challengeHtml =
            "<html><body><script type=\"text/javascript\" src=\"/aes.js\"></script><script>" +
            "var a=toNumbers(\"f655ba9d09a112d4968c63579db590b4\")," +
            "b=toNumbers(\"98344c2eee86c3994890592585b49f80\")," +
            "c=toNumbers(\"5a7e50682dac6bf578f25f0b456c405a\");" +
            "document.cookie=\"__test=\"+toHex(slowAES.decrypt(c,2,a,b))" +
            "</script></body></html>";
        Check("update js challenge solve",
            UpdateManager.SolveChallengeCookie(challengeHtml) == "__test=6eb6075c8b58e823ae46ff6b6949b4b8");
        Check("update normalize url",
            UpdateManager.NormalizeHttpUrl("https://x.cn//a//b.zip?i=1") == "https://x.cn/a/b.zip?i=1" &&
            UpdateManager.NormalizeHttpUrl("https://x.cn/a/b.zip") == "https://x.cn/a/b.zip");

        // ---------- JSON 序列化往返 ----------
        var ser = new JavaScriptSerializer();
        var df = new DataFile
        {
            classes = new Dictionary<string, string> { { "19", "示例十九班" } },
            students = new List<Student> { new Student { name = "张三", classId = "19", gender = "男" } }
        };
        string json = ser.Serialize(df);
        var df2 = ser.Deserialize<DataFile>(json);
        Check("json roundtrip", df2 != null && df2.students.Count == 1 && df2.students[0].name == "张三" && df2.classes["19"] == "示例十九班");

        Console.WriteLine("----------------------------");
        Console.WriteLine(failures == 0 ? "ALL PASS" : (failures + " FAILURES"));
        Environment.Exit(failures == 0 ? 0 : 1);
    }

    static byte[] MakeRiffChunk(int rate, int bits, int channels, int dataLen)
    {
        var c = new byte[44 + dataLen];
        WriteAscii(c, 0, "RIFF"); WriteU32(c, 4, 36 + dataLen); WriteAscii(c, 8, "WAVE");
        WriteAscii(c, 12, "fmt "); WriteU32(c, 16, 16); WriteU16(c, 20, 1);
        WriteU16(c, 22, channels); WriteU32(c, 24, rate); WriteU32(c, 28, rate * channels * bits / 8);
        WriteU16(c, 32, channels * bits / 8); WriteU16(c, 34, bits);
        WriteAscii(c, 36, "data"); WriteU32(c, 40, dataLen);
        for (int i = 0; i < dataLen; i++) c[44 + i] = (byte)i;
        return c;
    }

    static void CreateXlsx(string path)
    {
        using (var fs = File.Create(path))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            AddEntry(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>" +
                "</Types>");

            AddEntry(zip, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");

            AddEntry(zip, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "</Relationships>");

            AddEntry(zip, "xl/sharedStrings.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"6\" uniqueCount=\"6\">" +
                "<si><t>姓名</t></si><si><t>班级</t></si><si><t>性别</t></si>" +
                "<si><t>张三</t></si><si><t>男</t></si><si><t>女</t></si></sst>");

            AddEntry(zip, "xl/worksheets/sheet1.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" +
                "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c><c r=\"C1\" t=\"s\"><v>2</v></c></row>" +
                "<row r=\"2\"><c r=\"A2\" t=\"s\"><v>3</v></c><c r=\"B2\"><v>19</v></c><c r=\"C2\" t=\"s\"><v>4</v></c></row>" +
                "<row r=\"3\"><c r=\"A3\" t=\"inlineStr\"><is><t>李四</t></is></c><c r=\"B3\"><v>18</v></c><c r=\"C3\" t=\"s\"><v>5</v></c></row>" +
                "</sheetData></worksheet>");
        }
    }

    static void AddEntry(ZipArchive zip, string name, string content)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(content);
    }

    static void WriteAscii(byte[] b, int off, string s) { for (int i = 0; i < s.Length; i++) b[off + i] = (byte)s[i]; }
    static void WriteU16(byte[] b, int off, int v) { b[off] = (byte)(v & 0xFF); b[off + 1] = (byte)((v >> 8) & 0xFF); }
    static void WriteU32(byte[] b, int off, int v) { WriteU16(b, off, v & 0xFFFF); WriteU16(b, off + 2, (v >> 16) & 0xFFFF); }
    static ushort ReadU16(byte[] b, int off) { return (ushort)(b[off] | (b[off + 1] << 8)); }
    static uint ReadU32(byte[] b, int off) { return (uint)(b[off] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24)); }
}
