using static Broiler.Native.Windows.WindowNative;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;
using Broiler.Graphics.Text;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Broiler.Graphics.Windows.Demo;

internal sealed class Direct2DDemoWindow : IDisposable
{
    private const string WindowClassName = "BroilerGraphicsDirect2DDemoWindow";
    private const string WindowTitle = "Broiler.Graphics.Windows Direct2D Demo";
    private const int DesiredClientWidth = 1040;
    private const int DesiredClientHeight = 680;
    private const int AnimationTimerId = 1;
    private const uint AnimationTimerIntervalMs = 16;

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

    private const uint WmNccreate = 0x0081;
    private const uint WmNcdestroy = 0x0082;
    private const uint WmCreate = 0x0001;
    private const uint WmDestroy = 0x0002;
    private const uint WmSize = 0x0005;
    private const uint WmPaint = 0x000F;
    private const uint WmEraseBkgnd = 0x0014;
    private const uint WmTimer = 0x0113;
    private const uint WmDpiChanged = 0x02E0;

    private static readonly WndProc s_wndProc = WindowProc;
    private static bool s_classRegistered;

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private IntPtr _hwnd;
    private GCHandle _selfHandle;
    private Direct2DRenderer? _renderer;
    private IBroilerSurface? _surface;
    private BImageHandle _gradientImage;
    private long _frameIndex;
    private bool _disposed;

    public int Run()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ReleaseGraphicsResources();
        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
    }

    private void CreateWindow()
    {
        RECT rect = new(0, 0, DesiredClientWidth, DesiredClientHeight);
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
            WindowTitle,
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
                SetTimer(_hwnd, (nuint)AnimationTimerId, AnimationTimerIntervalMs, IntPtr.Zero);
                return IntPtr.Zero;

            case WmSize:
                if (wParam.ToInt32() != SizeMinimized)
                    ResizeSurface();
                return IntPtr.Zero;

            case WmDpiChanged:
                ResizeSurface();
                InvalidateRect(_hwnd, IntPtr.Zero, false);
                return IntPtr.Zero;

            case WmTimer:
                if (wParam.ToInt32() == AnimationTimerId)
                    InvalidateRect(_hwnd, IntPtr.Zero, false);
                return IntPtr.Zero;

            case WmEraseBkgnd:
                return new IntPtr(1);

            case WmPaint:
                RenderFrame();
                ValidateRect(_hwnd, IntPtr.Zero);
                return IntPtr.Zero;

            case WmDestroy:
                KillTimer(_hwnd, (nuint)AnimationTimerId);
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
        _surface = _renderer.CreateHwndSurface(_hwnd, new BSurfaceDescriptor(
            ClientDipSize,
            DpiScale,
            BPixelFormat.Bgra8,
            EnableTransparency: false));
        _gradientImage = _renderer.CreateImage(CreateGradientImage(128, 128));
    }

    private void ReleaseGraphicsResources()
    {
        if (_renderer is not null && _gradientImage.IsValid)
            _renderer.ReleaseImage(_gradientImage);

        _gradientImage = BImageHandle.Invalid;
        _surface?.Dispose();
        _surface = null;
        _renderer?.Dispose();
        _renderer = null;
    }

    private void ResizeSurface()
    {
        if (_surface is null || !HasDrawableClientArea)
            return;

        try
        {
            _surface.Resize(ClientDipSize, DpiScale);
        }
        catch (BDeviceLostException)
        {
            CreateGraphicsResources();
        }

        InvalidateRect(_hwnd, IntPtr.Zero, false);
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
            BSize size = ClientDipSize;
            var list = new BRenderList(48);
            BuildScene(list, size, _clock.Elapsed.TotalSeconds);

            var frame = new BFrameContext(
                new BColor(18, 21, 28),
                _frameIndex++,
                new BRenderOptions(Antialias: true, VSync: true, SubpixelText: true));

            _renderer.Render(_surface, list, frame);
        }
        catch (BDeviceLostException)
        {
            CreateGraphicsResources();
        }
    }

    private void BuildScene(BRenderList list, BSize size, double seconds)
    {
        double width = size.Width;
        double height = size.Height;
        double pulse = 0.5 + 0.5 * Math.Sin(seconds * 2.0);

        list.FillRect(new BRect(0, 0, width, height), new BColor(18, 21, 28));
        list.FillRect(new BRect(0, 0, width, 92), new BColor(34, 40, 54));
        list.FillRect(new BRect(24, 116, width - 48, height - 154), new BColor(27, 31, 40));
        list.StrokeRect(new BRect(24, 116, width - 48, height - 154), new BColor(76, 91, 116), 1.5);

        list.DrawText(
            new BTextRun(
                "Broiler.Graphics.Windows",
                new BFontStyle("Segoe UI", 30, BFontWeight.Bold),
                BColor.White),
            new BPoint(28, 20));
        list.DrawText(
            new BTextRun(
                "Direct2D swap chain, text, images, clipping, transforms",
                new BFontStyle("Segoe UI", 15),
                new BColor(185, 196, 214)),
            new BPoint(30, 58));

        double imageSize = Math.Min(230, Math.Max(140, height * 0.34));
        var imageDest = new BRect(48, 154, imageSize, imageSize);
        list.FillRect(new BRect(imageDest.X - 10, imageDest.Y - 10, imageDest.Width + 20, imageDest.Height + 20),
            new BColor(37, 45, 59));
        list.DrawImage(
            _gradientImage,
            new BRect(0, 0, 128, 128),
            imageDest,
            0.82 + pulse * 0.18);
        list.StrokeRect(new BRect(imageDest.X - 10, imageDest.Y - 10, imageDest.Width + 20, imageDest.Height + 20),
            new BColor(118, 138, 170),
            2);
        list.DrawText(
            new BTextRun("Image upload + opacity", new BFontStyle("Segoe UI", 15, BFontWeight.SemiBold), new BColor(232, 236, 244)),
            new BPoint(imageDest.X, imageDest.Bottom + 24));

        double panelX = imageDest.Right + 48;
        double panelW = Math.Max(250, width - panelX - 48);
        DrawClippedBars(list, panelX, 154, panelW, 180, seconds);
        DrawTransformDemo(list, panelX, 382, panelW, Math.Max(140, height - 430), seconds);
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

    private double DpiScale
    {
        get
        {
            if (_hwnd == IntPtr.Zero)
                return 1.0;

            uint dpi = GetDpiForWindow(_hwnd);
            return dpi == 0 ? 1.0 : dpi / 96.0;
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

    private static void DrawClippedBars(BRenderList list, double x, double y, double width, double height, double seconds)
    {
        var clip = new BRect(x, y, width, height);
        list.FillRect(clip, new BColor(22, 27, 38));
        list.StrokeRect(clip, new BColor(72, 88, 112), 1.5);
        list.DrawText(
            new BTextRun("PushClip + animated fills", new BFontStyle("Segoe UI", 16, BFontWeight.SemiBold), BColor.White),
            new BPoint(x + 16, y + 14));

        list.PushClip(new BRect(x + 16, y + 50, width - 32, height - 68));
        double offset = (seconds * 70) % 64;
        for (int i = -1; i < 12; i++)
        {
            double bx = x + 16 + i * 64 - offset;
            byte r = (byte)(72 + (i * 19 & 63));
            byte g = (byte)(118 + (i * 11 & 55));
            byte b = (byte)(160 + (i * 7 & 47));
            list.FillRect(new BRect(bx, y + 54, 42, height - 76), new BColor(r, g, b, 220));
        }
        list.PopClip();
    }

    private static void DrawTransformDemo(BRenderList list, double x, double y, double width, double height, double seconds)
    {
        if (height < 80)
            return;

        var panel = new BRect(x, y, width, height);
        list.FillRect(panel, new BColor(22, 27, 38));
        list.StrokeRect(panel, new BColor(72, 88, 112), 1.5);
        list.DrawText(
            new BTextRun("PushTransform stack", new BFontStyle("Segoe UI", 16, BFontWeight.SemiBold), BColor.White),
            new BPoint(x + 16, y + 14));

        double cx = x + width * 0.5;
        double cy = y + Math.Max(84, height * 0.55);
        BMatrix3x2 transform =
            BMatrix3x2.Scale(1.0 + Math.Sin(seconds * 1.7) * 0.18, 1.0 + Math.Cos(seconds * 1.3) * 0.18) *
            Rotation(seconds * 0.9) *
            BMatrix3x2.Translation(cx, cy);

        list.PushTransform(transform);
        list.FillRect(new BRect(-82, -42, 164, 84), new BColor(239, 184, 80));
        list.StrokeRect(new BRect(-82, -42, 164, 84), new BColor(255, 241, 194), 3);
        list.DrawText(
            new BTextRun("D2D", new BFontStyle("Segoe UI", 25, BFontWeight.Bold), new BColor(22, 27, 38)),
            new BPoint(-27, -18));
        list.PopTransform();
    }

    private static BMatrix3x2 Rotation(double radians)
    {
        double c = Math.Cos(radians);
        double s = Math.Sin(radians);
        return new BMatrix3x2(c, s, -s, c, 0, 0);
    }

    private static BPixelBuffer CreateGradientImage(int width, int height)
    {
        byte[] rgba = new byte[width * height * 4];
        int i = 0;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            bool tile = ((x / 16) + (y / 16)) % 2 == 0;
            rgba[i++] = (byte)(tile ? 236 : x * 255 / Math.Max(1, width - 1));
            rgba[i++] = (byte)(tile ? y * 255 / Math.Max(1, height - 1) : 184);
            rgba[i++] = (byte)(tile ? 95 : 228);
            rgba[i++] = (byte)(190 + (x + y) % 66);
        }

        return new BPixelBuffer(width, height, rgba);
    }

    private static int CenteredCoordinate(int screenExtent, int windowExtent)
    {
        if (screenExtent <= 0 || windowExtent <= 0)
            return CwUseDefault;

        return Math.Max(0, (screenExtent - windowExtent) / 2);
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
            if (handle.Target is Direct2DDemoWindow window)
            {
                window._hwnd = hwnd;
                SetWindowLongPtr(hwnd, GwlUserData, createStruct.LpCreateParams);
                return new IntPtr(1);
            }

            return IntPtr.Zero;
        }

        Direct2DDemoWindow? instance = FromHwnd(hwnd);
        return instance is null
            ? DefWindowProc(hwnd, message, wParam, lParam)
            : instance.HandleMessage(message, wParam, lParam);
    }

    private static Direct2DDemoWindow? FromHwnd(IntPtr hwnd)
    {
        IntPtr userData = GetWindowLongPtr(hwnd, GwlUserData);
        if (userData == IntPtr.Zero)
            return null;

        GCHandle handle = GCHandle.FromIntPtr(userData);
        return handle.Target as Direct2DDemoWindow;
    }

}
