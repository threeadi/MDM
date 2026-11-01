using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace Mdm.Services;

public class TrayIconService : IDisposable
{
    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 101;

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int NIF_INFO = 0x00000010;

    private const int NIIF_INFO = 0x00000001;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly Window _window;
    private HwndSource? _hwndSource;
    private NOTIFYICONDATA _nid;
    private bool _isCreated;
    private ContextMenu? _contextMenu;

    public event Action? OnOpenRequested;
    public event Action? OnAddDownloadRequested;
    public event Action? OnPauseAllRequested;
    public event Action? OnResumeAllRequested;
    public event Action? OnExitRequested;

    public TrayIconService(Window window)
    {
        _window = window;
    }

    public void Initialize()
    {
        var helper = new WindowInteropHelper(_window);
        IntPtr hwnd = helper.EnsureHandle();

        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(HwndHook);

        IntPtr iconHandle = IntPtr.Zero;
        try
        {
            string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(icoPath))
            {
                iconHandle = LoadImage(IntPtr.Zero, icoPath, 1, 16, 16, 0x00000010);
            }

            if (iconHandle == IntPtr.Zero)
            {
                IntPtr hInst = GetModuleHandle(null);
                iconHandle = LoadIcon(hInst, (IntPtr)1);
            }
        }
        catch { }

        if (iconHandle == IntPtr.Zero)
        {
            iconHandle = LoadIcon(IntPtr.Zero, (IntPtr)32512); // IDI_APPLICATION default icon
        }

        _nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = 1001,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = iconHandle,
            szTip = "MDM - Modern Download Manager"
        };

        _isCreated = Shell_NotifyIcon(NIM_ADD, ref _nid);
        BuildContextMenu();
    }

    private void BuildContextMenu()
    {
        _contextMenu = new ContextMenu
        {
            Background = (System.Windows.Media.Brush)_window.FindResource("SurfaceLowBrush"),
            BorderBrush = (System.Windows.Media.Brush)_window.FindResource("BorderBrush")
        };

        var openItem = new MenuItem { Header = "Buka MDM", FontWeight = FontWeights.Bold, Foreground = (System.Windows.Media.Brush)_window.FindResource("TextPrimaryBrush") };
        openItem.Click += (_, _) => OnOpenRequested?.Invoke();

        var addItem = new MenuItem { Header = "Tambah Unduhan Baru...", Foreground = (System.Windows.Media.Brush)_window.FindResource("AccentCyanBrush") };
        addItem.Click += (_, _) => OnAddDownloadRequested?.Invoke();

        var resumeAllItem = new MenuItem { Header = "Lanjutkan Semua", Foreground = (System.Windows.Media.Brush)_window.FindResource("AccentEmeraldBrush") };
        resumeAllItem.Click += (_, _) => OnResumeAllRequested?.Invoke();

        var pauseAllItem = new MenuItem { Header = "Jeda Semua", Foreground = (System.Windows.Media.Brush)_window.FindResource("AccentAmberBrush") };
        pauseAllItem.Click += (_, _) => OnPauseAllRequested?.Invoke();

        var exitItem = new MenuItem { Header = "Keluar", Foreground = (System.Windows.Media.Brush)_window.FindResource("AccentRoseBrush") };
        exitItem.Click += (_, _) => OnExitRequested?.Invoke();

        _contextMenu.Items.Add(openItem);
        _contextMenu.Items.Add(addItem);
        _contextMenu.Items.Add(new Separator { Background = (System.Windows.Media.Brush)_window.FindResource("BorderBrush") });
        _contextMenu.Items.Add(resumeAllItem);
        _contextMenu.Items.Add(pauseAllItem);
        _contextMenu.Items.Add(new Separator { Background = (System.Windows.Media.Brush)_window.FindResource("BorderBrush") });
        _contextMenu.Items.Add(exitItem);
    }

    public void ShowNotification(string title, string message)
    {
        if (!_isCreated) return;

        _nid.uFlags = NIF_INFO;
        _nid.szInfoTitle = title;
        _nid.szInfo = message;
        _nid.dwInfoFlags = NIIF_INFO;

        Shell_NotifyIcon(NIM_MODIFY, ref _nid);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYICON)
        {
            int mouseMsg = (int)((long)lParam & 0xFFFF);
            if (mouseMsg == WM_LBUTTONUP)
            {
                OnOpenRequested?.Invoke();
                handled = true;
            }
            else if (mouseMsg == WM_RBUTTONUP)
            {
                if (_contextMenu != null)
                {
                    SetForegroundWindow(hwnd);
                    _contextMenu.IsOpen = true;
                }
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_isCreated)
        {
            Shell_NotifyIcon(NIM_DELETE, ref _nid);
            _isCreated = false;
        }

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(HwndHook);
            _hwndSource = null;
        }
    }
}
