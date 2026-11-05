using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ProxyTool
{
    /// <summary>深色主题配色</summary>
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(23, 24, 27);
        public static readonly Color Panel = Color.FromArgb(32, 34, 38);
        public static readonly Color PanelHover = Color.FromArgb(43, 46, 52);
        public static readonly Color PanelSunken = Color.FromArgb(27, 29, 33);
        public static readonly Color Border = Color.FromArgb(48, 51, 56);
        public static readonly Color Text = Color.FromArgb(230, 232, 236);
        public static readonly Color SubText = Color.FromArgb(139, 144, 154);
        public static readonly Color Dim = Color.FromArgb(100, 106, 117);
        public static readonly Color Accent = Color.FromArgb(91, 108, 240);
        public static readonly Color AccentHover = Color.FromArgb(112, 127, 245);
        public static readonly Color Green = Color.FromArgb(67, 190, 120);
        public static readonly Color Yellow = Color.FromArgb(222, 168, 74);
        public static readonly Color Red = Color.FromArgb(233, 94, 94);
        public static readonly Color RedSoft = Color.FromArgb(226, 124, 84);
        public static readonly Color Card = Color.FromArgb(30, 33, 38);
        public static readonly Color CardHover = Color.FromArgb(38, 41, 48);
        public static readonly Color PillBg = Color.FromArgb(52, 58, 88);
        public static readonly Color PillFg = Color.FromArgb(158, 170, 255);
    }

    /// <summary>一条代理记录</summary>
    internal class Proxy
    {
        public string Addr = "";
        public string Type = "";
        public int Latency = -1;
        public int Code;
        public bool Ok;
        public string Err = "";
        public string Tested = "";
    }

    internal class Hit { public string Ip; public int Port; }
    internal class Result { public int Code; public int Ms = -1; public string Err = ""; public bool Ok; }

    internal class MainForm : Form
    {
        private const string TestUrl = "https://www.google.com/generate_204";
        private const string HttpFallbackUrl = "http://www.gstatic.com/generate_204";
        private const string RegPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

        private const int TitleH = 38;
        private const int ToolbarH = 54;
        private const int CardH = 62;
        private const int Gap = 8;
        private const int Pitch = CardH + Gap;
        private const int StatusH = 28;
        private const int Pad = 16;

        private static readonly int[] DefaultPorts = { 1080, 3128, 7890, 7891, 7892, 7897, 8080, 8118, 8123, 8888, 10808, 10809 };

        private readonly string[] _btnText = { "扫描", "测速", "自定义网址测速", "取消代理", "导出" };
        private readonly List<Rectangle> _btnRects = new List<Rectangle>();
        private readonly List<Proxy> _proxies = new List<Proxy>();

        private Rectangle _btnClose = Rectangle.Empty;
        private Rectangle _btnStop = Rectangle.Empty;
        private Rectangle _btnSort = Rectangle.Empty;

        private string _status = "就绪";
        private Color _statusColor = Theme.SubText;
        private bool _busy;
        private string _busyText = "";
        private int _progress = -1;
        private volatile bool _cancel;
        private int _opId;

        private int _hoverBtn = -1;
        private int _hoverRow = -1;
        private int _selectedRow = -1;
        private int _scroll;
        private bool _sortDesc;

        private readonly string _workDir;
        private string _cachePath;
        private DateTime _cacheTime = DateTime.MinValue;
        private bool _isAdmin;
        private bool _canWrite;
        private int[] _ports = DefaultPorts;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        public MainForm() : this(false) { }

        public MainForm(bool headless)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1000, 640);
            MinimumSize = new Size(860, 520);
            BackColor = Theme.Bg;
            Text = "局域网代理工具箱";

            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            if (Icon == null) Icon = SystemIcons.Application;

            _workDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            _cachePath = Path.Combine(_workDir, "proxy-cache.txt");
            _isAdmin = IsAdministrator();
            _canWrite = CanWriteProxySettings();

            LoadCache();
            SortProxies();
        }

        // ==================== 绘制 ====================

        protected override void OnPaint(PaintEventArgs e) { RenderPreview(e.Graphics); }

        internal void SimulateBusy()
        {
            _busy = true;
            _busyText = "测试中 3/8";
            _progress = 37;
        }

        internal void RenderPreview(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Bg);

            DrawTitleBar(g);
            DrawToolbar(g);
            DrawList(g);
            DrawStatusBar(g);
            DrawResizeGrip(g);
        }

        private void DrawTitleBar(Graphics g)
        {
            using (SolidBrush b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, 0, 0, Width, TitleH);
            using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, 0, TitleH - 1, Width, TitleH - 1);

            try { if (Icon != null) g.DrawIcon(Icon, new Rectangle(Pad, (TitleH - 16) / 2, 16, 16)); } catch { }

            using (Font f = new Font("Microsoft YaHei UI", 9.5f))
            using (SolidBrush b = new SolidBrush(Theme.Text))
                g.DrawString("局域网代理工具箱", f, b, Pad + 24, TitleH / 2 - 10);

            _btnClose = new Rectangle(Width - 46, 0, 46, TitleH);
            DrawWindowButton(g, _btnClose);
        }

        private void DrawWindowButton(Graphics g, Rectangle r)
        {
            bool hover = IsHandleCreated && r.Contains(PointToClient(Cursor.Position));
            if (hover)
                using (SolidBrush b = new SolidBrush(Color.FromArgb(196, 62, 62))) g.FillRectangle(b, r);

            using (Pen p = new Pen(hover ? Color.White : Theme.SubText, 1.4f))
            {
                int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                g.DrawLine(p, cx - 5, cy - 5, cx + 5, cy + 5);
                g.DrawLine(p, cx + 5, cy - 5, cx - 5, cy + 5);
            }
        }

        private void DrawToolbar(Graphics g)
        {
            using (SolidBrush b = new SolidBrush(Theme.Bg)) g.FillRectangle(b, 0, TitleH, Width, ToolbarH);

            _btnRects.Clear();
            for (int i = 0; i < _btnText.Length; i++) _btnRects.Add(Rectangle.Empty);
            _btnStop = Rectangle.Empty;
            _btnSort = Rectangle.Empty;

            int y = TitleH + (ToolbarH - 32) / 2;
            using (Font f = new Font("Microsoft YaHei UI", 9f))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;

                int x = Pad;
                for (int i = 0; i < 3; i++)
                {
                    int w = TextRenderer.MeasureText(_btnText[i], f).Width + 28;
                    Rectangle r = new Rectangle(x, y, w, 32);
                    _btnRects[i] = r;
                    DrawToolButton(g, r, _btnText[i], i == 0, sf);
                    x += w + 8;
                }

                if (_busy)
                {
                    int w = TextRenderer.MeasureText("停止", f).Width + 32;
                    Rectangle r = new Rectangle(Width - Pad - w, y, w, 32);
                    _btnStop = r;
                    bool hover = _hoverBtn == 99;
                    FillRound(g, r, 8, hover ? Color.FromArgb(196, 62, 62) : Theme.Card);
                    DrawRound(g, r, 8, hover ? Color.FromArgb(196, 62, 62) : Theme.Border);
                    using (SolidBrush b = new SolidBrush(hover ? Color.White : Theme.Text))
                        g.DrawString("停止", f, b, r, sf);

                    using (Font fs = new Font("Microsoft YaHei UI", 8f))
                    using (SolidBrush b = new SolidBrush(Theme.SubText))
                    {
                        Size sz = TextRenderer.MeasureText(_busyText, fs);
                        float tx = r.X - 18 - sz.Width;
                        if (tx > x + 8) g.DrawString(_busyText, fs, b, tx, y + 9);
                    }
                }
                else
                {
                    int rightX = Width - Pad;
                    for (int i = _btnText.Length - 1; i >= 3; i--)
                    {
                        int w = TextRenderer.MeasureText(_btnText[i], f).Width + 28;
                        rightX -= w;
                        Rectangle r = new Rectangle(rightX, y, w, 32);
                        _btnRects[i] = r;
                        DrawToolButton(g, r, _btnText[i], false, sf);
                        rightX -= 8;
                    }

                    string sortText = _sortDesc ? "延迟 ↓" : "延迟 ↑";
                    int sw = TextRenderer.MeasureText(sortText, f).Width + 24;
                    rightX -= sw;
                    _btnSort = new Rectangle(rightX, y, sw, 32);
                    DrawToolButton(g, _btnSort, sortText, false, sf);
                }
            }

            if (_busy)
            {
                int barY = TitleH + ToolbarH - 2;
                using (SolidBrush b = new SolidBrush(Theme.PanelSunken)) g.FillRectangle(b, 0, barY, Width, 2);
                if (_progress >= 0)
                {
                    int w = (int)((Width - 2) * (_progress / 100.0));
                    using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, 0, barY, w, 2);
                }
            }
        }

        private void DrawToolButton(Graphics g, Rectangle r, string text, bool primary, StringFormat sf)
        {
            int idx = _btnRects.IndexOf(r);
            bool hover = (idx >= 0 && idx == _hoverBtn) && !_busy;
            Color bg = primary ? (hover ? Theme.AccentHover : Theme.Accent) : (hover ? Theme.PanelHover : Theme.Card);
            Color fg = primary ? Color.White : Theme.Text;
            if (primary && _busy) { bg = Theme.Card; fg = Theme.SubText; }

            FillRound(g, r, 8, bg);
            if (!primary) DrawRound(g, r, 8, Theme.Border);

            using (Font f = new Font("Microsoft YaHei UI", 9f))
            using (SolidBrush b = new SolidBrush(fg))
                g.DrawString(text, f, b, r, sf);
        }

        private void DrawList(Graphics g)
        {
            int listTop = TitleH + ToolbarH;
            int listBottom = Height - StatusH - 8;
            int listH = listBottom - listTop;
            if (listH < CardH) return;

            int maxScroll = Math.Max(0, _proxies.Count * Pitch - Gap - listH);
            if (_scroll > maxScroll) _scroll = maxScroll;
            if (_scroll < 0) _scroll = 0;

            Region old = g.Clip;
            g.SetClip(new Rectangle(0, listTop, Width, listH));

            using (Font fAddr = new Font("Consolas", 12f, FontStyle.Bold))
            using (Font fNum = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold))
            using (Font fMs = new Font("Microsoft YaHei UI", 7.5f))
            using (Font fStat = new Font("Microsoft YaHei UI", 9f))
            using (Font fIdx = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold))
            using (StringFormat sfCenter = new StringFormat())
            using (StringFormat sfRightVC = new StringFormat())
            {
                sfCenter.Alignment = StringAlignment.Center;
                sfCenter.LineAlignment = StringAlignment.Center;
                sfRightVC.Alignment = StringAlignment.Far;
                sfRightVC.LineAlignment = StringAlignment.Center;

                float statusW = g.MeasureString("可用", fStat).Width;
                float msW = g.MeasureString("ms", fMs).Width;

                for (int i = 0; i < _proxies.Count; i++)
                {
                    int y = listTop + i * Pitch - _scroll;
                    if (y + CardH < listTop || y > listBottom) continue;

                    Proxy p = _proxies[i];
                    Rectangle card = new Rectangle(16, y, Width - 32, CardH);
                    bool selected = (i == _selectedRow);
                    bool hover = (i == _hoverRow) && !selected;

                    FillRound(g, card, 12, selected ? Color.FromArgb(32, 38, 60) : (hover ? Theme.CardHover : Theme.Card));
                    DrawRound(g, card, 12, selected ? Theme.Accent : Theme.Border);

                    using (SolidBrush b = new SolidBrush(Color.FromArgb(40, 44, 52)))
                        g.FillEllipse(b, card.X + 16, card.Y + 18, 26, 26);
                    using (SolidBrush b = new SolidBrush(selected ? Theme.Accent : Theme.SubText))
                        g.DrawString((i + 1).ToString(), fIdx, b, new RectangleF(card.X + 16, card.Y + 18, 26, 26), sfCenter);

                    DrawTextVC(g, p.Addr, fAddr, Theme.Text, card.X + 56, card);

                    DrawPill(g, new Rectangle(card.X + 300, card.Y + 21, 96, 20), p.Type);

                    float statusX = card.Right - 24 - statusW;
                    float dotX = statusX - 22;
                    float msX = dotX - 30 - msW;
                    float numRight = msX - 8;

                    if (p.Latency >= 0)
                    {
                        Color lc = p.Latency < 1000 ? Theme.Green : (p.Latency < 3000 ? Theme.Yellow : Theme.RedSoft);
                        using (SolidBrush b = new SolidBrush(lc))
                            g.DrawString(p.Latency.ToString(), fNum, b, new RectangleF(numRight - 80, card.Y, 80, card.Height), sfRightVC);
                        DrawTextVC(g, "ms", fMs, Theme.Dim, msX, card);
                    }
                    else
                    {
                        using (SolidBrush b = new SolidBrush(Theme.Dim))
                            g.DrawString("—", fNum, b, new RectangleF(numRight - 80, card.Y, 80, card.Height), sfRightVC);
                    }

                    Color st = p.Ok ? Theme.Green : Theme.Dim;
                    float cy = card.Y + card.Height / 2f;
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(38, st))) g.FillEllipse(b, dotX, cy - 7f, 14f, 14f);
                    using (SolidBrush b = new SolidBrush(st)) g.FillEllipse(b, dotX + 4f, cy - 3f, 6f, 6f);
                    DrawTextVC(g, p.Ok ? "可用" : (p.Tested.Length > 0 ? "不通" : "未测"), fStat, st, statusX, card);
                }
            }

            if (_proxies.Count == 0)
            {
                using (Font f = new Font("Microsoft YaHei UI", 9f))
                using (SolidBrush b = new SolidBrush(Theme.Dim))
                    g.DrawString("还没有数据，点击左上角「扫描」开始", f, b, 20, listTop + 20);
            }

            g.Clip = old;

            if (maxScroll > 0)
            {
                int trackX = Width - 8;
                int thumbH = Math.Max(30, (int)(listH * (listH / (double)(_proxies.Count * Pitch))));
                int thumbY = listTop + (int)((listH - thumbH) * (_scroll / (double)maxScroll));
                using (SolidBrush b = new SolidBrush(Color.FromArgb(60, 64, 72))) g.FillRectangle(b, trackX, thumbY, 4, thumbH);
            }
        }

        private void DrawStatusBar(Graphics g)
        {
            int y = Height - StatusH;
            using (SolidBrush b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, 0, y, Width, StatusH);
            using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, 0, y, Width, y);

            string left = "系统代理: " + GetSystemProxyText();
            string right = "缓存: " + CacheAgeText() + (_isAdmin ? "   管理员" : "   普通用户");

            using (Font f = new Font("Microsoft YaHei UI", 8.5f))
            {
                using (SolidBrush b = new SolidBrush(Theme.SubText)) g.DrawString(left, f, b, Pad, y + 6);
                Size sz = TextRenderer.MeasureText(right, f);
                using (SolidBrush b = new SolidBrush(Theme.SubText)) g.DrawString(right, f, b, Width - Pad - sz.Width, y + 6);

                if (_status.Length > 0)
                {
                    int leftW = TextRenderer.MeasureText(left, f).Width;
                    Rectangle rr = new Rectangle(Pad + leftW + 16, y + 6, Math.Max(50, Width - Pad * 2 - sz.Width - leftW - 30), 16);
                    using (SolidBrush b = new SolidBrush(_statusColor))
                    {
                        g.SetClip(rr);
                        g.DrawString(_status, f, b, rr.X, y + 6);
                        g.ResetClip();
                    }
                }
            }
        }

        private void DrawResizeGrip(Graphics g)
        {
            using (Pen p = new Pen(Color.FromArgb(60, 64, 72)))
                for (int i = 0; i < 3; i++)
                    g.DrawLine(p, Width - 14 + i * 4, Height - 4, Width - 4, Height - 14 + i * 4);
        }

        private static void DrawTextVC(Graphics g, string text, Font f, Color color, float x, Rectangle box)
        {
            SizeF sz = g.MeasureString(text, f);
            using (SolidBrush b = new SolidBrush(color))
                g.DrawString(text, f, b, x, box.Y + (box.Height - sz.Height) / 2f);
        }

        private static void DrawPill(Graphics g, Rectangle r, string text)
        {
            using (GraphicsPath path = RoundRect(r, r.Height / 2))
            using (SolidBrush b = new SolidBrush(Theme.PillBg)) g.FillPath(b, path);

            using (Font f = new Font("Microsoft YaHei UI", 7.5f))
            using (SolidBrush b = new SolidBrush(Theme.PillFg))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(text, f, b, r, sf);
            }
        }

        private static void FillRound(Graphics g, Rectangle r, int radius, Color c)
        {
            using (GraphicsPath path = RoundRect(r, radius))
            using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, path);
        }

        private static void DrawRound(Graphics g, Rectangle r, int radius, Color c)
        {
            using (GraphicsPath path = RoundRect(r, radius))
            using (Pen pen = new Pen(c)) g.DrawPath(pen, path);
        }

        private static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ==================== 交互 ====================

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int b = HitButton(e.Location);
            if (b != _hoverBtn) { _hoverBtn = b; Invalidate(); }
            int row = HitRow(e.Location);
            if (row != _hoverRow) { _hoverRow = row; Invalidate(); }
            Cursor = (b >= 0 || row >= 0) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverBtn != -1 || _hoverRow != -1) { _hoverBtn = -1; _hoverRow = -1; Invalidate(); }
            Cursor = Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            // 右键菜单必须放在左键判断之前
            if (e.Button == MouseButtons.Right)
            {
                int hr = HitRow(e.Location);
                if (hr >= 0)
                {
                    _selectedRow = hr;
                    Invalidate();
                    ShowRowMenu(hr, e.Location);
                }
                return;
            }

            if (e.Button != MouseButtons.Left) return;

            if (_btnClose.Contains(e.Location)) { Close(); return; }
            if (_busy && _btnStop.Contains(e.Location)) { StopNow(); return; }
            if (_btnSort.Contains(e.Location))
            {
                _sortDesc = !_sortDesc;
                SortProxies();
                SetStatus(_sortDesc ? "已按延迟降序" : "已按延迟升序", Theme.SubText);
                Invalidate();
                return;
            }

            int b = HitButton(e.Location);
            if (b >= 0) { ClickButton(b); return; }

            int row = HitRow(e.Location);
            if (row >= 0) { _selectedRow = row; Invalidate(); return; }

            if (e.Y < TitleH)
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 0x2, 0);
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            int row = HitRow(e.Location);
            if (row >= 0 && row < _proxies.Count) SetProxy(_proxies[row].Addr);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int listH = Height - StatusH - (TitleH + ToolbarH);
            int maxScroll = Math.Max(0, _proxies.Count * Pitch - Gap - listH);
            _scroll -= e.Delta / 120 * Pitch;
            if (_scroll < 0) _scroll = 0;
            if (_scroll > maxScroll) _scroll = maxScroll;
            Invalidate();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

        private int HitButton(Point pt)
        {
            for (int i = 0; i < _btnRects.Count; i++)
                if (_btnRects[i].Height > 0 && _btnRects[i].Contains(pt)) return i;
            if (_busy && _btnStop.Contains(pt)) return 99;
            return -1;
        }

        private int HitRow(Point pt)
        {
            int listTop = TitleH + ToolbarH;
            if (pt.Y < listTop) return -1;
            int y = pt.Y - listTop + _scroll;
            if (y < 0) return -1;
            int idx = y / Pitch;
            if (idx < 0 || idx >= _proxies.Count) return -1;
            return idx;
        }

        private void ClickButton(int i)
        {
            if (_busy && i != 99) return;
            switch (i)
            {
                case 0: StartScan(); break;
                case 1: StartTest(TestUrl); break;
                case 2: StartCustomTest(); break;
                case 3: DoUnsetProxy(); break;
                case 4: DoExport(); break;
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            base.WndProc(ref m);
            if (m.Msg == WM_NCHITTEST && (int)m.Result == 1)
            {
                int lp = m.LParam.ToInt32();
                Point p = PointToClient(new Point(lp & 0xFFFF, (lp >> 16) & 0xFFFF));
                int b = 6;
                bool left = p.X <= b, right = p.X >= Width - b, top = p.Y <= b, bottom = p.Y >= Height - b;
                if (left && top) { m.Result = (IntPtr)13; return; }
                if (right && top) { m.Result = (IntPtr)14; return; }
                if (left && bottom) { m.Result = (IntPtr)16; return; }
                if (right && bottom) { m.Result = (IntPtr)17; return; }
                if (left) { m.Result = (IntPtr)10; return; }
                if (right) { m.Result = (IntPtr)11; return; }
                if (top) { m.Result = (IntPtr)12; return; }
                if (bottom) { m.Result = (IntPtr)15; return; }
            }
        }

        // ==================== 业务 ====================

        private void StopNow()
        {
            _cancel = true;
            _opId++;
            _busy = false;
            _progress = -1;
            _busyText = "";
            SetStatus("已停止", Theme.Yellow);
            Invalidate();
            ThreadPool.QueueUserWorkItem(delegate { AbortActiveRequests(); });
        }

        private void StartScan()
        {
            if (_busy) return;
            _busy = true; _cancel = false;
            _opId++;
            int myOp = _opId;
            _progress = 0; _busyText = "准备扫描...";
            SetStatus("正在扫描局域网...", Theme.SubText);
            Invalidate();

            Thread t = new Thread(delegate()
            {
                try
                {
                    List<string> ranges = DetectLocalRanges();
                    if (ranges.Count == 0)
                    {
                        Ui(delegate
                        {
                            string manual = InputDialog.Show(this, "输入网段", "无法自动识别网段，请手动输入（如 192.168.1.0/24）：", "192.168.1.0/24");
                            if (!string.IsNullOrWhiteSpace(manual)) ranges.Add(manual.Trim());
                        });
                        if (ranges.Count == 0) { Ui(delegate { if (myOp == _opId) FinishScan(); }); return; }
                    }

                    List<string> targets = BuildTargets(ranges);
                    if (targets.Count == 0) { Ui(delegate { if (myOp == _opId) FinishScan(); }); return; }

                    List<Hit> hits = ScanPorts(targets);
                    if (_cancel || myOp != _opId) { Ui(delegate { if (myOp == _opId) FinishScan(true); }); return; }

                    List<Proxy> found = ProbeAll(hits);
                    if (myOp != _opId) return;

                    Ui(delegate
                    {
                        if (myOp != _opId) return;
                        _proxies.Clear();
                        _proxies.AddRange(found);
                        _selectedRow = -1;
                        _scroll = 0;
                    });

                    if (found.Count > 0) TestProxies(TestUrl);

                    Ui(delegate
                    {
                        if (myOp != _opId) return;
                        SortProxies();
                        SaveCache();
                        SetStatus("扫描完成：发现 " + found.Count + " 个代理端口", found.Count > 0 ? Theme.Green : Theme.Yellow);
                        FinishScan();
                    });
                }
                catch (Exception ex)
                {
                    Ui(delegate { if (myOp != _opId) return; SetStatus("扫描失败: " + ex.Message, Theme.Red); FinishScan(); });
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void FinishScan(bool canceled = false)
        {
            _busy = false; _progress = -1; _busyText = "";
            if (canceled) SetStatus("已停止扫描", Theme.Yellow);
            Invalidate();
        }

        private void StartTest(string url)
        {
            if (_busy) return;
            if (_proxies.Count == 0) { SetStatus("列表为空，请先扫描", Theme.Yellow); return; }

            _busy = true; _cancel = false;
            _opId++;
            int myOp = _opId;
            _progress = 0; _busyText = "测试中...";
            SetStatus("正在测试延迟...", Theme.SubText);
            Invalidate();

            Thread t = new Thread(delegate()
            {
                try
                {
                    TestProxies(url);
                    Ui(delegate
                    {
                        if (myOp != _opId) return;
                        SortProxies();
                        SaveCache();
                        if (_cancel)
                        {
                            SetStatus("已停止测速", Theme.Yellow);
                        }
                        else
                        {
                            int ok = _proxies.FindAll(delegate(Proxy p) { return p.Ok; }).Count;
                            Proxy fast = Fastest();
                            SetStatus(ok == 0 ? "全部不可用"
                                : ("测试完成：" + ok + "/" + _proxies.Count + " 可用，最快 " + fast.Addr + " (" + fast.Latency + "ms)"),
                                ok == 0 ? Theme.Yellow : Theme.Green);
                        }
                        _busy = false; _progress = -1; _busyText = "";
                        Invalidate();
                    });
                }
                catch (Exception ex)
                {
                    Ui(delegate { if (myOp != _opId) return; SetStatus("测试失败: " + ex.Message, Theme.Red); _busy = false; Invalidate(); });
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void StartCustomTest()
        {
            if (_busy) return;
            string url = InputDialog.Show(this, "自定义网址", "输入要测试的网址：", "https://www.google.com/");
            if (string.IsNullOrWhiteSpace(url)) return;
            url = url.Trim();
            if (!url.StartsWith("http://") && !url.StartsWith("https://")) url = "https://" + url;
            StartTest(url);
        }

        private List<string> BuildTargets(List<string> ranges)
        {
            List<string> targets = new List<string>();
            foreach (string r in ranges)
            {
                List<string> ips;
                try { ips = ExpandCidr(r); } catch { continue; }
                foreach (string ip in ips)
                    if (IsPrivate(ip) && !targets.Contains(ip)) targets.Add(ip);
            }

            List<string> self = new List<string>();
            try
            {
                foreach (IPAddress a in Dns.GetHostAddresses(Dns.GetHostName()))
                    if (a.AddressFamily == AddressFamily.InterNetwork) self.Add(a.ToString());
            }
            catch { }
            targets.RemoveAll(delegate(string x) { return self.Contains(x); });

            Ui(delegate { _busyText = "扫描 " + targets.Count + " 台主机..."; });
            return targets;
        }

        private List<Hit> ScanPorts(List<string> targets)
        {
            List<Hit> hits = new List<Hit>();
            object gate = new object();
            int op = _opId;
            const int batch = 64;

            for (int start = 0; start < targets.Count; start += batch)
            {
                if (_cancel || op != _opId) break;
                int end = Math.Min(start + batch - 1, targets.Count - 1);
                List<string> slice = targets.GetRange(start, end - start + 1);

                List<string> jobs = new List<string>();
                foreach (string ip in slice)
                    foreach (int port in _ports) jobs.Add(ip + "|" + port);

                ParallelOptions opts = new ParallelOptions();
                opts.MaxDegreeOfParallelism = 384;
                Parallel.ForEach(jobs, opts, delegate(string job)
                {
                    if (_cancel) return;
                    int bar = job.IndexOf('|');
                    string ipStr = job.Substring(0, bar);
                    int port = int.Parse(job.Substring(bar + 1));

                    IPAddress ip;
                    if (!IPAddress.TryParse(ipStr, out ip)) return;

                    Socket sock = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        sock.NoDelay = true;
                        sock.Blocking = false;
                        IAsyncResult ar = sock.BeginConnect(ip, port, null, null);
                        if (ar.AsyncWaitHandle.WaitOne(600))
                        {
                            try { sock.EndConnect(ar); } catch { }
                            if (sock.Connected) lock (gate) hits.Add(new Hit { Ip = ipStr, Port = port });
                        }
                    }
                    catch { }
                    finally { try { sock.Close(); } catch { } }
                });

                int done = end + 1;
                int pct = done * 100 / targets.Count;
                int hc = hits.Count;
                if (op == _opId)
                    Ui(delegate { _progress = pct; _busyText = "扫描中 " + done + "/" + targets.Count + "，已发现 " + hc; Invalidate(); });
            }
            return hits;
        }

        private List<Proxy> ProbeAll(List<Hit> hits)
        {
            List<Proxy> found = new List<Proxy>();
            object gate = new object();
            int done = 0;
            int total = hits.Count;

            ParallelOptions opts = new ParallelOptions();
            opts.MaxDegreeOfParallelism = 16;
            Parallel.ForEach(hits, opts, delegate(Hit h)
            {
                if (_cancel) return;
                string http = ProbeHttp(h.Ip, h.Port, 1500);
                string socks = ProbeSocks5(h.Ip, h.Port, 1500);
                if (http == "ok" || socks == "ok")
                {
                    Proxy p = new Proxy();
                    p.Addr = h.Ip + ":" + h.Port;
                    p.Type = (http == "ok" && socks == "ok") ? "HTTP+SOCKS5" : (http == "ok" ? "HTTP" : "SOCKS5");
                    lock (gate) found.Add(p);
                }
                int d = Interlocked.Increment(ref done);
                if ((d % 3) == 0 || d == total)
                {
                    int pct = d * 100 / Math.Max(1, total);
                    Ui(delegate { _progress = pct; _busyText = "识别代理 " + d + "/" + total; Invalidate(); });
                }
            });
            return found;
        }

        private void TestProxies(string url)
        {
            int total = _proxies.Count;
            if (total == 0) return;
            int op = _opId;
            int done = 0;
            int okCount = 0;
            object gate = new object();

            ParallelOptions opts = new ParallelOptions();
            opts.MaxDegreeOfParallelism = 8;

            Parallel.For(0, total, opts, delegate(int i)
            {
                if (_cancel || op != _opId) return;

                Proxy p;
                lock (gate) { p = _proxies[i]; }

                Result r = TestOne(p.Addr, url, 8000);
                if (op != _opId) return;

                lock (gate)
                {
                    p.Code = r.Code;
                    p.Err = r.Err;
                    p.Tested = DateTime.Now.ToString("MM-dd HH:mm");
                    p.Ok = r.Ok;
                    p.Latency = r.Ok ? r.Ms : -1;
                    if (r.Ok) okCount++;
                    done++;
                }

                int d = done;
                int pct = d * 100 / total;
                if ((d % 2) == 0 || d == total)
                    Ui(delegate { _progress = pct; _busyText = "测试中 " + d + "/" + total; Invalidate(); });
            });

            if (_cancel || op != _opId) return;

            // 全部失败时，可能是本机 HTTPS 受限，用 HTTP 端点复测一次
            if (okCount == 0 && url.StartsWith("https://www.google.com", StringComparison.OrdinalIgnoreCase))
            {
                Ui(delegate { _busyText = "HTTPS 受限，改用 HTTP 复测..."; Invalidate(); });
                Parallel.For(0, total, opts, delegate(int i)
                {
                    if (_cancel) return;
                    Proxy p;
                    lock (gate) { p = _proxies[i]; }
                    Result r = TestOne(p.Addr, HttpFallbackUrl, 8000);
                    if (r.Ok)
                        lock (gate) { p.Ok = true; p.Latency = r.Ms; p.Code = r.Code; p.Err = "HTTP测得"; }
                });
            }
        }

        private void SetProxy(string addr)
        {
            if (SetSystemProxy(addr))
            {
                SetStatus("已设置代理 -> " + addr, Theme.Green);
                Invalidate();
                return;
            }

            if (!_isAdmin && !_canWrite && PromptYesNo("修改系统代理需要管理员权限。\r\n\r\n是否以管理员身份设置 " + addr + " ？"))
            {
                if (RelaunchElevated("--apply " + addr))
                {
                    SetStatus("已通过管理员权限设置 -> " + addr, Theme.Green);
                    Invalidate();
                    return;
                }
            }
            SetStatus("设置代理失败（被拦截）", Theme.Red);
            Invalidate();
        }

        private void DoUnsetProxy()
        {
            if (SetSystemProxy(null)) { SetStatus("已取消代理，恢复直连", Theme.Green); Invalidate(); return; }

            if (!_isAdmin && !_canWrite && PromptYesNo("取消系统代理需要管理员权限。\r\n\r\n是否以管理员身份取消？"))
            {
                if (RelaunchElevated("--unset"))
                {
                    SetStatus("已通过管理员权限取消代理", Theme.Green);
                    Invalidate();
                    return;
                }
            }
            SetStatus("取消代理失败（被拦截）", Theme.Red);
            Invalidate();
        }

        private void DoExport()
        {
            if (_proxies.Count == 0) { SetStatus("列表为空", Theme.Yellow); return; }
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "CSV 文件|*.csv";
                dlg.FileName = "proxy-list.csv";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Addr,Type,Latency,Code,Ok,Err,Tested");
                foreach (Proxy p in _proxies)
                    sb.AppendLine(string.Join(",", new string[] {
                        p.Addr, p.Type, p.Latency.ToString(), p.Code.ToString(),
                        p.Ok ? "True" : "False", p.Err, p.Tested }));
                try
                {
                    File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
                    SetStatus("已导出: " + dlg.FileName, Theme.Green);
                }
                catch (Exception ex) { SetStatus("导出失败: " + ex.Message, Theme.Red); }
            }
            Invalidate();
        }

        private void ShowRowMenu(int idx, Point pt)
        {
            if (idx < 0 || idx >= _proxies.Count) return;
            Proxy p = _proxies[idx];

            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new DarkMenuRenderer();
            m.BackColor = Theme.Panel;
            m.ForeColor = Theme.Text;

            m.Items.Add("设为系统代理", null, delegate { SetProxy(p.Addr); });
            m.Items.Add("单独测速", null, delegate { StartSingleTest(idx); });
            m.Items.Add("复制地址", null, delegate
            {
                try { Clipboard.SetText(p.Addr); SetStatus("已复制 " + p.Addr, Theme.SubText); } catch { }
            });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("从列表移除", null, delegate
            {
                if (idx < _proxies.Count)
                {
                    _proxies.RemoveAt(idx);
                    _selectedRow = -1;
                    SaveCache();
                    Invalidate();
                }
            });
            m.Show(this, pt);
        }

        private void StartSingleTest(int idx)
        {
            if (idx < 0 || idx >= _proxies.Count) return;
            Proxy p = _proxies[idx];
            SetStatus("正在测试 " + p.Addr + " ...", Theme.SubText);

            Thread t = new Thread(delegate()
            {
                Result r = TestOne(p.Addr, TestUrl, 8000);
                Ui(delegate
                {
                    p.Ok = r.Ok;
                    p.Latency = r.Ok ? r.Ms : -1;
                    p.Code = r.Code;
                    p.Err = r.Err;
                    p.Tested = DateTime.Now.ToString("MM-dd HH:mm");
                    SortProxies();
                    SaveCache();
                    SetStatus(r.Ok ? ("可用 " + r.Ms + " ms") : "不通", r.Ok ? Theme.Green : Theme.Yellow);
                    Invalidate();
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        private Proxy Fastest()
        {
            Proxy best = null;
            foreach (Proxy p in _proxies)
                if (p.Ok && (best == null || p.Latency < best.Latency)) best = p;
            return best;
        }

        private void SortProxies()
        {
            _proxies.Sort(delegate(Proxy a, Proxy b)
            {
                int ra = a.Ok ? 0 : (a.Tested.Length > 0 ? 2 : 1);
                int rb = b.Ok ? 0 : (b.Tested.Length > 0 ? 2 : 1);
                if (ra != rb) return ra.CompareTo(rb);
                int la = a.Latency >= 0 ? a.Latency : 999999;
                int lb = b.Latency >= 0 ? b.Latency : 999999;
                return _sortDesc ? lb.CompareTo(la) : la.CompareTo(lb);
            });
        }

        private void SetStatus(string msg, Color c)
        {
            _status = msg;
            _statusColor = c;
            Invalidate();
        }

        private void Ui(MethodInvoker action)
        {
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch { }
        }

        // ==================== 网络 ====================

        private List<string> DetectLocalRanges()
        {
            List<string> result = new List<string>();
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    IPInterfaceProperties props = ni.GetIPProperties();
                    bool hasGw = false;
                    foreach (GatewayIPAddressInformation gw in props.GatewayAddresses)
                        if (gw.Address.AddressFamily == AddressFamily.InterNetwork) { hasGw = true; break; }
                    if (!hasGw) continue;

                    foreach (UnicastIPAddressInformation ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string ip = ua.Address.ToString();
                        if (!IsPrivate(ip)) continue;
                        if (ua.IPv4Mask == null) continue;

                        int bits = 0;
                        foreach (byte mb in ua.IPv4Mask.GetAddressBytes())
                            for (int k = 7; k >= 0; k--)
                            {
                                if (((mb >> k) & 1) == 1) bits++;
                                else break;
                            }
                        if (bits < 8) continue;
                        if (bits < 21) bits = 21;
                        if (bits > 30) bits = 30;

                        string cidr = NetworkOf(ip, bits) + "/" + bits;
                        if (!result.Contains(cidr)) result.Add(cidr);
                    }
                }
            }
            catch { }
            return result;
        }

        private static string NetworkOf(string ip, int bits)
        {
            byte[] b = IPAddress.Parse(ip).GetAddressBytes();
            uint v = ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
            uint mask = bits == 0 ? 0u : (uint)(0xFFFFFFFFL << (32 - bits));
            v = v & mask;
            return ((v >> 24) & 0xFF) + "." + ((v >> 16) & 0xFF) + "." + ((v >> 8) & 0xFF) + "." + (v & 0xFF);
        }

        private static List<string> ExpandCidr(string cidr)
        {
            string[] parts = cidr.Split('/');
            byte[] b = IPAddress.Parse(parts[0]).GetAddressBytes();
            int bits = int.Parse(parts[1]);
            if (bits < 1 || bits > 30) throw new FormatException("bad prefix");

            uint baseInt = ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
            uint mask = (uint)(0xFFFFFFFFL << (32 - bits));
            uint net = baseInt & mask;
            long count = 1L << (32 - bits);

            List<string> list = new List<string>();
            for (long i = 1; i < count - 1; i++)
            {
                long v = net + i;
                list.Add(((v >> 24) & 0xFF) + "." + ((v >> 16) & 0xFF) + "." + ((v >> 8) & 0xFF) + "." + (v & 0xFF));
            }
            return list;
        }

        private static bool IsPrivate(string ip)
        {
            string[] p = ip.Split('.');
            int a, b;
            if (p.Length != 4) return false;
            if (!int.TryParse(p[0], out a) || !int.TryParse(p[1], out b)) return false;
            if (a == 10) return true;
            if (a == 172 && b >= 16 && b <= 31) return true;
            if (a == 192 && b == 168) return true;
            if (a == 100 && b >= 64 && b <= 127) return true;
            return false;
        }

        private static string ProbeHttp(string ip, int port, int timeoutMs)
        {
            Socket s = Connect(ip, port, timeoutMs);
            if (s == null) return "";
            try
            {
                string req = "CONNECT www.google.com:443 HTTP/1.1\r\nHost: www.google.com:443\r\nProxy-Connection: keep-alive\r\n\r\n";
                s.Send(Encoding.ASCII.GetBytes(req));
                string head = ReadHead(s, 2048);
                if (head.Length == 0) return "";
                string first = head.Split('\n')[0].Trim();
                if (first.Contains(" 200")) return "ok";
                if (first.Contains(" 407")) return "auth";
                return "no";
            }
            catch { return ""; }
            finally { try { s.Close(); } catch { } }
        }

        private static string ProbeSocks5(string ip, int port, int timeoutMs)
        {
            Socket s = Connect(ip, port, timeoutMs);
            if (s == null) return "";
            try
            {
                s.Send(new byte[] { 5, 1, 0 });
                byte[] g = ReadN(s, 2);
                if (g == null || g[0] != 5) return "";
                if (g[1] == 0xFF) return "auth";
                if (g[1] != 0) return "";

                byte[] host = Encoding.ASCII.GetBytes("www.google.com");
                byte[] req = new byte[7 + host.Length];
                req[0] = 5; req[1] = 1; req[2] = 0; req[3] = 3; req[4] = (byte)host.Length;
                Array.Copy(host, 0, req, 5, host.Length);
                req[req.Length - 2] = 0x01;
                req[req.Length - 1] = 0xBB;
                s.Send(req);
                byte[] r = ReadN(s, 4);
                if (r == null || r[0] != 5) return "";
                return r[1] == 0 ? "ok" : "deny";
            }
            catch { return ""; }
            finally { try { s.Close(); } catch { } }
        }

        private static Socket Connect(string ip, int port, int timeoutMs)
        {
            try
            {
                Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                s.NoDelay = true;
                IAsyncResult ar = s.BeginConnect(ip, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) { try { s.Close(); } catch { } return null; }
                s.EndConnect(ar);
                s.ReceiveTimeout = timeoutMs;
                s.SendTimeout = timeoutMs;
                return s;
            }
            catch { return null; }
        }

        private static byte[] ReadN(Socket s, int n)
        {
            byte[] buf = new byte[n];
            int off = 0;
            while (off < n)
            {
                int r = s.Receive(buf, off, n - off, SocketFlags.None);
                if (r <= 0) return null;
                off += r;
            }
            return buf;
        }

        private static string ReadHead(Socket s, int max)
        {
            StringBuilder sb = new StringBuilder();
            byte[] one = new byte[1];
            while (sb.Length < max)
            {
                int r = s.Receive(one, 0, 1, SocketFlags.None);
                if (r <= 0) break;
                sb.Append((char)one[0]);
                if (sb.Length >= 4 && sb.ToString(sb.Length - 4, 4) == "\r\n\r\n") break;
            }
            return sb.ToString();
        }

        private static readonly List<HttpWebRequest> _active = new List<HttpWebRequest>();
        private static readonly object _activeLock = new object();

        private static void AbortActiveRequests()
        {
            HttpWebRequest[] arr;
            lock (_activeLock) { arr = _active.ToArray(); _active.Clear(); }
            foreach (HttpWebRequest rq in arr) { try { rq.Abort(); } catch { } }
        }

        private static Result TestOne(string proxyAddr, string url, int timeoutMs)
        {
            Result r = new Result();
            Stopwatch sw = Stopwatch.StartNew();
            HttpWebRequest req = null;
            try
            {
                req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.KeepAlive = false;
                req.AllowAutoRedirect = true;
                req.Proxy = new WebProxy("http://" + proxyAddr);
                lock (_activeLock) _active.Add(req);

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    r.Code = (int)resp.StatusCode;

                sw.Stop();
                r.Ms = (int)sw.ElapsedMilliseconds;
                r.Ok = r.Code >= 200 && r.Code < 400;
            }
            catch (WebException we)
            {
                sw.Stop();
                r.Ms = (int)sw.ElapsedMilliseconds;
                HttpWebResponse hr = we.Response as HttpWebResponse;
                if (hr != null)
                {
                    r.Code = (int)hr.StatusCode;
                    r.Ok = r.Code >= 200 && r.Code < 400;
                    try { hr.Close(); } catch { }
                }
                r.Err = we.Status.ToString();
            }
            catch (Exception e)
            {
                sw.Stop();
                r.Ms = (int)sw.ElapsedMilliseconds;
                r.Err = e.GetType().Name;
            }
            finally
            {
                if (req != null) { lock (_activeLock) _active.Remove(req); }
            }
            return r;
        }

        // ==================== 系统代理 ====================

        private static bool SetSystemProxy(string addr)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegPath, true))
                {
                    if (key == null) return false;
                    if (string.IsNullOrEmpty(addr))
                    {
                        key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                    }
                    else
                    {
                        object ov = key.GetValue("ProxyOverride");
                        if (ov == null || ov.ToString().Length == 0)
                            key.SetValue("ProxyOverride", "localhost;127.*;10.*;172.*;192.168.*;<local>", RegistryValueKind.String);
                        key.SetValue("ProxyServer", addr, RegistryValueKind.String);
                        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                    }
                }
                InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
                return true;
            }
            catch { return false; }
        }

        private static string GetSystemProxyText()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegPath, false))
                {
                    if (key == null) return "(直连)";
                    object en = key.GetValue("ProxyEnable");
                    object sv = key.GetValue("ProxyServer");
                    if (en != null && Convert.ToInt32(en) == 1 && sv != null) return sv.ToString();
                }
            }
            catch { }
            return "(直连)";
        }

        private static bool CanWriteProxySettings()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RegPath, true)) return k != null;
            }
            catch { return false; }
        }

        private static bool IsAdministrator()
        {
            try
            {
                WindowsPrincipal p = new WindowsPrincipal(WindowsIdentity.GetCurrent());
                return p.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private static bool PromptYesNo(string text)
        {
            try
            {
                const uint MB_YESNO = 0x4, MB_ICONWARNING = 0x30, MB_TOPMOST = 0x40000, MB_SETFOREGROUND = 0x10000;
                return MessageBoxW(IntPtr.Zero, text, "局域网代理工具箱", MB_YESNO | MB_ICONWARNING | MB_TOPMOST | MB_SETFOREGROUND) == 6;
            }
            catch { return false; }
        }

        private static bool RelaunchElevated(string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, arguments);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process p = Process.Start(psi);
                if (p == null) return false;
                p.WaitForExit();
                return p.ExitCode == 0;
            }
            catch { return false; }
        }

        // ==================== 缓存 ====================

        private void LoadCache()
        {
            try
            {
                if (!File.Exists(_cachePath))
                {
                    string alt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProxyTool", "proxy-cache.txt");
                    if (File.Exists(alt)) _cachePath = alt;
                    else return;
                }

                foreach (string raw in File.ReadAllLines(_cachePath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string k = line.Substring(0, eq), v = line.Substring(eq + 1);

                    if (k == "updated") { DateTime t; if (DateTime.TryParse(v, out t)) _cacheTime = t; }
                    else if (k == "proxy")
                    {
                        string[] f = v.Split('|');
                        if (f.Length < 8 || f[0].Length == 0) continue;
                        Proxy p = new Proxy();
                        p.Addr = f[0];
                        p.Type = f[1];
                        int lat; if (int.TryParse(f[2], out lat)) p.Latency = lat;
                        int code; if (int.TryParse(f[3], out code)) p.Code = code;
                        p.Ok = f[4] == "1";
                        p.Err = f[6];
                        p.Tested = f[7];
                        _proxies.Add(p);
                    }
                }
            }
            catch { }
        }

        private void SaveCache()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# ProxyTool cache v1");
                sb.AppendLine("updated=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                foreach (Proxy p in _proxies)
                    sb.AppendLine("proxy=" + string.Join("|", new string[] {
                        p.Addr, p.Type, p.Latency.ToString(), p.Code.ToString(),
                        p.Ok ? "1" : "0", "", p.Err, p.Tested }));

                File.WriteAllText(_cachePath, sb.ToString(), new UTF8Encoding(false));
                _cacheTime = DateTime.Now;
            }
            catch
            {
                string alt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProxyTool");
                try
                {
                    Directory.CreateDirectory(alt);
                    _cachePath = Path.Combine(alt, "proxy-cache.txt");
                    SaveCache();
                }
                catch { }
            }
        }

        private string CacheAgeText()
        {
            if (_cacheTime == DateTime.MinValue) return "无缓存";
            TimeSpan span = DateTime.Now - _cacheTime;
            if (span.TotalMinutes < 1) return "刚刚";
            if (span.TotalHours < 1) return (int)span.TotalMinutes + " 分钟前";
            if (span.TotalDays < 1) return span.TotalHours.ToString("0.0") + " 小时前";
            return (int)span.TotalDays + " 天前";
        }

        // ==================== 调试入口 ====================

        internal string BenchStop()
        {
            _proxies.Clear();
            for (int i = 0; i < 8; i++)
            {
                Proxy p = new Proxy();
                p.Addr = "10.255.255." + (i + 1) + ":8080";
                p.Type = "HTTP";
                _proxies.Add(p);
            }

            StringBuilder sb = new StringBuilder();

            Thread t1 = new Thread(delegate() { try { TestProxies(TestUrl); } catch { } });
            t1.IsBackground = true;
            t1.Start();
            Thread.Sleep(1200);
            Stopwatch swUi = Stopwatch.StartNew();
            StopNow();
            swUi.Stop();
            sb.AppendLine("ui-response: " + swUi.ElapsedMilliseconds + " ms, busy=" + _busy);

            Stopwatch sw1 = Stopwatch.StartNew();
            bool j1 = t1.Join(25000);
            sw1.Stop();
            sb.AppendLine("worker-finished-after: " + sw1.ElapsedMilliseconds + " ms, joined=" + j1);

            List<string> targets = new List<string>();
            for (int i = 1; i <= 254; i++) targets.Add("10.255.255." + i);
            _cancel = false;
            Thread t2 = new Thread(delegate() { try { ScanPorts(targets); } catch { } });
            t2.IsBackground = true;
            t2.Start();
            Thread.Sleep(1200);
            Stopwatch sw2 = Stopwatch.StartNew();
            _cancel = true;
            bool j2 = t2.Join(20000);
            sw2.Stop();
            sb.AppendLine("scan-cancel: " + sw2.ElapsedMilliseconds + " ms, joined=" + j2);

            return sb.ToString();
        }
    }

    // ==================== 输入对话框 ====================

    internal class InputDialog : Form
    {
        private TextBox _box;
        private bool _ok;

        private InputDialog(string title, string label, string def)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(520, 170);
            BackColor = Theme.Bg;
            ShowInTaskbar = false;

            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.BackColor = Theme.Bg;
            body.Paint += delegate(object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen p = new Pen(Theme.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);

                using (Font f = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold))
                using (SolidBrush b = new SolidBrush(Theme.Text))
                    g.DrawString(title, f, b, 18, 14);

                using (Font f = new Font("Microsoft YaHei UI", 8.5f))
                using (SolidBrush b = new SolidBrush(Theme.SubText))
                    g.DrawString(label, f, b, 18, 44);

                Rectangle box = new Rectangle(18, 70, Width - 36, 32);
                using (SolidBrush b = new SolidBrush(Theme.PanelSunken)) g.FillRectangle(b, box);
                using (Pen p = new Pen(Theme.Border)) g.DrawRectangle(p, box);
            };

            _box = new TextBox();
            _box.Text = def;
            _box.BorderStyle = BorderStyle.None;
            _box.BackColor = Theme.PanelSunken;
            _box.ForeColor = Theme.Text;
            _box.Font = new Font("Microsoft YaHei UI", 9.5f);
            _box.SetBounds(24, 77, 470, 20);
            _box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { _ok = true; Close(); }
                if (e.KeyCode == Keys.Escape) Close();
            };

            Button ok = new Button();
            ok.Text = "确定";
            ok.FlatStyle = FlatStyle.Flat;
            ok.FlatAppearance.BorderSize = 0;
            ok.BackColor = Theme.Accent;
            ok.ForeColor = Color.White;
            ok.SetBounds(Width - 190, 118, 80, 30);
            ok.Click += delegate { _ok = true; Close(); };

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.FlatAppearance.BorderSize = 0;
            cancel.BackColor = Theme.Panel;
            cancel.ForeColor = Theme.Text;
            cancel.SetBounds(Width - 100, 118, 80, 30);
            cancel.Click += delegate { Close(); };

            body.Controls.Add(_box);
            body.Controls.Add(ok);
            body.Controls.Add(cancel);
            Controls.Add(body);
        }

        public static string Show(IWin32Window owner, string title, string label, string def)
        {
            using (InputDialog d = new InputDialog(title, label, def))
            {
                d.ShowDialog(owner);
                return d._ok ? d._box.Text : null;
            }
        }
    }

    // ==================== 深色右键菜单 ====================

    internal class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected ? Color.White : Theme.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
    }

    internal class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Panel; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Panel; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Panel; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Panel; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemSelected { get { return Theme.Accent; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.Accent; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.Accent; } }
        public override Color MenuItemBorder { get { return Theme.Border; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Border; } }
    }

    // ==================== 入口 ====================

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length >= 2 && args[0].ToLowerInvariant() == "--apply")
                Environment.Exit(SetProxySilent(args[1]) ? 0 : 1);

            if (args.Length >= 1 && args[0].ToLowerInvariant() == "--unset")
                Environment.Exit(SetProxySilent(null) ? 0 : 1);

            if (args.Length >= 1 && args[0].ToLowerInvariant() == "--bench")
            {
                using (MainForm f = new MainForm(true))
                    File.WriteAllText("bench.txt", f.BenchStop(), Encoding.UTF8);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length >= 2 && args[0].ToLowerInvariant() == "--shot")
            {
                using (MainForm f = new MainForm(true))
                {
                    f.Size = new Size(1000, 640);
                    if (args.Length >= 3 && args[2] == "busy") f.SimulateBusy();
                    using (Bitmap bmp = new Bitmap(1000, 640, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(bmp)) f.RenderPreview(g);
                        bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                return;
            }

            Application.Run(new MainForm());
        }

        private static bool SetProxySilent(string addr)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true))
                {
                    if (key == null) return false;
                    if (string.IsNullOrEmpty(addr))
                    {
                        key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                    }
                    else
                    {
                        key.SetValue("ProxyServer", addr, RegistryValueKind.String);
                        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                    }
                }
                InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
                return true;
            }
            catch { return false; }
        }

        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
    }
}