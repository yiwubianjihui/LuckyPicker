// ================================================================
// ThemeManager.cs — 三套配色统一管理
//
// 所有颜色以 Application.Resources 中的 Brush 键发布，XAML 侧用
// DynamicResource 引用；代码创建控件在构建时读取 ThemeManager.Current。
// 切换主题 = 重写资源 + 触发 Changed，主窗口与悬浮球各自刷新代码部分。
// ================================================================
using System;
using System.Windows;
using System.Windows.Media;

namespace LuckyPickerWpf
{
    public sealed class ThemePalette
    {
        public string Id = "";
        public string Name = "";
        public string Description = "";

        public Color Canvas, Surface, SurfaceMuted, Stroke, StrokeHover;
        public Color Ink, InkSoft, Sub, Muted;
        public Color Accent, AccentHover, AccentPressed, AccentDisabled;
        public Color AccentInk, AccentSoft, AccentSoftHover, AccentSoftPressed, AccentSoftBorder;
        public Color CardShadow, ControlBg, ControlHover, ControlPressed;
        public Color BlockChipBg, BlockChipInk, BlockChipBorder;
        public Color BallHi, BallMid, BallEdge, BallFace, BallShadow, BallFlash;
    }

    public static class ThemeManager
    {
        public const string SageId = "sage";
        public const string MistId = "mist";
        public const string InkId = "ink";

        static readonly ThemePalette Sage = new()
        {
            Id = SageId, Name = "鼠尾草绿", Description = "低饱和 · 默认",
            Canvas = C(0xF4, 0xF4, 0xF0), Surface = C(0xFF, 0xFF, 0xFF), SurfaceMuted = C(0xFA, 0xFA, 0xF7),
            Stroke = C(0xE5, 0xE5, 0xDF), StrokeHover = C(0xBC, 0xC9, 0xC1),
            Ink = C(0x1F, 0x24, 0x21), InkSoft = C(0x3D, 0x44, 0x3F), Sub = C(0x6F, 0x75, 0x6F), Muted = C(0x94, 0x99, 0x94),
            Accent = C(0x3F, 0x6B, 0x58), AccentHover = C(0x34, 0x5A, 0x49), AccentPressed = C(0x2B, 0x4B, 0x3E),
            AccentDisabled = C(0xAF, 0xC3, 0xB8), AccentInk = C(0x2F, 0x54, 0x46),
            AccentSoft = C(0xED, 0xF3, 0xEF), AccentSoftHover = C(0xF1, 0xF5, 0xF2), AccentSoftPressed = C(0xE7, 0xEF, 0xE9),
            AccentSoftBorder = C(0xD6, 0xE3, 0xDB), CardShadow = C(0xE9, 0xE9, 0xE3),
            ControlBg = C(0xF4, 0xF4, 0xF1), ControlHover = C(0xEA, 0xEA, 0xE4), ControlPressed = C(0xDF, 0xDF, 0xD8),
            BlockChipBg = C(0xF5, 0xF3, 0xED), BlockChipInk = C(0x5E, 0x54, 0x42), BlockChipBorder = C(0xE3, 0xDB, 0xCB),
            BallHi = C(0xF2, 0xF7, 0xF3), BallMid = C(0xC9, 0xDE, 0xD2), BallEdge = C(0xA3, 0xC5, 0xB4),
            BallFace = C(0x2F, 0x54, 0x46), BallShadow = C(0x22, 0x3C, 0x31), BallFlash = C(0xBF, 0xD8, 0xC9),
        };

        static readonly ThemePalette Mist = new()
        {
            Id = MistId, Name = "雾霭蓝", Description = "冷静 · 清透",
            Canvas = C(0xF3, 0xF5, 0xF8), Surface = C(0xFF, 0xFF, 0xFF), SurfaceMuted = C(0xF8, 0xFA, 0xFC),
            Stroke = C(0xE1, 0xE6, 0xED), StrokeHover = C(0xB9, 0xC7, 0xD4),
            Ink = C(0x1C, 0x24, 0x30), InkSoft = C(0x38, 0x42, 0x4F), Sub = C(0x64, 0x70, 0x7F), Muted = C(0x92, 0x9C, 0xA8),
            Accent = C(0x3B, 0x6E, 0x8F), AccentHover = C(0x2F, 0x5B, 0x77), AccentPressed = C(0x27, 0x4C, 0x64),
            AccentDisabled = C(0xA8, 0xC0, 0xD0), AccentInk = C(0x27, 0x4F, 0x66),
            AccentSoft = C(0xEA, 0xF1, 0xF6), AccentSoftHover = C(0xF0, 0xF5, 0xF9), AccentSoftPressed = C(0xDF, 0xEA, 0xF2),
            AccentSoftBorder = C(0xCF, 0xE0, 0xEA), CardShadow = C(0xE3, 0xE8, 0xEF),
            ControlBg = C(0xF3, 0xF5, 0xF8), ControlHover = C(0xE9, 0xEE, 0xF3), ControlPressed = C(0xDE, 0xE5, 0xEC),
            BlockChipBg = C(0xEE, 0xF2, 0xF6), BlockChipInk = C(0x4A, 0x5A, 0x6B), BlockChipBorder = C(0xD7, 0xE1, 0xEA),
            BallHi = C(0xF2, 0xF7, 0xFB), BallMid = C(0xCB, 0xDE, 0xEA), BallEdge = C(0xA6, 0xC6, 0xD8),
            BallFace = C(0x27, 0x4F, 0x66), BallShadow = C(0x1E, 0x33, 0x42), BallFlash = C(0xB9, 0xD6, 0xE6),
        };

        static readonly ThemePalette Ink = new()
        {
            Id = InkId, Name = "墨色极简", Description = "中性 · 黑白",
            Canvas = C(0xF3, 0xF3, 0xF1), Surface = C(0xFF, 0xFF, 0xFF), SurfaceMuted = C(0xF8, 0xF8, 0xF6),
            Stroke = C(0xE2, 0xE2, 0xDE), StrokeHover = C(0xBD, 0xBD, 0xB7),
            Ink = C(0x18, 0x18, 0x18), InkSoft = C(0x35, 0x35, 0x32), Sub = C(0x6B, 0x6B, 0x68), Muted = C(0x97, 0x97, 0x93),
            Accent = C(0x2B, 0x2B, 0x2B), AccentHover = C(0x18, 0x18, 0x18), AccentPressed = C(0x00, 0x00, 0x00),
            AccentDisabled = C(0xB4, 0xB4, 0xB0), AccentInk = C(0x18, 0x18, 0x18),
            AccentSoft = C(0xEF, 0xEF, 0xEC), AccentSoftHover = C(0xF4, 0xF4, 0xF1), AccentSoftPressed = C(0xE7, 0xE7, 0xE3),
            AccentSoftBorder = C(0xD8, 0xD8, 0xD3), CardShadow = C(0xE6, 0xE6, 0xE2),
            ControlBg = C(0xF3, 0xF3, 0xF1), ControlHover = C(0xE9, 0xE9, 0xE5), ControlPressed = C(0xDE, 0xDE, 0xDA),
            BlockChipBg = C(0xF4, 0xF4, 0xF1), BlockChipInk = C(0x4B, 0x4B, 0x47), BlockChipBorder = C(0xDE, 0xDE, 0xD8),
            BallHi = C(0xF7, 0xF7, 0xF5), BallMid = C(0xDE, 0xDE, 0xDA), BallEdge = C(0xC4, 0xC4, 0xBE),
            BallFace = C(0x20, 0x20, 0x20), BallShadow = C(0x1A, 0x1A, 0x1A), BallFlash = C(0xCF, 0xCF, 0xC9),
        };

        public static readonly ThemePalette[] All = { Sage, Mist, Ink };

        public static ThemePalette Current { get; private set; } = Sage;

        public static event Action? Changed;

        public static ThemePalette Get(string? id)
        {
            foreach (var p in All)
                if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) return p;
            return Sage;
        }

        public static string Normalize(string? id) => Get(id).Id;

        /// <summary>应用主题到全局资源；persist=true 时同时写入本机配置。</summary>
        public static void Apply(string? id, bool persist = false)
        {
            var p = Get(id);
            Current = p;
            if (persist)
            {
                AppConfig.Theme = p.Id;
                AppConfig.Save();
            }
            var res = System.Windows.Application.Current?.Resources;
            if (res != null)
            {
                Set(res, "CanvasBrush", p.Canvas);
                Set(res, "SurfaceBrush", p.Surface);
                Set(res, "SurfaceMutedBrush", p.SurfaceMuted);
                Set(res, "StrokeBrush", p.Stroke);
                Set(res, "StrokeHoverBrush", p.StrokeHover);
                Set(res, "InkBrush", p.Ink);
                Set(res, "InkSoftBrush", p.InkSoft);
                Set(res, "SubBrush", p.Sub);
                Set(res, "MutedBrush", p.Muted);
                Set(res, "BrandBrush", p.Accent);
                Set(res, "BrandHoverBrush", p.AccentHover);
                Set(res, "BrandPressedBrush", p.AccentPressed);
                Set(res, "BrandDisabledBrush", p.AccentDisabled);
                Set(res, "BrandInkBrush", p.AccentInk);
                Set(res, "BrandSoftBrush", p.AccentSoft);
                Set(res, "BrandSoftHoverBrush", p.AccentSoftHover);
                Set(res, "BrandSoftPressedBrush", p.AccentSoftPressed);
                Set(res, "BrandSoftBorderBrush", p.AccentSoftBorder);
                Set(res, "CardShadowBrush", p.CardShadow);
                Set(res, "ControlBgBrush", p.ControlBg);
                Set(res, "ControlHoverBrush", p.ControlHover);
                Set(res, "ControlPressedBrush", p.ControlPressed);
                Set(res, "BlockChipBgBrush", p.BlockChipBg);
                Set(res, "BlockChipInkBrush", p.BlockChipInk);
                Set(res, "BlockChipBorderBrush", p.BlockChipBorder);
            }
            try { Changed?.Invoke(); } catch { }
        }

        static void Set(ResourceDictionary res, string key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            res[key] = brush;
        }

        static Color C(int r, int g, int b) => Color.FromRgb((byte)r, (byte)g, (byte)b);
    }
}
