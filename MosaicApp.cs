using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WeChatMosaic
{
    // ===== 一个遮挡区域的数据模型 =====
    public class Region
    {
        public double X;
        public double Y;
        public double W;
        public double H;
        public string Style = "blur";   // blur | mosaic | solid
        public string Color = "#FF202020"; // ARGB，纯色用（alpha 由 Opacity 覆盖）
        public double Opacity = 0.85;
    }

    public static class Config
    {
        public static List<Region> Regions = new List<Region>();
        public static bool Locked = false;
        public static int HotkeyVk = 0xDC;      // 主键，默认 \ 键
        public static uint HotkeyMods = 0x0004; // 修饰键，默认 Shift(0x4)。Ctrl=0x2 Alt=0x1 Win=0x8
        public static bool FollowEnabled = true;
        public static string FollowMode = "smart"; // smart | visible | running | manual
        public static int PollMs = 300;
        public static string TargetProcesses = "WeChat,Weixin"; // 跟随的目标进程名，逗号分隔
        public static bool InvertFollow = false; // true = 反向：目标不可见时才遮


        // 把修饰键+主键转成可读文字，如 "Shift+|"、"Ctrl+Alt+M"
        public static string HotkeyText()
        {
            var sb = new StringBuilder();
            if ((HotkeyMods & 0x2) != 0) sb.Append("Ctrl+");
            if ((HotkeyMods & 0x1) != 0) sb.Append("Alt+");
            if ((HotkeyMods & 0x4) != 0) sb.Append("Shift+");
            if ((HotkeyMods & 0x8) != 0) sb.Append("Win+");
            int vk = HotkeyVk;
            if (vk >= 0x30 && vk <= 0x39) sb.Append((char)vk);           // 0-9
            else if (vk >= 0x41 && vk <= 0x5A) sb.Append((char)vk);      // A-Z
            else if (vk == 0xDC) sb.Append("|");
            else if (vk == 0xC0) sb.Append("`");
            else if (vk == 0x20) sb.Append("空格");
            else if (vk == 0x0D) sb.Append("Enter");
            else if (vk >= 0x70 && vk <= 0x7B) sb.Append("F" + (vk - 0x6F)); // F1-F12
            else sb.Append("键0x" + vk.ToString("X2"));
            return sb.ToString();
        }

        private static string PathOf()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            return System.IO.Path.Combine(dir, "mosaic-config.json");
        }

        public static void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("{\"locked\":").Append(Locked ? "true" : "false")
                  .Append(",\"hotkeyVk\":").Append(HotkeyVk)
                  .Append(",\"hotkeyMods\":").Append(HotkeyMods)
                  .Append(",\"followEnabled\":").Append(FollowEnabled ? "true" : "false")
                  .Append(",\"followMode\":\"").Append(FollowMode).Append("\"")
                  .Append(",\"pollMs\":").Append(PollMs)
                  .Append(",\"targetProcesses\":\"").Append(TargetProcesses).Append("\"")
                  .Append(",\"invertFollow\":").Append(InvertFollow ? "true" : "false")
                  .Append(",\"regions\":[");
                for (int i = 0; i < Regions.Count; i++)
                {
                    var r = Regions[i];
                    sb.Append("{\"x\":").Append(r.X)
                      .Append(",\"y\":").Append(r.Y)
                      .Append(",\"w\":").Append(r.W)
                      .Append(",\"h\":").Append(r.H)
                      .Append(",\"style\":\"").Append(r.Style).Append("\"")
                      .Append(",\"color\":\"").Append(r.Color).Append("\"")
                      .Append(",\"opacity\":").Append(r.Opacity.ToString(System.Globalization.CultureInfo.InvariantCulture))
                      .Append("}");
                    if (i < Regions.Count - 1) sb.Append(",");
                }
                sb.Append("]}");
                File.WriteAllText(PathOf(), sb.ToString());
            }
            catch { }
        }

        public static void Load()
        {
            try
            {
                var p = PathOf();
                if (!File.Exists(p)) return;
                var t = File.ReadAllText(p);
                Regions.Clear();
                Locked = t.Contains("\"locked\":true");
                var hv = ExtractNum(t, "hotkeyVk");
                if (hv > 0) HotkeyVk = (int)hv;
                var hm = ExtractNum(t, "hotkeyMods", -1);
                if (hm >= 0) HotkeyMods = (uint)hm;
                FollowEnabled = t.Contains("\"followEnabled\":true");
                FollowMode = ExtractStr(t, "followMode", "smart");
                PollMs = (int)ExtractNum(t, "pollMs", 300);
                if (PollMs < 50) PollMs = 50;
                TargetProcesses = ExtractStr(t, "targetProcesses", "WeChat,Weixin");
                InvertFollow = t.Contains("\"invertFollow\":true");
                foreach (var block in ExtractObjects(t, "regions"))
                {
                    var r = new Region();
                    r.X = ExtractNum(block, "x");
                    r.Y = ExtractNum(block, "y");
                    r.W = ExtractNum(block, "w", 260);
                    r.H = ExtractNum(block, "h", 160);
                    r.Style = ExtractStr(block, "style", "blur");
                    r.Color = ExtractStr(block, "color", "#FF202020");
                    r.Opacity = ExtractNum(block, "opacity", 0.85);
                    Regions.Add(r);
                }
            }
            catch { }
        }

        // 极简 JSON 提取（避免引入外部依赖）
        private static IEnumerable<string> ExtractObjects(string json, string key)
        {
            var list = new List<string>();
            int k = json.IndexOf("\"" + key + "\"");
            if (k < 0) return list;
            int s = json.IndexOf('[', k);
            if (s < 0) return list;
            int depth = 0, start = -1;
            for (int i = s; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '{') { if (depth == 0) start = i; depth++; }
                else if (c == '}') { depth--; if (depth == 0 && start >= 0) { list.Add(json.Substring(start, i - start + 1)); start = -1; } }
                else if (c == ']' && depth == 0) break;
            }
            return list;
        }
        private static double ExtractNum(string j, string key, double def = 0)
        {
            int k = j.IndexOf("\"" + key + "\"");
            if (k < 0) return def;
            int c = j.IndexOf(':', k);
            if (c < 0) return def;
            int i = c + 1;
            while (i < j.Length && (j[i] == ' ' || j[i] == '"')) i++;
            int e = i;
            while (e < j.Length && (char.IsDigit(j[e]) || j[e] == '.' || j[e] == '-' || j[e] == '+')) e++;
            double v;
            if (double.TryParse(j.Substring(i, e - i), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out v)) return v;
            return def;
        }
        private static string ExtractStr(string j, string key, string def)
        {
            int k = j.IndexOf("\"" + key + "\"");
            if (k < 0) return def;
            int c = j.IndexOf(':', k);
            if (c < 0) return def;
            int q1 = j.IndexOf('"', c + 1);
            if (q1 < 0) return def;
            int q2 = j.IndexOf('"', q1 + 1);
            if (q2 < 0) return def;
            return j.Substring(q1 + 1, q2 - q1 - 1);
        }
    }

    // ===== 单个遮挡块窗口 =====
    public class MaskWindow : Window
    {
        public Region Data;
        private Grid root;
        private Border fill;
        private Border handle;      // 右下角调大小
        private TextBlock badge;

        public MaskWindow(Region r)
        {
            Data = r;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            Left = r.X; Top = r.Y; Width = r.W; Height = r.H;
            ShowActivated = false;

            root = new Grid();
            fill = new Border { CornerRadius = new CornerRadius(3) };
            badge = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 10,
                Margin = new Thickness(4, 2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false
            };
            ApplyStyle();
            root.Children.Add(fill);

            // 右下角拖角（编辑模式才显示）
            handle = new Border
            {
                Width = 18, Height = 18,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 255, 193, 7)),
                CornerRadius = new CornerRadius(0, 0, 3, 0),
                Cursor = Cursors.SizeNWSE,
                ToolTip = "拖动调整大小"
            };
            handle.MouseLeftButtonDown += ResizeStart;
            root.Children.Add(handle);

            // 不添加文字标签，遮挡块保持干净

            Content = root;
            MouseLeftButtonDown += DragStart;
            LocationChanged += (s, e) => SyncBack();
            SizeChanged += (s, e) => { SyncBack(); RebuildTextureIfNeeded(); };
            UpdateBadge();
        }

        // 记住穿透目标状态，等窗口句柄创建后再应用
        private bool pendingClickThrough = false;

        // 毛玻璃/马赛克/模糊的纹理是按尺寸生成的，尺寸变了必须重建，否则会出现"一块磨砂一块空白"
        private double lastBuiltW = -1, lastBuiltH = -1;

        private void RebuildTextureIfNeeded()
        {
            if (Math.Abs(Width - lastBuiltW) < 1 && Math.Abs(Height - lastBuiltH) < 1) return;
            ApplyStyle();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyClickThrough(pendingClickThrough);
        }

        private void UpdateBadge()
        {
            // 不在遮挡块上显示任何文字，保持干净
            if (badge != null) badge.Visibility = Visibility.Collapsed;
        }

        public void ApplyStyle()
        {
            double op = Data.Opacity;
            // 每个分支都从干净状态开始，避免残留上一种样式的纹理/透明度
            fill.Child = null;
            fill.Effect = null;
            fill.Opacity = 1;
            fill.Background = Brushes.Transparent;
            if (Data.Style == "glass" || Data.Style == "blend") // blend 为旧版遗留，归入毛玻璃
            {
                // 毛玻璃：取背后颜色 → 调亮 + 半透明 + 磨砂噪点，隐约见影但读不清字
                var baseC = SampleColorBehind();
                var glass = Lighten(baseC, 0.55);          // 往白色拉，更通透
                glass.A = (byte)(Math.Max(op, 0.75) * 255); // 默认更实，遮挡更稳
                fill.Background = new SolidColorBrush(glass);
                fill.Effect = null;
                fill.Opacity = 1;
                fill.Child = MakeFrostCanvas();            // 磨砂颗粒
            }
            else if (Data.Style == "solid")
            {
                // 纯色：不透明度由滑块控制（Data.Opacity），不再用颜色里写死的 alpha
                var sc = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(Data.Color);
                sc.A = (byte)(op * 255);
                fill.Background = new SolidColorBrush(sc);
                fill.Effect = null;
                fill.Opacity = 1;
            }
            else if (Data.Style == "mosaic")
            {
                fill.Background = MakeMosaicBrush();
                fill.Effect = null;
                fill.Opacity = Math.Max(op, 0.92);
            }
            else // blur：深色底 + 噪点，看不出底下内容
            {
                fill.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(op * 255), 0x24, 0x24, 0x24));
                fill.Effect = null;
                fill.Opacity = 1;
                fill.Child = MakeNoiseCanvas();
            }
            lastBuiltW = Width;
            lastBuiltH = Height;
            MainWindow.RequestSave();
            UpdateBadge();
        }

        // 取遮挡块"边缘外侧"一圈的颜色（不采自己区域，无需隐藏窗口，零闪烁）
        private System.Windows.Media.Color cachedSample = System.Windows.Media.Color.FromRgb(0xF5, 0xF5, 0xF5);
        private bool suppressResample = false; // 拖拽调整大小时不重复取色，避免闪烁

        private System.Windows.Media.Color SampleColorBehind()
        {
            try
            {
                if (suppressResample) return cachedSample;

                // 采样遮挡块外圈 8 个点（上下左右 + 四角），绕开自己，直接读屏幕
                IntPtr desk = GetDC(IntPtr.Zero);
                try
                {
                    long r = 0, g = 0, b = 0; int n = 0;
                    int L = (int)Left, T = (int)Top, W = (int)Width, H = (int)Height;
                    int m = 6; // 向外偏移像素
                    // 按边采 8 点（上下左右中点 + 四角）
                    int[][] pts = {
                        new[]{ L + W/2, T - m }, new[]{ L + W/2, T + H + m },
                        new[]{ L - m,   T + H/2 }, new[]{ L + W + m, T + H/2 },
                        new[]{ L - m,   T - m },   new[]{ L + W + m, T - m },
                        new[]{ L - m,   T + H + m }, new[]{ L + W + m, T + H + m }
                    };
                    foreach (var p in pts)
                    {
                        uint px = GetPixel(desk, p[0], p[1]);
                        if (px == 0xFFFFFFFF) continue; // 取屏外失败跳过
                        r += px & 0xFF; g += (px >> 8) & 0xFF; b += (px >> 16) & 0xFF; n++;
                    }
                    if (n > 0)
                        cachedSample = System.Windows.Media.Color.FromRgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
                    return cachedSample;
                }
                finally { ReleaseDC(IntPtr.Zero, desk); }
            }
            catch { return cachedSample; }
        }

        private static System.Drawing.Color SampleAverage(int cx, int cy, int w, int h)
        {
            IntPtr desk = GetDC(IntPtr.Zero);
            try
            {
                long r = 0, g = 0, b = 0; int n = 0;
                // 在块内取 5x5 网格点求平均，更贴近页面底色
                for (int i = -2; i <= 2; i++)
                    for (int j = -2; j <= 2; j++)
                    {
                        int x = cx + i * w / 5, y = cy + j * h / 5;
                        uint px = GetPixel(desk, x, y);
                        r += px & 0xFF; g += (px >> 8) & 0xFF; b += (px >> 16) & 0xFF; n++;
                    }
                return System.Drawing.Color.FromArgb((int)(r / n), (int)(g / n), (int)(b / n));
            }
            finally { ReleaseDC(IntPtr.Zero, desk); }
        }

        private static void DoEvents()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

        // 把颜色往白色提亮 amount(0~1)，让毛玻璃更通透
        private static System.Windows.Media.Color Lighten(System.Windows.Media.Color c, double amount)
        {
            return System.Windows.Media.Color.FromArgb(255,
                (byte)(c.R + (255 - c.R) * amount),
                (byte)(c.G + (255 - c.G) * amount),
                (byte)(c.B + (255 - c.B) * amount));
        }

        // 磨砂颗粒层：密集噪点 + 柔光块，做出扎实的磨砂玻璃质感
        private Canvas MakeFrostCanvas()
        {
            var c = new Canvas();
            var rnd = new Random(Guid.NewGuid().GetHashCode()); // 每次重建换一批，避免固定图案
            double w = Math.Max(Data.W, 40), h = Math.Max(Data.H, 30);

            // 1) 抖动网格噪点：把画面分成均匀小格，每格内随机放 1 个点 → 均匀铺开但不机械
            double cell = 3.5;                     // 格子边长，越小越密
            int cols = Math.Max(1, (int)(w / cell));
            int rows = Math.Max(1, (int)(h / cell));
            for (int gy = 0; gy < rows; gy++)
                for (int gx = 0; gx < cols; gx++)
                {
                    double px = (gx + rnd.NextDouble()) * (w / cols);
                    double py = (gy + rnd.NextDouble()) * (h / rows);
                    double sz = 1.0 + rnd.NextDouble() * 1.4;   // 更细颗粒，配合小格子
                    bool white = rnd.Next(2) == 0;
                    byte alpha = (byte)(white ? (110 + rnd.Next(120)) : (70 + rnd.Next(90)));
                    byte gray = (byte)(white ? 255 : (150 + rnd.Next(80)));
                    var dot = new Ellipse
                    {
                        Width = sz, Height = sz,
                        Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, gray, gray, gray))
                    };
                    Canvas.SetLeft(dot, px);
                    Canvas.SetTop(dot, py);
                    c.Children.Add(dot);
                }

            // 2) 交错的小方块马赛克纹，打散底下文字轮廓
            int grid = 14;
            for (double x = 0; x < w; x += grid)
                for (double y = 0; y < h; y += grid)
                {
                    if (rnd.Next(2) == 0) continue;
                    var sq = new Rectangle
                    {
                        Width = grid - 4, Height = grid - 4,
                        Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(24 + rnd.Next(30)), 255, 255, 255))
                    };
                    Canvas.SetLeft(sq, x); Canvas.SetTop(sq, y);
                    c.Children.Add(sq);
                }

            // 3) 柔和高光：也按网格散开，别挤一块
            int glowCols = 2, glowRows = 3;
            for (int gy = 0; gy < glowRows; gy++)
                for (int gx = 0; gx < glowCols; gx++)
                {
                    var glow = new Ellipse
                    {
                        Width = 50 + rnd.NextDouble() * 90,
                        Height = 50 + rnd.NextDouble() * 90,
                        Fill = new RadialGradientBrush(
                            System.Windows.Media.Color.FromArgb(70, 255, 255, 255),
                            System.Windows.Media.Color.FromArgb(0, 255, 255, 255))
                    };
                    double px = (gx + rnd.NextDouble()) * Math.Max(1, (w - 100) / glowCols);
                    double py = (gy + rnd.NextDouble()) * Math.Max(1, (h - 100) / glowRows);
                    Canvas.SetLeft(glow, px);
                    Canvas.SetTop(glow, py);
                    c.Children.Add(glow);
                }
            c.IsHitTestVisible = false;
            return c;
        }

        private Canvas MakeNoiseCanvas()
        {
            var c = new Canvas();
            var rnd = new Random(7);
            int n = (int)(Data.W * Data.H / 90);
            for (int i = 0; i < n; i++)
            {
                var dot = new Ellipse { Width = 1.4, Height = 1.4, Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 255, 255, 255)) };
                Canvas.SetLeft(dot, rnd.NextDouble() * Data.W);
                Canvas.SetTop(dot, rnd.NextDouble() * Data.H);
                c.Children.Add(dot);
            }
            c.IsHitTestVisible = false;
            return c;
        }

        private Brush MakeMosaicBrush()
        {
            // 棋盘马赛克块
            var g = new DrawingGroup();
            var c1 = System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33);
            var c2 = System.Windows.Media.Color.FromRgb(0x4a, 0x4a, 0x4a);
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(c1), null, new RectangleGeometry(new Rect(0, 0, 14, 14))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(c2), null, new RectangleGeometry(new Rect(0, 0, 7, 7))));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(c2), null, new RectangleGeometry(new Rect(7, 7, 7, 7))));
            var db = new DrawingBrush(g)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 14, 14),
                ViewportUnits = BrushMappingMode.Absolute
            };
            return db;
        }

        public void SetEditMode(bool edit)
        {
            handle.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
            UpdateBadge();
        }

        // 设置鼠标穿透：开启后点击/滚轮会漏到下层（微信），不能拖动；关闭则可编辑
        public void SetClickThrough(bool through)
        {
            pendingClickThrough = through;
            ApplyClickThrough(through);
        }

        private void ApplyClickThrough(bool through)
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return; // 句柄还没创建，OnSourceInitialized 时会补上
                int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                if (through) ex |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
                else ex &= ~WS_EX_TRANSPARENT;
                SetWindowLong(hwnd, GWL_EXSTYLE, ex);
            }
            catch { }
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private void DragStart(object s, MouseButtonEventArgs e)
        {
            if (!MainWindow.EditMode) return;
            // 用 WPF 内置 DragMove 拖动（内部用屏幕坐标，不会抖动）
            try { DragMove(); } catch { }
        }

        private void ResizeStart(object s, MouseButtonEventArgs e)
        {
            if (!MainWindow.EditMode) return;
            e.Handled = true;
            var win = this;
            // 用屏幕坐标计算，避免窗口变大时坐标系变化导致抖动
            var startScreen = PointToScreen(e.GetPosition(win));
            double sw = Width, sh = Height;
            suppressResample = true; // 拖拽过程中不重复取色
            MouseEventHandler mv = null;
            mv = (s2, e2) =>
            {
                var curScreen = PointToScreen(e2.GetPosition(win));
                win.Width = Math.Max(40, sw + (curScreen.X - startScreen.X));
                win.Height = Math.Max(30, sh + (curScreen.Y - startScreen.Y));
            };
            handle.CaptureMouse();
            handle.MouseMove += mv;
            handle.MouseLeftButtonUp += (s2, e2) =>
            {
                handle.MouseMove -= mv;
                handle.ReleaseMouseCapture();
                suppressResample = false;
                ApplyStyle(); // 松手后按最终尺寸重建纹理会重新取色
            };
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            if (!MainWindow.EditMode) { base.OnMouseRightButtonUp(e); return; }
            var m = new ContextMenu();
            var bl = new MenuItem { Header = "毛玻璃（推荐，隐约见影不突兀）", IsChecked = Data.Style == "glass" }; bl.Click += (s, ev) => { Data.Style = "glass"; ApplyStyle(); };
            var b = new MenuItem { Header = "模糊", IsChecked = Data.Style == "blur" }; b.Click += (s, ev) => { Data.Style = "blur"; ApplyStyle(); };
            var mo = new MenuItem { Header = "马赛克", IsChecked = Data.Style == "mosaic" }; mo.Click += (s, ev) => { Data.Style = "mosaic"; ApplyStyle(); };
            var so = new MenuItem { Header = "纯色", IsChecked = Data.Style == "solid" }; so.Click += (s, ev) => { Data.Style = "solid"; ApplyStyle(); };
            var refresh = new MenuItem { Header = "重新取色（毛玻璃用）" };
            refresh.Click += (s, ev) => { if (Data.Style == "glass") { cachedSample = System.Windows.Media.Color.FromRgb(0xF5, 0xF5, 0xF5); suppressResample = false; ApplyStyle(); } };
            var del = new MenuItem { Header = "删除此块" }; del.Click += (s, ev) => MainWindow.Instance.RemoveRegion(this);

            m.Items.Add(bl); m.Items.Add(b); m.Items.Add(mo); m.Items.Add(so); m.Items.Add(new Separator());
            m.Items.Add(refresh);

            // 实时透明度滑块（0.3 ~ 1.0）
            var sliderPanel = new StackPanel { Margin = new Thickness(8, 4, 8, 4), Width = 200 };
            var lbl = new TextBlock { FontSize = 11, Foreground = Brushes.Gray };
            var slider = new Slider
            {
                Minimum = 0.3, Maximum = 1.0,
                Value = Data.Opacity,
                SmallChange = 0.05, LargeChange = 0.1
            };
            Action updateLbl = () => lbl.Text = "不透明度：" + (int)(Data.Opacity * 100) + "%（越往上越实）";
            updateLbl();
            slider.ValueChanged += (s, ev) =>
            {
                Data.Opacity = slider.Value;
                updateLbl();
                ApplyStyle(); // 实时生效
            };
            sliderPanel.Children.Add(lbl);
            sliderPanel.Children.Add(slider);
            var sliderItem = new MenuItem { Header = sliderPanel, StaysOpenOnClick = true };
            m.Items.Add(sliderItem);

            m.Items.Add(new Separator());
            m.Items.Add(del);
            m.IsOpen = true;
        }

        private void SyncBack()
        {
            Data.X = Left; Data.Y = Top; Data.W = Width; Data.H = Height;
        }
    }

    // ===== 主控：托盘 + 热键 + 区域管理 =====
    public class MainWindow : Window
    {
        public static MainWindow Instance;
        public static bool EditMode = true;
        private List<MaskWindow> masks = new List<MaskWindow>();
        private bool visible = true;
        private HwndSource hwndSource;

        // 跟随微信自动遮挡
        private DispatcherTimer wechatWatcher;
        // 跟随开关已迁入 Config.FollowEnabled
        private bool manualOverride = false; // 用户手动按热键后，暂停自动跟随

        // Win32 全局热键
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        private const int HOTKEY_ID = 9001;
        private const int WM_HOTKEY = 0x0312;

        public MainWindow()
        {
            Instance = this;
            Width = 400; Height = 380;
            MinWidth = 360; MinHeight = 300;
            Title = "马赛克工具";
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = false;
            BuildUI();
            Config.Load();
            // 启动后强制跳到最前：先置顶一下再取消，再激活获得焦点
            Loaded += (s, e) =>
            {
                Topmost = true;
                Topmost = false;
                Activate();
                Focus();
            };
        }

        private void BuildUI()
        {
            var sp = new StackPanel { Margin = new Thickness(14) };
            sp.Children.Add(new TextBlock { Text = "马赛克工具", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) });

            var btnAdd = new Button { Content = "＋ 新增遮挡块", Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(0, 6, 0, 6) };
            btnAdd.Click += (s, e) => AddRegion(new Region { X = 300, Y = 200, W = 300, H = 200, Style = "mosaic" });

            var btnMode = new Button { Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(0, 6, 0, 6) };
            btnMode.Click += (s, e) => ToggleMode(btnMode);
            btnModeRef = btnMode;

            var btnHide = new Button { Content = "👁 显示/隐藏全部 (" + Config.HotkeyText() + ")", Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(0, 6, 0, 6) };
            btnHide.Click += (s, e) => ToggleAll();

            var btnHotkey = new Button { Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(0, 6, 0, 6) };
            RefreshHotkeyButton(btnHotkey);
            btnHotkey.Click += (s, e) => BeginCaptureHotkey(btnHotkey);

            var btnClear = new Button { Content = "🗑 清空全部", Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(0, 6, 0, 6) };
            btnClear.Click += (s, e) => { foreach (var m in masks.ToList()) RemoveRegion(m); };

            var btnFollow = new Button { Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(0, 6, 0, 6) };
            RefreshFollowButton(btnFollow);
            btnFollow.Click += (s, e) =>
            {
                Config.FollowEnabled = !Config.FollowEnabled;
                manualOverride = false; // 重新开启自动跟随
                RefreshFollowButton(btnFollow);
                if (Config.FollowEnabled) WeChatTick(); // 立即按当前状态刷一次
            };

            var btnAutoStart = new Button { Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(0, 6, 0, 6) };
            RefreshAutoStartButton(btnAutoStart);
            btnAutoStart.Click += (s, e) =>
            {
                bool target = !IsAutoStartEnabled();
                SetAutoStart(target);
                RefreshAutoStartButton(btnAutoStart);
            };

            sp.Children.Add(btnAdd); sp.Children.Add(btnMode); sp.Children.Add(btnHide); sp.Children.Add(btnHotkey); sp.Children.Add(btnFollow); sp.Children.Add(btnAutoStart); sp.Children.Add(btnClear);

            sp.Children.Add(BuildSettingsPanel());

            sp.Children.Add(new TextBlock
            {
                Text = "\n用法：\n• 新增块后，拖动摆到微信要遮住的位置，拖右下角调大小\n• 块上右键换样式：毛玻璃(推荐)/模糊/马赛克/纯色，可调不透明度、删除\n• 摆好后点“鼠标穿透：开”→ 点击直达微信，遮挡块不再挡操作（要再调就切回关）\n• 跟随微信（默认开）：微信在用时自动遮挡；微信被别的窗口盖住、最小化或退出时自动收起\n• 热键：随时手动遮住/还原（按过一次后转为手动，再点“跟随微信”恢复自动）\n• 区域和热键自动保存，下次打开直接套用",
                FontSize = 11, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap
            });
            // 包一层滚动条，屏幕小或内容多时也能看全
            Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        // ===== 可选参数面板 =====
        private UIElement BuildSettingsPanel()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            panel.Children.Add(new TextBlock
            {
                Text = "高级设置（可选参数）",
                FontSize = 13, FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            // 1. 跟随模式
            panel.Children.Add(new TextBlock { Text = "跟随触发方式", FontSize = 11, Foreground = Brushes.Gray });
            var cbMode = new ComboBox { Margin = new Thickness(0, 2, 0, 6) };
            cbMode.Items.Add(new ComboBoxItem { Content = "智能：微信可见且当前正在用（推荐）", Tag = "smart" });
            cbMode.Items.Add(new ComboBoxItem { Content = "微信窗口可见就遮挡", Tag = "visible" });
            cbMode.Items.Add(new ComboBoxItem { Content = "微信进程运行就遮挡", Tag = "running" });
            cbMode.Items.Add(new ComboBoxItem { Content = "不自动跟随，完全手动", Tag = "manual" });
            cbMode.SelectedIndex = Config.FollowMode == "visible" ? 1 : Config.FollowMode == "running" ? 2 : Config.FollowMode == "manual" ? 3 : 0;
            cbMode.SelectionChanged += (s, e) =>
            {
                var item = cbMode.SelectedItem as ComboBoxItem;
                if (item == null) return;
                Config.FollowMode = item.Tag as string;
                if (Config.FollowMode == "manual") Config.FollowEnabled = false;
                else { Config.FollowEnabled = true; manualOverride = false; }
                Config.Save();
                WeChatTick();
            };
            panel.Children.Add(cbMode);

            // 2. 反向触发
            var chkInvert = new CheckBox
            {
                Content = "反向触发：目标不可见/未运行时才遮挡",
                FontSize = 11, IsChecked = Config.InvertFollow,
                Margin = new Thickness(0, 2, 0, 6)
            };
            chkInvert.Checked += (s, e) => { Config.InvertFollow = true; Config.Save(); manualOverride = false; WeChatTick(); };
            chkInvert.Unchecked += (s, e) => { Config.InvertFollow = false; Config.Save(); manualOverride = false; WeChatTick(); };
            panel.Children.Add(chkInvert);

            // 3. 检测间隔
            panel.Children.Add(new TextBlock { Text = "检测间隔（越小越灵敏，越大越省电）", FontSize = 11, Foreground = Brushes.Gray });
            var cbPoll = new ComboBox { Margin = new Thickness(0, 2, 0, 6) };
            int[] ms = { 100, 300, 500, 1000, 2000 };
            foreach (var v in ms) cbPoll.Items.Add(v + " ms");
            int idx = Array.IndexOf(ms, Config.PollMs);
            cbPoll.SelectedIndex = idx >= 0 ? idx : 1;
            cbPoll.SelectionChanged += (s, e) =>
            {
                Config.PollMs = ms[cbPoll.SelectedIndex];
                Config.Save();
                RestartWatcher();
            };
            panel.Children.Add(cbPoll);

            // 4. 目标进程名
            panel.Children.Add(new TextBlock { Text = "跟随的目标进程（逗号分隔，可改成 QQ、DingTalk 等）", FontSize = 11, Foreground = Brushes.Gray });
            var tbProc = new TextBox { Text = Config.TargetProcesses, Margin = new Thickness(0, 2, 0, 2) };
            panel.Children.Add(tbProc);
            var btnApplyProc = new Button { Content = "应用进程名", Margin = new Thickness(0, 2, 0, 4), Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Left };
            btnApplyProc.Click += (s, e) =>
            {
                Config.TargetProcesses = tbProc.Text.Trim();
                if (Config.TargetProcesses.Length == 0) Config.TargetProcesses = "WeChat,Weixin";
                tbProc.Text = Config.TargetProcesses;
                Config.Save();
                manualOverride = false;
                WeChatTick();
            };
            panel.Children.Add(btnApplyProc);

            return panel;
        }

        private void RestartWatcher()
        {
            if (wechatWatcher == null) return;
            wechatWatcher.Stop();
            wechatWatcher.Interval = TimeSpan.FromMilliseconds(Config.PollMs);
            wechatWatcher.Start();
        }


        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                hwndSource = (HwndSource)PresentationSource.FromVisual(this);
                if (hwndSource != null)
                {
                    hwndSource.AddHook(WndProc);
                    RegisterCurrentHotkey();
                }
                // 首次使用：无保存区域时给两块示例，方便直接拖动上手
                if (Config.Regions.Count == 0)
                {
                    Config.Regions.Add(new Region { X = 220, Y = 160, W = 320, H = 420, Style = "mosaic" });   // 会话列表
                    Config.Regions.Add(new Region { X = 560, Y = 160, W = 460, H = 420, Style = "blur" });      // 聊天内容
                }
                // 载入区域
                foreach (var r in Config.Regions) SpawnMask(r, false);
                SetEditModeAll(!Config.Locked);
                RefreshModeButton();
                UpdateVisibility();
                SetupTray();
                // 启动微信监听：每 300 毫秒检查一次微信窗口是否可见
                wechatWatcher = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Config.PollMs) };
                wechatWatcher.Tick += (s, ev) => WeChatTick();
                wechatWatcher.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show("初始化遮挡块出错：\n" + ex.Message, "WeChatMosaic");
            }
        }

        private void RefreshFollowButton(Button btn)
        {
            if (btn != null) btn.Content = Config.FollowEnabled ? "🟢 跟随微信：开（智能：用微信时遮挡）" : "⚪ 跟随微信：关（手动控制）";
        }

        // 智能跟随：微信可见 + 前台窗口属于"微信家族/本工具" → 遮；
        // 微信被别的程序盖住（前台是别人）或最小化 → 收
        // 根据配置判定"现在该不该遮挡"（支持 smart/visible/running/manual 四种模式 + 反向触发）
        private bool IsWeChatWindowVisible()
        {
            try
            {
                var names = Config.TargetProcesses.Split(',');
                string self = System.Diagnostics.Process.GetCurrentProcess().ProcessName;

                // 1) 目标进程是否在运行
                bool running = false;
                foreach (var n in names)
                {
                    var name = n.Trim();
                    if (name.Length == 0) continue;
                    if (System.Diagnostics.Process.GetProcessesByName(name).Length > 0) { running = true; break; }
                }

                // running 模式：只要进程在就遮（不管窗口状态）
                if (Config.FollowMode == "running")
                    return Config.InvertFollow ? !running : running;

                // 2) 目标主窗口是否可见且未最小化
                bool targetVisible = false;
                if (running)
                {
                    foreach (var n in names)
                    {
                        var name = n.Trim();
                        if (name.Length == 0) continue;
                        foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
                        {
                            IntPtr h = p.MainWindowHandle;
                            if (h != IntPtr.Zero && IsWindowVisible(h) && !IsIconic(h)) { targetVisible = true; break; }
                        }
                        if (targetVisible) break;
                    }
                }

                // visible 模式：可见就遮
                if (Config.FollowMode == "visible")
                    return Config.InvertFollow ? !targetVisible : targetVisible;

                // manual 模式：不走自动跟随，保持手动状态
                if (Config.FollowMode == "manual")
                    return visible;

                if (!targetVisible)
                    return Config.InvertFollow; // 智能模式 + 反向：不可见才遮

                // smart 模式：可见 + 前台窗口属于白名单（目标进程 或 本工具自身）
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return true;
                uint pid;
                GetWindowThreadProcessId(fg, out pid);
                string fgName;
                try { fgName = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; }
                catch { return true; }

                if (fgName.Equals(self, StringComparison.OrdinalIgnoreCase)) return true;
                foreach (var n in names)
                    if (fgName.Equals(n.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            catch { return false; }
        }

        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        // ===== 开机自启动（写当前用户注册表 Run 键，无需管理员）=====
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "WeChatMosaic";

        private static bool IsAutoStartEnabled()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    return key != null && key.GetValue(RunValueName) != null;
                }
            }
            catch { return false; }
        }

        private static void SetAutoStart(bool enable)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) return;
                    if (enable)
                    {
                        // 存 exe 完整路径，加引号防止路径含空格
                        string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                        key.SetValue(RunValueName, "\"" + exe + "\"");
                    }
                    else
                    {
                        key.DeleteValue(RunValueName, false);
                    }
                }
            }
            catch { }
        }

        private void RefreshAutoStartButton(Button btn)
        {
            if (btn != null) btn.Content = IsAutoStartEnabled() ? "🚀 开机自启：开" : "🚀 开机自启：关";
        }

        // 每 300ms 检查：跟随模式下，微信窗口可见→显示，否则→隐藏
        private void WeChatTick()
        {
            if (!Config.FollowEnabled || manualOverride) return;
            bool shouldShow = IsWeChatWindowVisible();
            if (shouldShow != visible)
            {
                visible = shouldShow;
                UpdateVisibility();
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                ToggleAll();
                handled = true;
            }
            return IntPtr.Zero;
        }

        // 注册当前配置的热键（先注销旧的）
        private void RegisterCurrentHotkey()
        {
            if (hwndSource == null) return;
            UnregisterHotKey(hwndSource.Handle, HOTKEY_ID);
            RegisterHotKey(hwndSource.Handle, HOTKEY_ID, Config.HotkeyMods, (uint)Config.HotkeyVk);
        }

        // ===== 设置热键：点按钮后捕获下一次按键组合 =====
        private Button btnHotkeyRef;

        private void BeginCaptureHotkey(Button btn)
        {
            btnHotkeyRef = btn;
            btn.Content = "请按下新的组合键… (Esc 取消)";
            PreviewKeyDown += CaptureKey;
            Focus();
        }

        private void CaptureKey(object s, KeyEventArgs e)
        {
            var btn = btnHotkeyRef;

            // 真实主键：Alt 组合时 e.Key 是 System，要取 e.SystemKey
            Key key = (e.Key == Key.System) ? e.SystemKey : e.Key;

            // 只按下/松开修饰键本身：先忽略，继续等主键（不退出捕获）
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LWin || key == Key.RWin)
            { e.Handled = true; return; }

            // 读事件瞬间的修饰键状态（比 Keyboard.IsKeyDown 可靠）
            var m = e.KeyboardDevice.Modifiers;
            uint mods = 0;
            if ((m & ModifierKeys.Control) != 0) mods |= 0x2;
            if ((m & ModifierKeys.Alt) != 0) mods |= 0x1;
            if ((m & ModifierKeys.Shift) != 0) mods |= 0x4;
            if ((m & ModifierKeys.Windows) != 0) mods |= 0x8;

            // Esc 取消（无修饰键时）
            if (key == Key.Escape && mods == 0)
            {
                PreviewKeyDown -= CaptureKey;
                RefreshHotkeyButton(btn);
                e.Handled = true;
                return;
            }

            int vk = KeyInterop.VirtualKeyFromKey(key);
            if (vk <= 0) { e.Handled = true; return; }

            if (mods == 0)
            {
                MessageBox.Show("请带上修饰键（Ctrl / Alt / Shift / Win）再加一个键，\n单独一个普通键容易误触。", "马赛克工具");
                e.Handled = true;
                return; // 继续等待下一次按键
            }

            PreviewKeyDown -= CaptureKey;
            Config.HotkeyVk = vk;
            Config.HotkeyMods = mods;
            Config.Save();
            RegisterCurrentHotkey();
            RefreshHotkeyButton(btn);
            MessageBox.Show("热键已设置为：" + Config.HotkeyText(), "马赛克工具");
            e.Handled = true;
        }

        private void RefreshHotkeyButton(Button btn)
        {
            if (btn != null) btn.Content = "⌨ 设置热键（当前：" + Config.HotkeyText() + "）";
        }

        public void AddRegion(Region r)
        {
            Config.Regions.Add(r);
            SpawnMask(r, true);
            Config.Save();
        }

        private void SpawnMask(Region r, bool save)
        {
            var m = new MaskWindow(r);
            masks.Add(m);
            m.SetEditMode(EditMode);
            m.SetClickThrough(!EditMode); // 与当前模式保持一致
            if (visible) m.Show(); else m.Hide();
        }

        public void RemoveRegion(MaskWindow m)
        {
            Config.Regions.Remove(m.Data);
            masks.Remove(m);
            m.Close();
            Config.Save();
        }

        // 防抖保存：拖尺寸/调透明度时不会每帧写盘
        private DispatcherTimer saveTimer;

        public static void RequestSave()
        {
            var inst = Instance;
            if (inst == null) return;
            if (inst.saveTimer == null)
            {
                inst.saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                inst.saveTimer.Tick += (s, e) => { inst.saveTimer.Stop(); Config.Save(); };
            }
            inst.saveTimer.Stop();
            inst.saveTimer.Start();
        }

        private void ToggleMode(Button btn)
        {
            SetEditModeAll(!EditMode);
            RefreshModeButton();
            Config.Locked = !EditMode;
            Config.Save();
        }

        private Button btnModeRef;

        private void RefreshModeButton()
        {
            if (btnModeRef == null) return;
            btnModeRef.Content = EditMode
                ? "🖱 鼠标穿透：关（编辑中，可拖动）"
                : "🖱 鼠标穿透：开（点击直达微信）";
        }

        private void SetEditModeAll(bool edit)
        {
            EditMode = edit;
            foreach (var m in masks)
            {
                m.SetEditMode(edit);
                m.SetClickThrough(!edit);   // 编辑模式关穿透；锁定模式开穿透
            }
        }

        private void ToggleAll()
        {
            visible = !visible;
            manualOverride = true; // 手动按热键后，暂停自动跟随，直到重新点“跟随微信”
            UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            foreach (var m in masks) { if (visible) m.Show(); else m.Hide(); }
        }

        private System.Windows.Forms.NotifyIcon trayIcon;
        private bool reallyQuit = false;

        private void SetupTray()
        {
            trayIcon = new System.Windows.Forms.NotifyIcon();
            trayIcon.Text = "马赛克工具";
            // 用系统自带图标，避免额外的 ico 文件
            trayIcon.Icon = System.Drawing.SystemIcons.Application;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += (s, e) => ShowMain();

            var menu = new System.Windows.Forms.ContextMenuStrip();
            var miShow = new System.Windows.Forms.ToolStripMenuItem("显示窗口");
            miShow.Click += (s, e) => ShowMain();
            var miToggle = new System.Windows.Forms.ToolStripMenuItem("显示/隐藏遮挡 (Shift+|)");
            miToggle.Click += (s, e) => ToggleAll();
            var miExit = new System.Windows.Forms.ToolStripMenuItem("退出");
            miExit.Click += (s, e) => { reallyQuit = true; Close(); };
            menu.Items.Add(miShow);
            menu.Items.Add(miToggle);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(miExit);
            trayIcon.ContextMenuStrip = menu;
        }

        private void ShowMain()
        {
            Show();
            WindowState = WindowState.Normal;
            Topmost = true; Topmost = false;
            Activate();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!reallyQuit)
            {
                // 点 X 不退出，只隐藏窗口到托盘，遮挡与热键继续在后台生效
                e.Cancel = true;
                Hide();
                return;
            }
            Config.Save();
            if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); }
            UnregisterHotKey(hwndSource != null ? hwndSource.Handle : IntPtr.Zero, HOTKEY_ID);
            foreach (var m in masks.ToList()) m.Close();
            base.OnClosing(e);
        }
    }

    public static class App
    {
        [STAThread]
        public static void Main()
        {
            // 单实例：已在运行则把已有窗口唤到最前，不再开新实例
            var cur = System.Diagnostics.Process.GetCurrentProcess();
            var others = System.Diagnostics.Process.GetProcessesByName(cur.ProcessName)
                .Where(p => p.Id != cur.Id).ToArray();
            if (others.Length > 0)
            {
                foreach (var p in others)
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(p.MainWindowHandle, 9); // SW_RESTORE
                        SetForegroundWindow(p.MainWindowHandle);
                    }
                }
                return;
            }

            try
            {
                var app = new Application();
                app.DispatcherUnhandledException += (s, e) =>
                {
                    MessageBox.Show("运行出错：\n" + e.Exception.ToString(), "WeChatMosaic 错误");
                    e.Handled = true;
                };
                app.Run(new MainWindow());
            }
            catch (Exception ex)
            {
                MessageBox.Show("启动失败：\n" + ex.ToString(), "WeChatMosaic 错误");
            }
        }

        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
