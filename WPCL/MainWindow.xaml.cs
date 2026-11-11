using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WPCL.Bridge;
using WPCL.Interop;
using WPCL.Services;

namespace WPCL;

public partial class MainWindow : Window
{
    // ---------------- Win32 互操作 ----------------
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int DwmWindowCornerPreference = 33;
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MAXIMIZE = 0xF030;
    private const int HTCAPTION = 0x2;
    private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12,
                      HTTOPLEFT = 13, HTTOPRIGHT = 14,
                      HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    private const int ResizeBorder = 6;      // 边缘缩放热区像素
    private const int CaptionHeight = 30;    // 网页标题栏高度（需与 CSS 一致）

    private readonly LauncherCoordinator _coordinator;

    public MainWindow()
    {
        InitializeComponent();
        _coordinator = new LauncherCoordinator();
        _coordinator.EventRaised += SendEvent;
        Loaded += MainWindowLoaded;
        Closed += (_, _) => _coordinator.Dispose();
    }

    // ---------------- 窗口初始化 ----------------
    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        var rounded = 2;
        DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref rounded, sizeof(int));

        if (!DesktopAcrylic.TryApply(hwnd))
        {
            var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
            if (chrome is not null)
            {
                chrome.GlassFrameThickness = new Thickness(0);
            }
            Background = new SolidColorBrush(Color.FromRgb(248, 248, 248));
        }

        // 深色标题栏（对无边框影响不大，但保留兼容）
        //var dark = 1;
        //DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref dark, sizeof(int));

        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
                return HandleHitTest(lParam, ref handled);

            case WM_SYSCOMMAND when ((long)wParam & 0xFFF0) == SC_MAXIMIZE:
                handled = true;
                return IntPtr.Zero;
        }
        return IntPtr.Zero;
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }
    }

    // HandleHitTest
    private IntPtr HandleHitTest(IntPtr lParam, ref bool handled)
    {
        int sx = unchecked((short)(long)lParam);
        int sy = unchecked((short)((long)lParam >> 16));

        // 获取当前 DPI 缩放比例（应对 125% / 150% 缩放）
        var dpi = VisualTreeHelper.GetDpi(this);
        double scale = dpi.DpiScaleX;

        var screenPoint = new System.Windows.Point(sx, sy);
        var clientPoint = PointFromScreen(screenPoint);

        double w = ActualWidth;
        double h = ActualHeight;
        double x = clientPoint.X;
        double y = clientPoint.Y;

        // 根据 DPI 调整边缘判定宽度
        double border = ResizeBorder * scale;

        bool left = x <= border;
        bool right = x >= w - border;
        bool top = y <= border;
        bool bottom = y >= h - border;

        if (top && left) { handled = true; return (IntPtr)HTTOPLEFT; }
        if (top && right) { handled = true; return (IntPtr)HTTOPRIGHT; }
        if (bottom && left) { handled = true; return (IntPtr)HTBOTTOMLEFT; }
        if (bottom && right) { handled = true; return (IntPtr)HTBOTTOMRIGHT; }
        if (left) { handled = true; return (IntPtr)HTLEFT; }
        if (right) { handled = true; return (IntPtr)HTRIGHT; }
        if (top) { handled = true; return (IntPtr)HTTOP; }
        if (bottom) { handled = true; return (IntPtr)HTBOTTOM; }

        return IntPtr.Zero;
    }

    // ---------------- WebView2 初始化 ----------------
    private async void MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await webView.EnsureCoreWebView2Async();
            webView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0, 0, 0, 0);
            webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            webView.CoreWebView2.WebMessageReceived += WebMessageReceived;

            var assetsPath = Path.Combine(AppContext.BaseDirectory, "Assets");
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "wpcl.local",
                assetsPath,
                CoreWebView2HostResourceAccessKind.DenyCors);
            webView.Source = new Uri("https://wpcl.local/index.html");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"WebView2 初始化失败：{ex.Message}", "WPCL", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------------- 消息桥 ----------------
    private async void WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var request = JsonSerializer.Deserialize<BridgeRequest>(e.WebMessageAsJson, JsonDefaults.Options)
                ?? throw new InvalidOperationException("消息内容为空。");

            // 窗口控制类消息：不走 LauncherCoordinator
            switch (request.Action)
            {
                case "chooseDirectory":
                    {
                        var dialog = new OpenFolderDialog { Title = "选择 Minecraft 游戏目录" };
                        var selected = dialog.ShowDialog(this) == true ? dialog.FolderName : null;
                        SendResponse(request.Id, true, selected, null);
                        return;
                    }

                case "windowDrag":
                    {
                        StartWindowDrag();
                        SendResponse(request.Id, true, null, null);
                        return;
                    }

                case "windowAction":
                    {
                        var op = TryReadPayloadString(e.WebMessageAsJson);
                        HandleWindowAction(op);
                        SendResponse(request.Id, true, null, null);
                        return;
                    }

            }

            var result = await _coordinator.HandleAsync(request);
            SendResponse(request.Id, true, result, null);
        }
        catch (Exception ex)
        {
            var requestId = TryReadRequestId(e.WebMessageAsJson);
            SendResponse(requestId, false, null, ex.Message);
        }
    }

    private void StartWindowDrag()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        ReleaseCapture();
        SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    private void HandleWindowAction(string? op)
    {
        switch (op)
        {
            case "minimize":
                WindowState = WindowState.Minimized;
                break;
            case "close":
                Close();
                break;
        }
    }

    private void SendEvent(BridgeEvent message)
    {
        Dispatcher.Invoke(() => webView.CoreWebView2?.PostWebMessageAsJson(
            JsonSerializer.Serialize(message, JsonDefaults.Options)));
    }

    private void SendResponse(string id, bool ok, object? data, string? error)
    {
        var response = new BridgeResponse("response", id, ok, data, error);
        webView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(response, JsonDefaults.Options));
    }

    private static string TryReadRequestId(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
        }
        catch
        {
            return "";
        }
    }

    private static string? TryReadPayloadString(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("payload", out var p) ? p.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}
