// Native Win32 tray icon for Windows.
// Replaces Avalonia's broken TrayIcon right-click context menu on Windows.
// Uses CreatePopupMenu + TrackPopupMenu for proper native context menus.
#if WINDOWS
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace NudgeTray;

static class NativeTray
{
    static IntPtr _trayWnd;
    static IntPtr _hInst;
    static bool _iconAdded;
    const int NOTIFY_ID = 42;
    const int WM_TRAY_CALLBACK = 0x8001;
    const int WM_LBUTTONUP = 0x0202;
    const int WM_RBUTTONUP = 0x0205;
    const int WM_COMMAND = 0x0111;
    const int WM_DESTROY = 0x0002;

    // Menu item IDs
    const int IDM_STATUS = 0;
    const int IDM_ANALYTICS = 1;
    const int IDM_SETTINGS = 2;
    const int IDM_FEEDBACK = 3;
    const int IDM_UPDATES = 4;
    const int IDM_QUIT = 5;

    static string _statusText = "Nudge Tracker";
    static string _updateText = "Check for Updates";
    static bool _initialized;

    public static bool IsInitialized => _initialized;

    public static event Action? LeftClicked;
    public static event Action? AnalyticsClicked;
    public static event Action? SettingsClicked;
    public static event Action? FeedbackClicked;
    public static event Action? UpdatesClicked;
    public static event Action? QuitClicked;

    public static bool Initialize()
    {
        if (_initialized) return true;

        _hInst = GetModuleHandleW(null);
        if (_hInst == IntPtr.Zero) return false;

        var wc = new WNDCLASSW
        {
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate<WndProcDelegate>(TrayWndProc),
            hInstance = _hInst,
            lpszClassName = "NudgeTrayMsgWindow"
        };

        ushort atom = RegisterClassW(ref wc);
        if (atom == 0) return false;

        _trayWnd = CreateWindowExW(0, "NudgeTrayMsgWindow", "",
            0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, _hInst, IntPtr.Zero);
        if (_trayWnd == IntPtr.Zero) return false;

        AddIcon();
        _initialized = true;
        return true;
    }

    public static void RemoveIcon()
    {
        if (_iconAdded)
        {
            var nid = new NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
                hWnd = _trayWnd,
                uID = NOTIFY_ID
            };
            Shell_NotifyIconW(NIM_DELETE, ref nid);
            _iconAdded = false;
        }
    }

    public static void SetStatusText(string text) => _statusText = text;
    public static void SetUpdateText(string text) => _updateText = text;

    static void AddIcon()
    {
        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _trayWnd,
            uID = NOTIFY_ID,
            uFlags = NIF_ICON | NIF_TIP | NIF_MESSAGE,
            uCallbackMessage = WM_TRAY_CALLBACK,
            hIcon = CreateNativeIcon(),
            szTip = "Nudge Productivity Tracker"
        };

        if (!Shell_NotifyIconW(NIM_ADD, ref nid))
        {
            int err = Marshal.GetLastWin32Error();
            Debug.WriteLine($"[NativeTray] NIM_ADD failed: {err}");
        }
        else
        {
            _iconAdded = true;
            Debug.WriteLine("[NativeTray] Icon registered");
        }
    }

    static IntPtr TrayWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_TRAY_CALLBACK:
                switch (lParam.ToInt32())
                {
                    case WM_LBUTTONUP:
                        LeftClicked?.Invoke();
                        return IntPtr.Zero;
                    case WM_RBUTTONUP:
                        ShowMenu();
                        return IntPtr.Zero;
                }
                return IntPtr.Zero;

            case WM_COMMAND:
                int cmd = wParam.ToInt32() & 0xFFFF;
                switch (cmd)
                {
                    case IDM_ANALYTICS: AnalyticsClicked?.Invoke(); break;
                    case IDM_SETTINGS: SettingsClicked?.Invoke(); break;
                    case IDM_FEEDBACK: FeedbackClicked?.Invoke(); break;
                    case IDM_UPDATES: UpdatesClicked?.Invoke(); break;
                    case IDM_QUIT: QuitClicked?.Invoke(); break;
                }
                return IntPtr.Zero;

            case WM_DESTROY:
                RemoveIcon();
                return IntPtr.Zero;
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    static void ShowMenu()
    {
        IntPtr hMenu = CreatePopupMenu();

        // Status (disabled)
        AppendMenuW(hMenu, MF_STRING | MF_DISABLED | MF_GRAYED, IDM_STATUS, _statusText);
        AppendMenuW(hMenu, MF_SEPARATOR, 0, null);
        AppendMenuW(hMenu, MF_STRING, IDM_ANALYTICS, "📊 Analytics");
        AppendMenuW(hMenu, MF_SEPARATOR, 0, null);
        AppendMenuW(hMenu, MF_STRING, IDM_SETTINGS, "Settings");
        AppendMenuW(hMenu, MF_SEPARATOR, 0, null);
        AppendMenuW(hMenu, MF_STRING, IDM_FEEDBACK, "Send Feedback");
        AppendMenuW(hMenu, MF_STRING, IDM_UPDATES, _updateText);
        AppendMenuW(hMenu, MF_SEPARATOR, 0, null);
        AppendMenuW(hMenu, MF_STRING, IDM_QUIT, "Quit");

        GetCursorPos(out POINT pt);

        // Ensure the menu window has focus so it dismisses properly
        SetForegroundWindow(_trayWnd);

        TrackPopupMenu(hMenu, TPM_RIGHTBUTTON, pt.X, pt.Y, 0, _trayWnd, IntPtr.Zero);

        DestroyMenu(hMenu);
    }

    static IntPtr CreateNativeIcon()
    {
        var renderBitmap = new RenderTargetBitmap(new PixelSize(32, 32), new Vector(96, 96));
        using (var ctx = renderBitmap.CreateDrawingContext())
        {
            ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, 32, 32));
            var brush = new SolidColorBrush(Color.FromRgb(85, 136, 255));
            ctx.DrawRectangle(brush, null, new RoundedRect(new Rect(2, 2, 28, 28), 8));
            var pen = new Pen(Brushes.White, 3);
            ctx.DrawLine(pen, new Point(10, 22), new Point(10, 10));
            ctx.DrawLine(pen, new Point(10, 10), new Point(22, 22));
            ctx.DrawLine(pen, new Point(22, 22), new Point(22, 10));
        }
        var stream = new MemoryStream();
        renderBitmap.Save(stream);
        stream.Position = 0;

        // Convert PNG to HICON via System.Drawing
        using var bmp = new System.Drawing.Bitmap(stream);
        return bmp.GetHicon();
    }

    #region P/Invoke

    const uint NIF_ICON = 0x0002;
    const uint NIF_TIP = 0x0004;
    const uint NIF_MESSAGE = 0x0001;

    const uint NIM_ADD = 0x00000000;
    const uint NIM_DELETE = 0x00000002;

    const uint MF_STRING = 0x00000000;
    const uint MF_SEPARATOR = 0x00000800;
    const uint MF_DISABLED = 0x00000002;
    const uint MF_GRAYED = 0x00000001;
    const uint TPM_RIGHTBUTTON = 0x0002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSW
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern ushort RegisterClassW(ref WNDCLASSW lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent,
        IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, int uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y,
        int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    #endregion
}

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
#endif
