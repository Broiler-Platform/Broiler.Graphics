using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Broiler.Graphics.Windows;

/// <summary>
/// Win32 window implementation that presents a <see cref="BRenderList"/> directly through
/// <see cref="Direct2DRenderer"/>.
/// </summary>
[SupportedOSPlatform("windows7.0")]
public abstract class Direct2DWindow : BWindow
{
    private const string WindowClassName = "BroilerGraphicsDirect2DWindow";

    private const int ErrorClassAlreadyExists = 1410;
    private const int CwUseDefault = unchecked((int)0x80000000);

    private const uint CsHRedraw = 0x0002;
    private const uint CsVRedraw = 0x0001;
    private const uint WsOverlappedWindow = 0x00CF0000;

    private const int SwShow = 5;
    private const int SizeMinimized = 1;
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;
    private const int GwlUserData = -21;
    private const int LogPixelsX = 88;

    private const uint WmNccreate = 0x0081;
    private const uint WmNcdestroy = 0x0082;
    private const uint WmCreate = 0x0001;
    private const uint WmDestroy = 0x0002;
    private const uint WmSize = 0x0005;
    private const uint WmPaint = 0x000F;
    private const uint WmEraseBkgnd = 0x0014;
    private const uint WmDpiChanged = 0x02E0;

    private static readonly WndProc s_wndProc = WindowProc;
    private static bool s_classRegistered;

    private IntPtr _hwnd;
    private GCHandle _selfHandle;
    private Direct2DRenderer? _renderer;
    private IBroilerSurface? _surface;
    private long _frameIndex;
    private bool _runStarted;

    protected Direct2DWindow(BWindowOptions options)
        : base(options)
    {
    }

    public override IntPtr NativeHandle => _hwnd;

    public override BSize ClientSize => ClientDipSize;

    public override double DpiScale
    {
        get
        {
            if (_hwnd == IntPtr.Zero)
                return 1.0;

            uint dpi = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393)
                ? GetDpiForWindow(_hwnd)
                : GetDeviceDpi(_hwnd);
            return dpi == 0 ? 1.0 : dpi / 96.0;
        }
    }

    public override IBroilerRenderer? Renderer => _renderer;

    public Direct2DRenderer? Direct2DRenderer => _renderer;

    public override IBroilerSurface? Surface => _surface;

    protected override int RunCore()
    {
        if (_runStarted)
            throw new InvalidOperationException("A Direct2DWindow instance can only be run once.");

        _runStarted = true;
        EnsureWindowClassRegistered();
        CreateWindow();

        ShowWindow(_hwnd, SwShow);
        UpdateWindow(_hwnd);

        while (true)
        {
            int result = GetMessage(out MSG msg, IntPtr.Zero, 0, 0);
            if (result == -1)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetMessage failed.");
            if (result == 0)
                return unchecked((int)msg.WParam);

            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    protected override void InvalidateCore()
    {
        if (_hwnd != IntPtr.Zero)
            InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ReleaseGraphicsResources();

        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
    }

    private void CreateWindow()
    {
        RECT rect = new(0, 0, EffectiveClientWidth, EffectiveClientHeight);
        if (!AdjustWindowRectEx(ref rect, WsOverlappedWindow, false, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "AdjustWindowRectEx failed.");

        int width = rect.Width;
        int height = rect.Height;
        int x = CenteredCoordinate(GetSystemMetrics(SmCxScreen), width);
        int y = CenteredCoordinate(GetSystemMetrics(SmCyScreen), height);

        _selfHandle = GCHandle.Alloc(this);
        IntPtr lpParam = GCHandle.ToIntPtr(_selfHandle);

        _hwnd = CreateWindowEx(
            0,
            WindowClassName,
            string.IsNullOrWhiteSpace(Options.Title) ? "Broiler.Graphics" : Options.Title,
            WsOverlappedWindow,
            x,
            y,
            width,
            height,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandle(null),
            lpParam);

        if (_hwnd == IntPtr.Zero)
        {
            if (_selfHandle.IsAllocated)
                _selfHandle.Free();

            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx failed.");
        }
    }

    private IntPtr HandleMessage(uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WmCreate:
                CreateGraphicsResources();
                OnCreated();
                return IntPtr.Zero;

            case WmSize:
                if (wParam.ToInt32() != SizeMinimized)
                    ResizeSurfaceAndNotify();
                return IntPtr.Zero;

            case WmDpiChanged:
                ResizeSurfaceAndNotify();
                return IntPtr.Zero;

            case WmEraseBkgnd:
                return new IntPtr(1);

            case WmPaint:
                RenderFrame();
                ValidateRect(_hwnd, IntPtr.Zero);
                return IntPtr.Zero;

            case WmDestroy:
                ReleaseGraphicsResources();
                PostQuitMessage(0);
                return IntPtr.Zero;

            case WmNcdestroy:
                IntPtr destroyedHwnd = _hwnd;
                SetWindowLongPtr(_hwnd, GwlUserData, IntPtr.Zero);
                if (_selfHandle.IsAllocated)
                    _selfHandle.Free();
                _hwnd = IntPtr.Zero;
                return DefWindowProc(destroyedHwnd, message, wParam, lParam);

            default:
                return DefWindowProc(_hwnd, message, wParam, lParam);
        }
    }

    private void CreateGraphicsResources()
    {
        ReleaseGraphicsResources();

        if (_hwnd == IntPtr.Zero || !HasDrawableClientArea)
            return;

        _renderer = new Direct2DRenderer();
        _surface = _renderer.CreateHwndSurface(
            _hwnd,
            new BSurfaceDescriptor(ClientDipSize, DpiScale, BPixelFormat.Bgra8, Options.EnableTransparency));
    }

    private void ReleaseGraphicsResources()
    {
        if (_renderer is not null || _surface is not null)
            OnGraphicsResourcesReleasing();

        _surface?.Dispose();
        _surface = null;
        _renderer?.Dispose();
        _renderer = null;
    }

    private void ResizeSurfaceAndNotify()
    {
        if (!HasDrawableClientArea)
            return;

        if (_renderer is null || _surface is null)
        {
            CreateGraphicsResources();
            OnResized(ClientSize, DpiScale);
            InvalidateCore();
            return;
        }

        try
        {
            _surface.Resize(ClientDipSize, DpiScale);
        }
        catch (BDeviceLostException)
        {
            CreateGraphicsResources();
        }

        OnResized(ClientSize, DpiScale);
        InvalidateCore();
    }

    private void RenderFrame()
    {
        if (!HasDrawableClientArea)
            return;

        if (_renderer is null || _surface is null)
            CreateGraphicsResources();

        if (_renderer is null || _surface is null)
            return;

        try
        {
            BRenderList? renderList = BuildRenderList(ClientSize);
            if (renderList is null)
                return;

            _renderer.Render(_surface, renderList, CreateFrameContext(_frameIndex++));
        }
        catch (BDeviceLostException)
        {
            CreateGraphicsResources();
            InvalidateCore();
        }
    }

    private bool HasDrawableClientArea
    {
        get
        {
            if (_hwnd == IntPtr.Zero || !GetClientRect(_hwnd, out RECT client))
                return false;

            return client.Width > 0 && client.Height > 0;
        }
    }

    private BSize ClientDipSize
    {
        get
        {
            if (_hwnd == IntPtr.Zero || !GetClientRect(_hwnd, out RECT client))
                return BSize.Empty;

            double dpiScale = DpiScale;
            return new BSize(client.Width / dpiScale, client.Height / dpiScale);
        }
    }

    private int EffectiveClientWidth => Math.Max(1, Options.ClientWidth);

    private int EffectiveClientHeight => Math.Max(1, Options.ClientHeight);

    private static int CenteredCoordinate(int screenExtent, int windowExtent)
    {
        if (screenExtent <= 0 || windowExtent <= 0)
            return CwUseDefault;

        return Math.Max(0, (screenExtent - windowExtent) / 2);
    }

    private static uint GetDeviceDpi(IntPtr hwnd)
    {
        IntPtr hdc = GetDC(hwnd);
        if (hdc == IntPtr.Zero)
            return 96;

        try
        {
            int dpi = GetDeviceCaps(hdc, LogPixelsX);
            return dpi <= 0 ? 96u : (uint)dpi;
        }
        finally
        {
            _ = ReleaseDC(hwnd, hdc);
        }
    }

    private static void EnsureWindowClassRegistered()
    {
        if (s_classRegistered)
            return;

        var windowClass = new WNDCLASSEX
        {
            CbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            Style = CsHRedraw | CsVRedraw,
            LpfnWndProc = s_wndProc,
            HInstance = GetModuleHandle(null),
            HCursor = LoadCursor(IntPtr.Zero, new IntPtr(32512)),
            LpszClassName = WindowClassName,
        };

        ushort atom = RegisterClassEx(ref windowClass);
        if (atom == 0)
        {
            int error = Marshal.GetLastWin32Error();
            if (error != ErrorClassAlreadyExists)
                throw new Win32Exception(error, "RegisterClassEx failed.");
        }

        s_classRegistered = true;
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmNccreate)
        {
            CREATESTRUCT createStruct = Marshal.PtrToStructure<CREATESTRUCT>(lParam);
            GCHandle handle = GCHandle.FromIntPtr(createStruct.LpCreateParams);
            if (handle.Target is Direct2DWindow window)
            {
                window._hwnd = hwnd;
                SetWindowLongPtr(hwnd, GwlUserData, createStruct.LpCreateParams);
                return new IntPtr(1);
            }

            return IntPtr.Zero;
        }

        Direct2DWindow? instance = FromHwnd(hwnd);
        return instance is null
            ? DefWindowProc(hwnd, message, wParam, lParam)
            : instance.HandleMessage(message, wParam, lParam);
    }

    private static Direct2DWindow? FromHwnd(IntPtr hwnd)
    {
        IntPtr userData = GetWindowLongPtr(hwnd, GwlUserData);
        if (userData == IntPtr.Zero)
            return null;

        GCHandle handle = GCHandle.FromIntPtr(userData);
        return handle.Target as Direct2DWindow;
    }

    private static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hwnd, index, value)
            : new IntPtr(SetWindowLong32(hwnd, index, value.ToInt32()));
    }

    private static IntPtr GetWindowLongPtr(IntPtr hwnd, int index)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hwnd, index)
            : new IntPtr(GetWindowLong32(hwnd, index));
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint CbSize;
        public uint Style;
        public WndProc LpfnWndProc;
        public int CbClsExtra;
        public int CbWndExtra;
        public IntPtr HInstance;
        public IntPtr HIcon;
        public IntPtr HCursor;
        public IntPtr HbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? LpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string LpszClassName;
        public IntPtr HIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RECT
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;

        public RECT(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public int Width => Right - Left;

        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public POINT Pt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREATESTRUCT
    {
        public IntPtr LpCreateParams;
        public IntPtr HInstance;
        public IntPtr HMenu;
        public IntPtr HwndParent;
        public int Cy;
        public int Cx;
        public int Y;
        public int X;
        public int Style;
        public IntPtr LpszName;
        public IntPtr LpszClass;
        public uint DwExStyle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateWindowExW")]
    private static extern IntPtr CreateWindowEx(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr hwndParent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustWindowRectEx(ref RECT rect, uint style, [MarshalAs(UnmanagedType.Bool)] bool menu, uint exStyle);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int commandShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetMessage(out MSG message, IntPtr hwnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG message);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ValidateRect(IntPtr hwnd, IntPtr rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hwnd, int index);
}
