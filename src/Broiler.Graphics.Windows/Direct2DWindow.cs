using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Broiler.Graphics.Windows;

/// <summary>
/// Win32 window implementation that presents a <see cref="BRenderList"/> directly through
/// <see cref="Direct2DRenderer"/>.
/// </summary>
[SupportedOSPlatform("windows7.0")]
public abstract partial class Direct2DWindow(BWindowOptions options) : BWindow(options)
{
    private const string WindowClassName = "BroilerGraphicsDirect2DWindow";
    private const string RenderHostClassName = "BroilerGraphicsDirect2DRenderHost";

    private const int ErrorClassAlreadyExists = 1410;
    private const int CwUseDefault = unchecked((int)0x80000000);

    private const uint CsHRedraw = 0x0002;
    private const uint CsVRedraw = 0x0001;
    private const uint WsOverlappedWindow = 0x00CF0000;
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsClipChildren = 0x02000000;
    private const uint WsClipSiblings = 0x04000000;
    private const uint WsThickFrame = 0x00040000;
    private const uint WsMaximizeBox = 0x00010000;

    private const int SwShow = 5;
    private const int SwMaximize = 3;
    private const int SwMinimize = 6;
    private const int SwRestore = 9;
    private const int SizeMinimized = 1;
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;
    private const int SmCxSizeFrame = 32;
    private const int SmCxPaddedBorder = 92;
    private const int GwlUserData = -21;
    private const int ColorWindow = 5;
    private const int LogPixelsX = 88;

    private const int HtTransparent = -1;
    private const int HtClient = 1;
    private const int HtCaption = 2;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;

    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const uint DibRgbColors = 0;
    private const uint MonitorDefaultToNearest = 2;

    private const uint WmNccreate = 0x0081;
    private const uint WmNcdestroy = 0x0082;
    private const uint WmCreate = 0x0001;
    private const uint WmDestroy = 0x0002;
    private const uint WmSize = 0x0005;
    private const uint WmCommand = 0x0111;
    private const uint WmPaint = 0x000F;
    private const uint WmEraseBkgnd = 0x0014;
    private const uint WmDpiChanged = 0x02E0;
    private const uint WmTimer = 0x0113;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonDown = 0x0204;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmMButtonDown = 0x0207;
    private const uint WmMButtonUp = 0x0208;
    private const uint WmMouseWheel = 0x020A;
    private const uint WmMouseLeave = 0x02A3;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmChar = 0x0102;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmSetFocus = 0x0007;
    private const uint WmClose = 0x0010;
    private const uint WmSetIcon = 0x0080;
    private const uint WmNccalcsize = 0x0083;
    private const uint WmNchittest = 0x0084;
    private const uint WmNcactivate = 0x0086;
    private const uint WmNcLButtonDown = 0x00A1;
    private const uint WmInvoke = 0x8001;

    private const int MkLButton = 0x0001;
    private const int MkRButton = 0x0002;
    private const int MkMButton = 0x0010;

    private const int VkControl = 0x11;
    private const int VkShift = 0x10;
    private const int VkMenu = 0x12;

    private const int WheelDelta = 120;
    private const uint TmeLeave = 0x00000002;
    private const nuint AnimationTimerId = 1;

    private static readonly WndProc s_wndProc = WindowProc;
    private static readonly WndProc s_renderHostWndProc = RenderHostWindowProc;
    private static bool s_classRegistered;
    private static bool s_renderHostClassRegistered;

    private IntPtr _hwnd;
    private IntPtr _renderHwnd;
    private GCHandle _selfHandle;
    private Direct2DRenderer? _renderer;
    private IBroilerSurface? _surface;
    private readonly Dictionary<int, IDirect2DControl> _controls = [];
    private readonly Queue<Action> _postedCallbacks = new();
    private long _frameIndex;
    private int _nextControlId = 1000;
    private bool _runStarted;
    private bool _trackingMouse;
    private bool _animationTimerRunning;
    private bool _closing;
    private IntPtr _largeIcon;
    private IntPtr _smallIcon;
    private BWindowState _lastReportedState = BWindowState.Normal;

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

    public override BWindowState WindowState
    {
        get
        {
            if (_hwnd == IntPtr.Zero)
                return BWindowState.Normal;
            if (IsIconic(_hwnd))
                return BWindowState.Minimized;
            return IsZoomed(_hwnd) ? BWindowState.Maximized : BWindowState.Normal;
        }
    }

    protected IntPtr RenderNativeHandle => _renderHwnd;

    /// <summary>True when the platform frame is suppressed and the app draws its own chrome.</summary>
    protected bool IsOwnerDrawnChrome => Options.Chrome == BWindowChrome.Owner;

    protected override int RunCore()
    {
        if (_runStarted)
            throw new InvalidOperationException("A Direct2DWindow instance can only be run once.");
        if (!Options.OwnsMessageLoop)
            throw new InvalidOperationException(
                "A window created with OwnsMessageLoop = false is realized with Show() and serviced by an existing message loop.");

        _runStarted = true;
        EnsureNativeWindow();
        ShowNativeWindow();

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

    protected override void ShowCore()
    {
        EnsureNativeWindow();
        ShowNativeWindow();
    }

    protected override void CloseCore()
    {
        if (_hwnd != IntPtr.Zero)
            DestroyWindow(_hwnd);
    }

    protected override void SetTitleCore(string title)
    {
        if (_hwnd != IntPtr.Zero)
            SetWindowText(_hwnd, title);
    }

    protected override void SetIconCore(BPixelBuffer? icon)
    {
        if (_hwnd == IntPtr.Zero)
            return;

        IntPtr large = icon is null ? IntPtr.Zero : CreateIconFromPixels(icon);
        IntPtr small = icon is null ? IntPtr.Zero : CreateIconFromPixels(icon);

        SendMessage(_hwnd, WmSetIcon, new IntPtr(IconBig), large);
        SendMessage(_hwnd, WmSetIcon, new IntPtr(IconSmall), small);

        ReleaseIcons();
        _largeIcon = large;
        _smallIcon = small;
    }

    protected override void SetWindowStateCore(BWindowState state)
    {
        if (_hwnd == IntPtr.Zero)
            return;

        ShowWindow(_hwnd, state switch
        {
            BWindowState.Minimized => SwMinimize,
            BWindowState.Maximized => SwMaximize,
            _ => SwRestore,
        });
    }

    protected override void BeginMoveDragCore() => BeginNonClientDrag(HtCaption);

    protected override void BeginResizeDragCore(BWindowEdge edge)
    {
        if (!Options.Resizable || IsZoomed(_hwnd))
            return;

        BeginNonClientDrag(edge switch
        {
            BWindowEdge.Left => HtLeft,
            BWindowEdge.Top => HtTop,
            BWindowEdge.Right => HtRight,
            BWindowEdge.Bottom => HtBottom,
            BWindowEdge.TopLeft => HtTopLeft,
            BWindowEdge.TopRight => HtTopRight,
            BWindowEdge.BottomLeft => HtBottomLeft,
            BWindowEdge.BottomRight => HtBottomRight,
            _ => HtCaption,
        });
    }

    protected override void InvalidateCore()
    {
        if (_renderHwnd != IntPtr.Zero)
            InvalidateRect(_renderHwnd, IntPtr.Zero, false);
        else if (_hwnd != IntPtr.Zero)
            InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    protected override void StartAnimationTimerCore(double intervalMilliseconds)
    {
        EnsureNativeHandle();
        uint interval = (uint)Math.Max(1, Math.Round(intervalMilliseconds));
        if (SetTimer(_hwnd, AnimationTimerId, interval, IntPtr.Zero) == UIntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetTimer failed.");
        _animationTimerRunning = true;
    }

    protected override void StopAnimationTimerCore()
    {
        if (!_animationTimerRunning || _hwnd == IntPtr.Zero)
            return;

        KillTimer(_hwnd, AnimationTimerId);
        _animationTimerRunning = false;
    }

    protected bool PostToUiThread(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (_closing || _hwnd == IntPtr.Zero || IsDisposed)
            return false;

        lock (_postedCallbacks)
            _postedCallbacks.Enqueue(callback);

        if (PostMessage(_hwnd, WmInvoke, IntPtr.Zero, IntPtr.Zero))
            return true;

        lock (_postedCallbacks)
            RemovePostedCallback(callback);

        return false;
    }

    protected override BEditControl CreateEditControlCore(BControlOptions options)
    {
        ThrowIfDisposed();
        EnsureNativeHandle();

        int id = AllocateControlId();
        var control = new Direct2DEditControl(this, id, options ?? throw new ArgumentNullException(nameof(options)));
        _controls.Add(id, control);
        return control;
    }

    protected override BButtonControl CreateButtonControlCore(BControlOptions options)
    {
        ThrowIfDisposed();
        EnsureNativeHandle();

        int id = AllocateControlId();
        var control = new Direct2DButtonControl(this, id, options ?? throw new ArgumentNullException(nameof(options)));
        _controls.Add(id, control);
        return control;
    }

    protected override BLabelControl CreateLabelControlCore(BControlOptions options)
    {
        ThrowIfDisposed();
        EnsureNativeHandle();

        int id = AllocateControlId();
        var control = new Direct2DLabelControl(this, id, options ?? throw new ArgumentNullException(nameof(options)));
        _controls.Add(id, control);
        return control;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            BeginClosing();
            StopAnimationTimerCore();
            DisposeControls();
            ReleaseGraphicsResources();
            DestroyRenderHost();

            // A secondary window is not torn down by a quitting message loop, so disposing it has
            // to destroy the native window itself.
            if (_hwnd != IntPtr.Zero)
                DestroyWindow(_hwnd);

            ReleaseIcons();
        }

        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
    }

    internal static IntPtr ModuleHandle => GetModuleHandle(null);

    internal void UnregisterControl(IDirect2DControl control)
    {
        if (_controls.TryGetValue(control.Id, out IDirect2DControl? existing) && ReferenceEquals(existing, control))
            _controls.Remove(control.Id);
    }

    private void EnsureNativeWindow()
    {
        if (_hwnd != IntPtr.Zero)
            return;

        EnsureWindowClassRegistered();
        EnsureRenderHostClassRegistered();
        CreateWindow();
    }

    private void ShowNativeWindow()
    {
        if (_hwnd == IntPtr.Zero)
            return;

        ShowWindow(_hwnd, SwShow);
        UpdateWindow(_hwnd);
    }

    private void CreateWindow()
    {
        uint initialDpi = GetInitialDpi();
        double initialDpiScale = initialDpi / 96.0;
        uint style = ResolveWindowStyle();
        RECT rect = new(
            0,
            0,
            ToInitialPixels(EffectiveClientWidth, initialDpiScale),
            ToInitialPixels(EffectiveClientHeight, initialDpiScale));

        // Owner-drawn chrome makes the whole window client area, so the requested client extent is
        // already the window extent; only a system frame has to be added around it.
        if (!IsOwnerDrawnChrome && !AdjustWindowRectForInitialDpi(ref rect, style, false, 0, initialDpi))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "AdjustWindowRectEx failed.");

        int width = rect.Width;
        int height = rect.Height;
        int x = Options.Left is { } requestedLeft
            ? ToInitialPixels((int)Math.Round(requestedLeft), initialDpiScale)
            : CenteredCoordinate(GetSystemMetrics(SmCxScreen), width);
        int y = Options.Top is { } requestedTop
            ? ToInitialPixels((int)Math.Round(requestedTop), initialDpiScale)
            : CenteredCoordinate(GetSystemMetrics(SmCyScreen), height);

        _selfHandle = GCHandle.Alloc(this);
        IntPtr lpParam = GCHandle.ToIntPtr(_selfHandle);

        _hwnd = CreateWindowEx(
            0,
            WindowClassName,
            string.IsNullOrWhiteSpace(Options.Title) ? "Broiler.Graphics" : Options.Title,
            style,
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

    /// <summary>
    /// Owner-drawn chrome keeps the overlapped style - the caption is what makes Aero snap, the
    /// drop shadow, and the minimize/restore animations work - and suppresses only its drawing, by
    /// handing the whole window rect back as client area from WM_NCCALCSIZE.
    /// </summary>
    private uint ResolveWindowStyle()
    {
        uint style = WsOverlappedWindow | WsClipChildren;
        if (!Options.Resizable)
            style &= ~(WsThickFrame | WsMaximizeBox);

        return style;
    }

    /// <summary>Width of the invisible resize border an owner-drawn frame hit-tests, in pixels.</summary>
    private int ResizeGripPixels =>
        Math.Max(4, GetSystemMetrics(SmCxSizeFrame) + GetSystemMetrics(SmCxPaddedBorder));

    /// <summary>
    /// Hands the full window rect back as client area so no caption or border is drawn. A maximized
    /// frameless window is inset by the frame thickness and clamped to the monitor work area -
    /// without that, Windows sizes it past the screen edges and over the taskbar.
    /// </summary>
    private void AdjustOwnerDrawnClientArea(IntPtr lParam)
    {
        // For both wParam forms the first 16 bytes at lParam are the proposed window rect on the
        // way in and the new client rect on the way out (NCCALCSIZE_PARAMS starts with rgrc[0]).

        if (!IsZoomed(_hwnd))
            return;

        int frame = ResizeGripPixels;
        int left = Marshal.ReadInt32(lParam, 0) + frame;
        int top = Marshal.ReadInt32(lParam, 4) + frame;
        int right = Marshal.ReadInt32(lParam, 8) - frame;
        int bottom = Marshal.ReadInt32(lParam, 12) - frame;

        if (TryGetWorkArea(out RECT work))
        {
            left = Math.Max(left, work.Left);
            top = Math.Max(top, work.Top);
            right = Math.Min(right, work.Right);
            bottom = Math.Min(bottom, work.Bottom);
        }

        Marshal.WriteInt32(lParam, 0, left);
        Marshal.WriteInt32(lParam, 4, top);
        Marshal.WriteInt32(lParam, 8, right);
        Marshal.WriteInt32(lParam, 12, bottom);
    }

    /// <summary>
    /// Resize-border hit test for an owner-drawn frame. <paramref name="lParam"/> carries the
    /// cursor in screen coordinates, so the render-host child reuses this to report
    /// <c>HTTRANSPARENT</c> over the border and let the top-level window own the resize.
    /// </summary>
    private int HitTestOwnerDrawnFrame(IntPtr lParam)
    {
        if (!Options.Resizable || IsZoomed(_hwnd) || !GetWindowRect(_hwnd, out RECT window))
            return HtClient;

        int x = SignedLowWord(lParam);
        int y = SignedHighWord(lParam);
        int grip = ResizeGripPixels;

        bool left = x < window.Left + grip;
        bool right = x >= window.Right - grip;
        bool top = y < window.Top + grip;
        bool bottom = y >= window.Bottom - grip;

        if (top)
            return left ? HtTopLeft : right ? HtTopRight : HtTop;
        if (bottom)
            return left ? HtBottomLeft : right ? HtBottomRight : HtBottom;
        if (left)
            return HtLeft;
        if (right)
            return HtRight;

        return HtClient;
    }

    private bool TryGetWorkArea(out RECT workArea)
    {
        workArea = default;
        IntPtr monitor = MonitorFromWindow(_hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return false;

        var info = new MONITORINFO { CbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
            return false;

        workArea = info.RcWork;
        return true;
    }

    private void BeginNonClientDrag(int hitTest)
    {
        if (_hwnd == IntPtr.Zero || _closing)
            return;

        // The press that started the drag is still captured by the render-host child; the window
        // manager needs it released before it takes over the move/resize loop.
        ReleaseCapture();
        SendMessage(_hwnd, WmNcLButtonDown, new IntPtr(hitTest), IntPtr.Zero);
    }

    private void ReportWindowStateChange()
    {
        BWindowState state = WindowState;
        if (state == _lastReportedState)
            return;

        _lastReportedState = state;
        RaiseStateChanged();
    }

    private void ReleaseIcons()
    {
        if (_largeIcon != IntPtr.Zero)
        {
            DestroyIcon(_largeIcon);
            _largeIcon = IntPtr.Zero;
        }

        if (_smallIcon != IntPtr.Zero)
        {
            DestroyIcon(_smallIcon);
            _smallIcon = IntPtr.Zero;
        }
    }

    /// <summary>Builds an HICON from straight-alpha RGBA pixels, or zero when it cannot.</summary>
    private static IntPtr CreateIconFromPixels(BPixelBuffer icon)
    {
        var header = new BITMAPINFOHEADER
        {
            BiSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            BiWidth = icon.Width,
            BiHeight = -icon.Height, // top-down
            BiPlanes = 1,
            BiBitCount = 32,
            BiCompression = 0,
        };

        IntPtr color = CreateDIBSection(IntPtr.Zero, ref header, DibRgbColors, out IntPtr bits, IntPtr.Zero, 0);
        if (color == IntPtr.Zero || bits == IntPtr.Zero)
        {
            if (color != IntPtr.Zero)
                DeleteObject(color);
            return IntPtr.Zero;
        }

        IntPtr mask = IntPtr.Zero;
        try
        {
            // BPixelBuffer is RGBA; a 32bpp DIB is BGRA.
            byte[] bgra = new byte[icon.Rgba.Length];
            for (int i = 0; i + 3 < bgra.Length; i += 4)
            {
                bgra[i] = icon.Rgba[i + 2];
                bgra[i + 1] = icon.Rgba[i + 1];
                bgra[i + 2] = icon.Rgba[i];
                bgra[i + 3] = icon.Rgba[i + 3];
            }

            Marshal.Copy(bgra, 0, bits, bgra.Length);

            mask = CreateBitmap(icon.Width, icon.Height, 1, 1, IntPtr.Zero);
            if (mask == IntPtr.Zero)
                return IntPtr.Zero;

            var info = new ICONINFO
            {
                FIcon = 1,
                XHotspot = 0,
                YHotspot = 0,
                HbmMask = mask,
                HbmColor = color,
            };

            return CreateIconIndirect(ref info);
        }
        finally
        {
            if (mask != IntPtr.Zero)
                DeleteObject(mask);
            DeleteObject(color);
        }
    }

    private IntPtr HandleMessage(uint message, IntPtr wParam, IntPtr lParam)
    {
        OnNativeWindowMessage(_hwnd, message, wParam, lParam);
        switch (message)
        {
            // Owner-drawn chrome: report the whole window rect as client area, so Windows draws
            // neither caption nor border and the UI paints its own title bar into that space.
            // Both wParam forms matter - a window created hidden gets the FALSE form first, and
            // letting DefWindowProc answer that one puts the frame straight back.
            case WmNccalcsize when IsOwnerDrawnChrome:
                AdjustOwnerDrawnClientArea(lParam);
                return IntPtr.Zero;

            // The frame is gone, so the resize border has to be hit-tested by hand. Everything
            // that is not a border is client area; the title bar drives moves through
            // BeginMoveDrag rather than reporting HTCAPTION here, which would swallow its buttons.
            case WmNchittest when IsOwnerDrawnChrome:
                return new IntPtr(HitTestOwnerDrawnFrame(lParam));

            // The caption still exists as a style; -1 keeps DefWindowProc from repainting it.
            case WmNcactivate when IsOwnerDrawnChrome:
                return DefWindowProc(_hwnd, message, wParam, new IntPtr(-1));

            case WmCreate:
                CreateRenderHost();
                CreateGraphicsResources();
                OnCreated();
                return IntPtr.Zero;

            // A window that owns the loop closes the way Windows intends. A secondary window only
            // reports the request: its owner decides whether to Close() it, so a cancelled dialog
            // close does not destroy the window under the framework.
            case WmClose:
                RaiseCloseRequested();
                return Options.OwnsMessageLoop
                    ? DefWindowProc(_hwnd, message, wParam, lParam)
                    : IntPtr.Zero;

            case WmSize:
                if (wParam.ToInt32() != SizeMinimized)
                    ResizeSurfaceAndNotify();
                ReportWindowStateChange();
                return IntPtr.Zero;

            case WmCommand:
                if (_closing)
                    return IntPtr.Zero;

                if (HandleCommand(wParam, lParam))
                    return IntPtr.Zero;
                return DefWindowProc(_hwnd, message, wParam, lParam);

            case WmDpiChanged:
                ResizeSurfaceAndNotify();
                return IntPtr.Zero;

            case WmTimer:
                if (_closing)
                    return IntPtr.Zero;

                if ((nuint)wParam == AnimationTimerId)
                {
                    OnAnimationTick();
                    return IntPtr.Zero;
                }
                return DefWindowProc(_hwnd, message, wParam, lParam);

            case WmInvoke:
                DrainPostedCallbacks();
                return IntPtr.Zero;

            case WmEraseBkgnd:
                return DefWindowProc(_hwnd, message, wParam, lParam);

            case WmPaint:
                ValidateRect(_hwnd, IntPtr.Zero);
                return IntPtr.Zero;

            case WmDestroy:
                BeginClosing();
                StopAnimationTimerCore();
                ReleaseGraphicsResources();
                DestroyRenderHost();

                // Only the window that owns the loop may end it; destroying a secondary window
                // must leave the application running.
                if (Options.OwnsMessageLoop)
                    PostQuitMessage(0);
                return IntPtr.Zero;

            case WmNcdestroy:
                IntPtr destroyedHwnd = _hwnd;
                SetWindowLongPtr(_hwnd, GwlUserData, IntPtr.Zero);
                if (_selfHandle.IsAllocated)
                    _selfHandle.Free();
                _hwnd = IntPtr.Zero;
                IntPtr result = DefWindowProc(destroyedHwnd, message, wParam, lParam);
                RaiseClosed();
                return result;

            default:
                return DefWindowProc(_hwnd, message, wParam, lParam);
        }
    }

    private void CreateGraphicsResources()
    {
        if (_closing)
            return;

        ReleaseGraphicsResources();

        if (_renderHwnd == IntPtr.Zero || !HasDrawableRenderArea)
            return;

        _renderer = new Direct2DRenderer();
        _surface = _renderer.CreateHwndSurface(
            _renderHwnd,
            new BSurfaceDescriptor(RenderDipSize, DpiScale, BPixelFormat.Bgra8, Options.EnableTransparency));
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

    private void DisposeControls()
    {
        if (_controls.Count == 0)
            return;

        var controls = new List<IDirect2DControl>(_controls.Values);
        _controls.Clear();

        foreach (IDirect2DControl control in controls)
            control.Dispose();
    }

    private void ResizeSurfaceAndNotify()
    {
        if (_closing)
            return;

        MoveRenderHost();

        if (!HasDrawableRenderArea)
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
            _surface.Resize(RenderDipSize, DpiScale);
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
        if (_closing)
            return;

        if (!HasDrawableRenderArea)
            return;

        if (_renderer is null || _surface is null)
            CreateGraphicsResources();

        if (_renderer is null || _surface is null)
            return;

        try
        {
            BRenderList? renderList = BuildRenderList(RenderDipSize);
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

    private bool HasDrawableRenderArea => HasDrawableClientArea && !RenderBounds.IsEmpty;

    private BRect RenderBounds => NormalizeRenderBounds(GetRenderBounds(ClientDipSize), ClientDipSize);

    private BSize RenderDipSize => RenderBounds.Size;

    protected virtual BRect GetRenderBounds(BSize clientSize) =>
        new(0, 0, clientSize.Width, clientSize.Height);

    protected virtual void OnNativeWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
    }

    private static BRect NormalizeRenderBounds(BRect bounds, BSize clientSize)
    {
        if (clientSize.IsEmpty || bounds.IsEmpty)
            return BRect.Empty;

        return bounds.Intersect(new BRect(0, 0, clientSize.Width, clientSize.Height));
    }

    private int EffectiveClientWidth => Math.Max(1, Options.ClientWidth);

    private int EffectiveClientHeight => Math.Max(1, Options.ClientHeight);

    private static int ToInitialPixels(int dip, double dpiScale) =>
        Math.Max(1, (int)Math.Round(dip * dpiScale));

    private static uint GetInitialDpi() => GetDeviceDpi(IntPtr.Zero);

    private static bool AdjustWindowRectForInitialDpi(ref RECT rect, uint style, bool menu, uint exStyle, uint dpi)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
            return AdjustWindowRectExForDpi(ref rect, style, menu, exStyle, dpi);

        return AdjustWindowRectEx(ref rect, style, menu, exStyle);
    }

    private int AllocateControlId() => _nextControlId++;

    private void EnsureNativeHandle()
    {
        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException("Controls can only be created after the native window exists.");
    }

    private void CreateRenderHost()
    {
        if (_renderHwnd != IntPtr.Zero)
            return;

        BRect bounds = RenderBounds;
        _renderHwnd = CreateWindowEx(
            0,
            RenderHostClassName,
            string.Empty,
            WsChild | WsVisible | WsClipSiblings,
            ToPixels(bounds.X),
            ToPixels(bounds.Y),
            ToPixels(bounds.Width),
            ToPixels(bounds.Height),
            _hwnd,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        if (_renderHwnd == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx failed for Direct2D render host.");
    }

    private void MoveRenderHost()
    {
        if (_renderHwnd == IntPtr.Zero)
            return;

        BRect bounds = RenderBounds;
        MoveWindow(
            _renderHwnd,
            ToPixels(bounds.X),
            ToPixels(bounds.Y),
            ToPixels(bounds.Width),
            ToPixels(bounds.Height),
            true);
    }

    private void DestroyRenderHost()
    {
        if (_renderHwnd == IntPtr.Zero)
            return;

        DestroyWindow(_renderHwnd);
        _renderHwnd = IntPtr.Zero;
    }

    private void BeginClosing()
    {
        if (_closing)
            return;

        _closing = true;
        OnClosing();
        DrainPostedCallbacks();
    }

    private void DrainPostedCallbacks()
    {
        while (true)
        {
            Action[] callbacks;
            lock (_postedCallbacks)
            {
                if (_postedCallbacks.Count == 0)
                    return;

                callbacks = [.. _postedCallbacks];
                _postedCallbacks.Clear();
            }

            foreach (Action callback in callbacks)
            {
                try
                {
                    callback();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }
    }

    private void RemovePostedCallback(Action callback)
    {
        if (_postedCallbacks.Count == 0)
            return;

        var retained = new Queue<Action>(_postedCallbacks.Count);
        while (_postedCallbacks.Count > 0)
        {
            Action pending = _postedCallbacks.Dequeue();
            if (!ReferenceEquals(pending, callback))
                retained.Enqueue(pending);
        }

        while (retained.Count > 0)
            _postedCallbacks.Enqueue(retained.Dequeue());
    }

    private int ToPixels(double dip)
    {
        if (double.IsNaN(dip) || double.IsInfinity(dip))
            return 0;

        return (int)Math.Round(dip * DpiScale);
    }

    private bool HandleCommand(IntPtr wParam, IntPtr lParam)
    {
        int id = LowWord(wParam);
        if (!_controls.TryGetValue(id, out IDirect2DControl? control))
            return false;

        control.HandleCommand(HighWord(wParam), lParam);
        return true;
    }

    private static int LowWord(IntPtr value) => unchecked((ushort)((long)value & 0xFFFF));

    private static int HighWord(IntPtr value) => unchecked((ushort)(((long)value >> 16) & 0xFFFF));

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
            HbrBackground = GetSysColorBrush(ColorWindow),
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

    private static void EnsureRenderHostClassRegistered()
    {
        if (s_renderHostClassRegistered)
            return;

        var windowClass = new WNDCLASSEX
        {
            CbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            Style = CsHRedraw | CsVRedraw,
            LpfnWndProc = s_renderHostWndProc,
            HInstance = GetModuleHandle(null),
            HCursor = LoadCursor(IntPtr.Zero, new IntPtr(32512)),
            LpszClassName = RenderHostClassName,
        };

        ushort atom = RegisterClassEx(ref windowClass);
        if (atom == 0)
        {
            int error = Marshal.GetLastWin32Error();
            if (error != ErrorClassAlreadyExists)
                throw new Win32Exception(error, "RegisterClassEx failed for Direct2D render host.");
        }

        s_renderHostClassRegistered = true;
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

    private static IntPtr RenderHostWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        Direct2DWindow? instance = FromRenderHostHwnd(hwnd);
        if (instance is null)
            return DefWindowProc(hwnd, message, wParam, lParam);

        instance.OnNativeWindowMessage(hwnd, message, wParam, lParam);

        switch (message)
        {
            // The render host covers the whole owner-drawn window, including the invisible resize
            // border. Reporting HTTRANSPARENT there continues the hit test in the top-level
            // window, which owns the resize; everything else stays with the render host.
            case WmNchittest when instance.IsOwnerDrawnChrome:
                return instance.HitTestOwnerDrawnFrame(lParam) == HtClient
                    ? new IntPtr(HtClient)
                    : new IntPtr(HtTransparent);

            case WmPaint:
                instance.RenderFrame();
                ValidateRect(hwnd, IntPtr.Zero);
                return IntPtr.Zero;

            case WmEraseBkgnd:
                return new IntPtr(1);

            case WmMouseMove:
                instance.EnsureMouseTracking(hwnd);
                instance.OnPointerMove(new BPointerEventArgs(instance.LParamToDip(lParam), ButtonsFromWParam(wParam)));
                return IntPtr.Zero;

            case WmMouseLeave:
                instance._trackingMouse = false;
                instance.OnPointerLeave();
                return IntPtr.Zero;

            case WmLButtonDown:
            case WmRButtonDown:
            case WmMButtonDown:
                SetFocus(hwnd);
                instance.OnPointerDown(new BPointerEventArgs(
                    instance.LParamToDip(lParam),
                    ButtonsFromWParam(wParam),
                    ButtonFromMouseMessage(message)));
                return IntPtr.Zero;

            case WmLButtonUp:
            case WmRButtonUp:
            case WmMButtonUp:
                instance.OnPointerUp(new BPointerEventArgs(
                    instance.LParamToDip(lParam),
                    ButtonsFromWParam(wParam),
                    ButtonFromMouseMessage(message)));
                return IntPtr.Zero;

            case WmMouseWheel:
                instance.DispatchMouseWheel(hwnd, wParam, lParam);
                return IntPtr.Zero;

            case WmKeyDown:
            case WmSysKeyDown:
                instance.OnKeyDown(KeyArgsFromWParam(wParam));
                return DefWindowProc(hwnd, message, wParam, lParam);

            case WmKeyUp:
                instance.OnKeyUp(KeyArgsFromWParam(wParam));
                return IntPtr.Zero;

            case WmChar:
                instance.OnTextInput(new BTextInputEventArgs((char)(ushort)(long)wParam));
                return IntPtr.Zero;

            default:
                return DefWindowProc(hwnd, message, wParam, lParam);
        }
    }

    private void EnsureMouseTracking(IntPtr hwnd)
    {
        if (_trackingMouse)
            return;

        var tme = new TRACKMOUSEEVENT
        {
            CbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(),
            DwFlags = TmeLeave,
            HwndTrack = hwnd,
            DwHoverTime = 0,
        };
        if (TrackMouseEvent(ref tme))
            _trackingMouse = true;
    }

    private void DispatchMouseWheel(IntPtr hwnd, IntPtr wParam, IntPtr lParam)
    {
        // WM_MOUSEWHEEL reports the cursor position in screen coordinates; map it into the render host.
        var point = new POINT { X = SignedLowWord(lParam), Y = SignedHighWord(lParam) };
        ScreenToClient(hwnd, ref point);
        double delta = SignedHighWord(wParam) / (double)WheelDelta;
        OnMouseWheel(new BMouseWheelEventArgs(PixelsToDip(point.X, point.Y), delta, ButtonsFromWParam(wParam)));
    }

    private BPoint LParamToDip(IntPtr lParam) => PixelsToDip(SignedLowWord(lParam), SignedHighWord(lParam));

    private BPoint PixelsToDip(int x, int y)
    {
        double scale = DpiScale;
        return new BPoint(x / scale, y / scale);
    }

    private static BMouseButtons ButtonsFromWParam(IntPtr wParam)
    {
        int keys = LowWord(wParam);
        BMouseButtons buttons = BMouseButtons.None;
        if ((keys & MkLButton) != 0)
            buttons |= BMouseButtons.Left;
        if ((keys & MkRButton) != 0)
            buttons |= BMouseButtons.Right;
        if ((keys & MkMButton) != 0)
            buttons |= BMouseButtons.Middle;
        return buttons;
    }

    private static BMouseButtons ButtonFromMouseMessage(uint message) => message switch
    {
        WmLButtonDown or WmLButtonUp => BMouseButtons.Left,
        WmRButtonDown or WmRButtonUp => BMouseButtons.Right,
        WmMButtonDown or WmMButtonUp => BMouseButtons.Middle,
        _ => BMouseButtons.None,
    };

    private static BKeyEventArgs KeyArgsFromWParam(IntPtr wParam) =>
        new(
            (int)(long)wParam,
            (GetKeyState(VkControl) & 0x8000) != 0,
            (GetKeyState(VkShift) & 0x8000) != 0,
            (GetKeyState(VkMenu) & 0x8000) != 0);

    private static int SignedLowWord(IntPtr value) => unchecked((short)((long)value & 0xFFFF));

    private static int SignedHighWord(IntPtr value) => unchecked((short)(((long)value >> 16) & 0xFFFF));

    private static Direct2DWindow? FromHwnd(IntPtr hwnd)
    {
        IntPtr userData = GetWindowLongPtr(hwnd, GwlUserData);
        if (userData == IntPtr.Zero)
            return null;

        GCHandle handle = GCHandle.FromIntPtr(userData);
        return handle.Target as Direct2DWindow;
    }

    private static Direct2DWindow? FromRenderHostHwnd(IntPtr hwnd)
    {
        IntPtr parent = GetParent(hwnd);
        return parent == IntPtr.Zero ? null : FromHwnd(parent);
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
    private readonly struct RECT(int left, int top, int right, int bottom)
    {
        public readonly int Left = left;
        public readonly int Top = top;
        public readonly int Right = right;
        public readonly int Bottom = bottom;

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
    private struct TRACKMOUSEEVENT
    {
        public uint CbSize;
        public uint DwFlags;
        public IntPtr HwndTrack;
        public uint DwHoverTime;
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

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16, EntryPoint = "GetModuleHandleW")]
    private static partial IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateWindowEx(
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

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustWindowRectEx(ref RECT rect, uint style, [MarshalAs(UnmanagedType.Bool)] bool menu, uint exStyle);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustWindowRectExForDpi(ref RECT rect, uint style, [MarshalAs(UnmanagedType.Bool)] bool menu, uint exStyle, uint dpi);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hwnd, int commandShow);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateWindow(IntPtr hwnd);

    [LibraryImport("user32.dll", SetLastError = true, EntryPoint = "GetMessageW")]
    private static partial int GetMessage(out MSG message, IntPtr hwnd, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref MSG message);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial IntPtr DispatchMessage(ref MSG message);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll", SetLastError = true, EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InvalidateRect(IntPtr hwnd, IntPtr rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ValidateRect(IntPtr hwnd, IntPtr rect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveWindow(IntPtr hwnd, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetParent(IntPtr hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial UIntPtr SetTimer(IntPtr hwnd, nuint eventId, uint elapseMs, IntPtr timerFunc);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool KillTimer(IntPtr hwnd, nuint eventId);

    [LibraryImport("user32.dll")]
    private static partial IntPtr SetFocus(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial short GetKeyState(int virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ScreenToClient(IntPtr hwnd, ref POINT point);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TrackMouseEvent(ref TRACKMOUSEEVENT trackMouseEvent);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(IntPtr hwnd, out RECT rect);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr GetDC(IntPtr hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDeviceCaps(IntPtr hdc, int index);

    [LibraryImport("user32.dll", SetLastError = true, EntryPoint = "LoadCursorW")]
    private static partial IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetSysColorBrush(int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static partial int SetWindowLong32(IntPtr hwnd, int index, int value);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLong32(IntPtr hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowTextW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowText(IntPtr hwnd, string title);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReleaseCapture();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsZoomed(IntPtr hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr icon);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr CreateIconIndirect(ref ICONINFO iconInfo);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial IntPtr CreateDIBSection(
        IntPtr hdc,
        ref BITMAPINFOHEADER header,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, IntPtr bits);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr gdiObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint CbSize;
        public RECT RcMonitor;
        public RECT RcWork;
        public uint DwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public int FIcon;
        public int XHotspot;
        public int YHotspot;
        public IntPtr HbmMask;
        public IntPtr HbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint BiSize;
        public int BiWidth;
        public int BiHeight;
        public ushort BiPlanes;
        public ushort BiBitCount;
        public uint BiCompression;
        public uint BiSizeImage;
        public int BiXPelsPerMeter;
        public int BiYPelsPerMeter;
        public uint BiClrUsed;
        public uint BiClrImportant;
    }
}
