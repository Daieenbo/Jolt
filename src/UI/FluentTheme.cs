using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Voltide.UI
{
    /// <summary>One line in the hover card: a device and its level.</summary>
    internal class TipRow
    {
        public string Name;
        public string Value;
        public string Channel;      // "48%", "Full", "休眠"
        public int? Percent;        // null when the level is unknown
        public bool Online;
    }

    /// <summary>
    /// The fluent skin used everywhere now: a compact card with one row per device.
    /// Both tints are drawn from the same layout; only the palette changes, so the
    /// dark mode is the light design repainted rather than a second theme.
    ///
    /// Also home of the battery glyphs (hover card + tray icon), so every surface
    /// agrees on how a level looks.
    /// </summary>
    internal static class FluentTheme
    {
        public const float TipRowH = 28f;
        public const float TipPadX = 10f;
        public const float TipPadY = 5f;
        private const float TipGlyphW = 24f;
        private const float TipGap = 6f;

        // ------------------------------------------------------------ palettes

        internal sealed class Palette
        {
            public Color BgTop;
            public Color BgBottom;
            public Color Ink;
            public Color Sub;
            public Color Hair;
            public Color Border;
            public Color Wash;
            public Color AccentGreen;
            public Color AccentAmber;
            public Color AccentRed;
        }

        private static readonly Palette LightPalette = new Palette
        {
            BgTop = Color.FromArgb(246, 242, 237),
            BgBottom = Color.FromArgb(238, 232, 225),
            Ink = Color.FromArgb(43, 38, 33),
            Sub = Color.FromArgb(142, 132, 121),
            Hair = Color.FromArgb(226, 218, 209),
            Border = Color.FromArgb(220, 211, 201),
            Wash = Color.FromArgb(12, 0, 0, 0),
            AccentGreen = Color.FromArgb(64, 168, 96),
            AccentAmber = Color.FromArgb(216, 145, 44),
            AccentRed = Color.FromArgb(199, 74, 58)
        };

        private static readonly Palette DarkPalette = new Palette
        {
            BgTop = Color.FromArgb(46, 46, 48),
            BgBottom = Color.FromArgb(37, 37, 39),
            Ink = Color.FromArgb(245, 245, 245),
            Sub = Color.FromArgb(160, 160, 164),
            Hair = Color.FromArgb(64, 64, 68),
            Border = Color.FromArgb(72, 72, 76),
            Wash = Color.FromArgb(18, 255, 255, 255),
            AccentGreen = Color.FromArgb(90, 200, 130),
            AccentAmber = Color.FromArgb(232, 170, 80),
            AccentRed = Color.FromArgb(230, 112, 96)
        };

        /// <summary>The palette for the currently resolved skin.</summary>
        public static Palette P
        {
            get { return ThemeManager.IsDark ? DarkPalette : LightPalette; }
        }

        public static Color LevelColor(int pct)
        {
            Palette p = P;
            if (pct <= 15) return p.AccentRed;
            if (pct <= 30) return p.AccentAmber;
            return p.AccentGreen;
        }

        // ------------------------------------------------------------ glyphs

        /// <summary>Battery glyph for the hover card: outline plus a proportional fill.</summary>
        public static void DrawBattery(Graphics g, RectangleF r, int? percent, bool online,
                                       float scale, Color outline, Color fill)
        {
            float nubW = Math.Max(1.4f, r.Height * 0.20f);
            float nubH = r.Height * 0.38f;
            float bodyW = r.Width - nubW;

            if (bodyW < 2f) bodyW = r.Width;

            float stroke = Math.Max(1f, scale * 0.9f);
            RectangleF body = new RectangleF(r.X, r.Y, bodyW, r.Height);
            float radius = Math.Min(body.Height / 2f, 3.2f * scale);

            using (GraphicsPath path = Round(body, radius))
            using (Pen pen = new Pen(outline, stroke))
            {
                pen.Alignment = PenAlignment.Inset;
                g.DrawPath(pen, path);

                if (percent.HasValue && online)
                {
                    float inset = stroke;
                    float innerW = body.Width - inset * 2f;
                    float w = innerW * (percent.Value / 100f);
                    if (w > 0.5f)
                    {
                        RectangleF inner = new RectangleF(
                            body.X + inset, body.Y + inset, w, body.Height - inset * 2f);
                        using (GraphicsPath fillPath = Round(inner, Math.Max(0f, radius - inset)))
                        using (SolidBrush brush = new SolidBrush(fill))
                        {
                            g.FillPath(brush, fillPath);
                        }
                    }
                }
                else
                {
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(150, outline)))
                    {
                        float dashW = body.Width * 0.34f;
                        g.FillRectangle(brush, body.X + (body.Width - dashW) / 2f,
                                        body.Y + body.Height / 2f - stroke / 2f, dashW, stroke);
                    }
                }
            }

            RectangleF nub = new RectangleF(body.Right - stroke * 0.5f,
                                            r.Y + (r.Height - nubH) / 2f, nubW, nubH);
            using (GraphicsPath path = Round(nub, nubH * 0.3f))
            using (SolidBrush brush = new SolidBrush(outline))
            {
                g.FillPath(brush, path);
            }
        }

        /// <summary>Keep the rounded outline inside the tray slot and the level edge on a pixel boundary.</summary>
        public static void DrawTrayBattery(Graphics g, Rectangle body, int nubWidth,
                                          int? percent, bool light)
        {
            int nubHeight = Math.Max(1, body.Height / 2);
            Rectangle nub = new Rectangle(body.Right - nubWidth,
                body.Top + (body.Height - nubHeight) / 2, 2 * nubWidth, nubHeight);
            int level = Math.Max(0, Math.Min(100, percent.GetValueOrDefault()));
            Color charge = light ? Color.FromArgb(23, 23, 23) : DarkPalette.AccentGreen;
            Color track = light ? Color.FromArgb(171, 174, 179) : Color.FromArgb(94, 98, 104);
            float stroke = light ? 0f : Math.Min(body.Height / 4f, (body.Width + nubWidth) / 16f);
            RectangleF nubInside = nub;
            nubInside.Inflate(-stroke * 0.5f, -stroke * 0.5f);
            RectangleF inside = body;
            inside.Inflate(-stroke, -stroke);
            float radius = body.Height * 0.26f;

            using (SolidBrush outline = new SolidBrush(Color.White))
            using (SolidBrush empty = new SolidBrush(track))
            using (SolidBrush filled = new SolidBrush(charge))
            using (GraphicsPath terminal = Round(nub, Math.Min(nubWidth, nubHeight) * 0.5f))
            using (GraphicsPath terminalInside = Round(nubInside, Math.Max(0, (Math.Min(nubWidth, nubHeight) - stroke) * 0.5f)))
            using (GraphicsPath shell = Round(body, radius))
            using (GraphicsPath shape = Round(inside, Math.Max(0, radius - stroke)))
            {
                if (!light) g.FillPath(outline, terminal);
                g.FillPath(empty, terminalInside);
                if (!light) g.FillPath(outline, shell);
                if (level == 0 || level == 100)
                {
                    g.FillPath(level == 0 ? empty : filled, shape);
                    return;
                }

                int split = (int)Math.Round(inside.Left + inside.Width * level / 100.0);
                split = Math.Max((int)Math.Ceiling(inside.Left) + 1,
                    Math.Min((int)Math.Floor(inside.Right) - 1, split));
                GraphicsState state = g.Save();
                g.IntersectClip(Rectangle.FromLTRB(body.Left, body.Top, split, body.Bottom));
                g.FillPath(filled, shape);
                g.Restore(state);

                state = g.Save();
                g.IntersectClip(Rectangle.FromLTRB(split, body.Top, body.Right, body.Bottom));
                g.FillPath(empty, shape);
                g.Restore(state);
            }
        }

        // -------------------------------------------------------------- tip

        /// <summary>Size of the hover card for a given row count.</summary>
        public static Size TipSize(List<TipRow> rows, float scale)
        {
            if (rows == null || rows.Count == 0)
                return new Size((int)Math.Ceiling(176f * scale), (int)Math.Ceiling(52f * scale));

            float need = 0f;

            if (rows != null && rows.Count > 0)
            {
                using (Bitmap probe = new Bitmap(8, 8))
                using (Graphics g = Graphics.FromImage(probe))
                {
                    float rowGap = TipGap * scale;

                    foreach (TipRow row in rows)
                    {
                        float nameW = Measure(g, row.Name, NameFont(row.Name, scale));
                        float valueW = Measure(g, row.Value,
                            FontManager.Get(FontKind.MontserratSemiBold, 14f * scale));
                        float channelW = string.IsNullOrEmpty(row.Channel)
                            ? 0f : Measure(g, row.Channel, ChannelFont(row.Channel, scale));

                        float w = (TipPadX + TipGlyphW) * scale + rowGap + nameW;
                        if (channelW > 0f) w += rowGap + channelW;
                        w += rowGap + valueW + TipPadX * scale;

                        if (w > need) need = w;
                    }
                }
            }

            int w2 = (int)Math.Ceiling(need);
            int h = (int)Math.Round((TipPadY * 2f + rows.Count * TipRowH) * scale);
            return new Size(w2, h);
        }

        /// <summary>The hover card: one row per device, name left and level right.</summary>
        public static void DrawTip(Graphics g, int w, int h, float scale, List<TipRow> rows)
        {
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            Palette p = P;

            using (LinearGradientBrush brush = new LinearGradientBrush(
                new Rectangle(0, 0, w, h), p.BgTop, p.BgBottom, LinearGradientMode.Vertical))
            {
                g.FillRectangle(brush, new Rectangle(0, 0, w, h));
            }

            using (Pen pen = new Pen(p.Border, Math.Max(1f, scale)))
            {
                pen.Alignment = PenAlignment.Inset;
                g.DrawRectangle(pen, 0, 0, w - 1, h - 1);
            }

            if (rows == null || rows.Count == 0)
            {
                DrawEmpty(g, TipPadY * scale, w, scale, p.Sub);
                return;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                TipRow row = rows[i];
                float top_ = (TipPadY + i * TipRowH) * scale;
                float cy = top_ + TipRowH * scale / 2f;

                float glyphW = TipGlyphW * scale;
                float glyphH = 12f * scale;
                Color level = row.Percent.HasValue ? LevelColor(row.Percent.Value) : p.Sub;
                DrawBattery(g, new RectangleF(TipPadX * scale, cy - glyphH / 2f, glyphW, glyphH),
                            row.Percent, row.Online, scale, p.Sub, level);

                Font valueFont = FontManager.Get(FontKind.MontserratSemiBold, 14f * scale);
                Font channelFont = ChannelFont(row.Channel, scale);
                string channel = row.Channel != null ? row.Channel : "";

                float gap = TipGap * scale;
                float right = w - TipPadX * scale;
                float valueW = Measure(g, row.Value, valueFont);
                float channelW = channel.Length > 0 ? Measure(g, channel, channelFont) : 0f;
                float channelRight = right - valueW - (channelW > 0f ? gap : 0f);
                float nameRight = channelRight - gap;

                Font nameFont = NameFont(row.Name, scale);
                using (SolidBrush brush = new SolidBrush(row.Online ? p.Ink : p.Sub))
                using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
                {
                    sf.FormatFlags |= StringFormatFlags.NoWrap;
                    sf.Trimming = StringTrimming.EllipsisCharacter;
                    sf.LineAlignment = StringAlignment.Center;
                    RectangleF nameBox = new RectangleF((TipPadX + TipGlyphW + TipGap) * scale, top_,
                        Math.Max(0f, nameRight - (TipPadX + TipGlyphW + TipGap) * scale), TipRowH * scale);
                    g.DrawString(row.Name, nameFont, brush, nameBox, sf);
                }

                if (channelW > 0f)
                {
                    using (SolidBrush brush = new SolidBrush(p.Sub))
                    using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
                    {
                        sf.FormatFlags |= StringFormatFlags.NoWrap;
                        sf.Alignment = StringAlignment.Far;
                        sf.LineAlignment = StringAlignment.Center;
                        RectangleF box = new RectangleF(0f, top_, channelRight, TipRowH * scale);
                        g.DrawString(channel, channelFont, brush, box, sf);
                    }
                }

                using (SolidBrush brush = new SolidBrush(row.Percent.HasValue ? p.Ink : p.Sub))
                using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
                {
                    sf.FormatFlags |= StringFormatFlags.NoWrap;
                    sf.Alignment = StringAlignment.Far;
                    sf.LineAlignment = StringAlignment.Center;
                    RectangleF box = new RectangleF(0f, top_, right, TipRowH * scale);
                    g.DrawString(row.Value, valueFont, brush, box, sf);
                }
            }
        }

        /// <summary>"2.4G", "有线" or "蓝牙" from the provider detail string.</summary>
        public static string ChannelOf(string detail)
        {
            if (string.IsNullOrEmpty(detail)) return "";

            if (detail.IndexOf("蓝牙", StringComparison.Ordinal) >= 0) return "蓝牙";
            if (detail.IndexOf("有线", StringComparison.Ordinal) >= 0) return "有线";
            if (detail.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0) return "有线";
            if (detail.IndexOf("2.4G", StringComparison.OrdinalIgnoreCase) >= 0) return "2.4G";
            if (detail.IndexOf("XInput", StringComparison.OrdinalIgnoreCase) >= 0) return "2.4G";
            if (detail.IndexOf("HID++", StringComparison.OrdinalIgnoreCase) >= 0) return "2.4G";
            return "";
        }

        /// <summary>True when the string needs a CJK face; short helper shared by the card.</summary>
        public static bool HasCjk(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            foreach (char c in text)
            {
                if (c >= 0x2E80) return true;
            }

            return false;
        }

        private static Font ChannelFont(string channel, float scale)
        {
            bool cjk = HasCjk(channel);
            return FontManager.Get(cjk ? FontKind.Chinese : FontKind.MontserratMedium, 11f * scale);
        }

        private static float Measure(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.FormatFlags = StringFormatFlags.NoWrap;
                return g.MeasureString(text, font, 1000, sf).Width;
            }
        }

        private static Font NameFont(string name, float scale)
        {
            bool cjk = HasCjk(name);
            return FontManager.Get(cjk ? FontKind.Chinese : FontKind.MontserratMedium, 14f * scale);
        }

        private static void DrawEmpty(Graphics g, float top, float width, float scale, Color ink)
        {
            Font font = FontManager.Get(FontKind.Chinese, 13f * scale);
            Font small = FontManager.Get(FontKind.Chinese, 11f * scale);
            float lineH = font.GetHeight(g);

            using (SolidBrush brush = new SolidBrush(ink))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Near;
                sf.FormatFlags = StringFormatFlags.NoWrap;

                g.DrawString("没有检测到受支持的设备", font, brush,
                    new RectangleF(0f, top, width, lineH), sf);
                g.DrawString("请确认接收器已插入，设备已开机", small, brush,
                    new RectangleF(0f, top + lineH + 5f * scale, width, lineH), sf);
            }
        }

        // ------------------------------------------------------------ helpers

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Max(0f, radius * 2f);

            if (d <= 0.01f)
            {
                p.AddRectangle(r);
                return p;
            }

            p.AddArc(r.Left, r.Top, d, d, 180f, 90f);
            p.AddArc(r.Right - d, r.Top, d, d, 270f, 90f);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90f, 90f);
            p.CloseFigure();
            return p;
        }
    }
}
