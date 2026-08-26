using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Broiler.Graphics.Windows;

internal interface IDirect2DControl : IDisposable
{
    int Id { get; }

    void HandleCommand(int notificationCode, IntPtr sourceHwnd);
}

[SupportedOSPlatform("windows7.0")]
internal sealed class Direct2DEditControl : BEditControl, IDirect2DControl
{
    private const uint WsTabstop = 0x00010000;
    private const uint WsExClientEdge = 0x00000200;
    private const uint EsAutoHScroll = 0x0080;
    private const int EnChange = 0x0300;
    private const int GwlWndProc = -4;
    private const uint WmKeyDown = 0x0100;
    private const int VkReturn = 0x0D;

    private readonly Direct2DControlPeer _peer;
    private readonly WndProc _windowProc;
    private IntPtr _previousWindowProc;

    public Direct2DEditControl(Direct2DWindow owner, int id, BControlOptions options)
    {
        _peer = new Direct2DControlPeer(owner, this, id, "EDIT", WsExClientEdge, WsTabstop | EsAutoHScroll, options);
        _windowProc = EditWindowProc;
        _previousWindowProc = Direct2DControlPeer.SetWindowLongPtr(
            NativeHandle,
            GwlWndProc,
            Marshal.GetFunctionPointerForDelegate(_windowProc));
    }

    public int Id => _peer.Id;

    public override IntPtr NativeHandle => _peer.NativeHandle;

    public override BRect Bounds
    {
        get => _peer.Bounds;
        set => _peer.Bounds = value;
    }

    public override string Text
    {
        get => _peer.Text;
        set => _peer.Text = value;
    }

    public override bool Enabled
    {
        get => _peer.Enabled;
        set => _peer.Enabled = value;
    }

    public override bool Visible
    {
        get => _peer.Visible;
        set => _peer.Visible = value;
    }

    public override void Focus() => _peer.Focus();

    public override void Dispose()
    {
        if (_previousWindowProc != IntPtr.Zero && NativeHandle != IntPtr.Zero)
        {
            Direct2DControlPeer.SetWindowLongPtr(NativeHandle, GwlWndProc, _previousWindowProc);
            _previousWindowProc = IntPtr.Zero;
        }

        _peer.Dispose();
    }

    public void HandleCommand(int notificationCode, IntPtr sourceHwnd)
    {
        if (sourceHwnd == NativeHandle && notificationCode == EnChange)
            OnTextChanged();
    }

    private IntPtr EditWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmKeyDown && wParam.ToInt32() == VkReturn)
        {
            OnSubmitted();
            return IntPtr.Zero;
        }

        return CallWindowProc(_previousWindowProc, hwnd, message, wParam, lParam);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CallWindowProc(IntPtr previousWindowProc, IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}

[SupportedOSPlatform("windows7.0")]
internal sealed class Direct2DButtonControl : BButtonControl, IDirect2DControl
{
    private const uint WsTabstop = 0x00010000;
    private const uint BsPushButton = 0x00000000;
    private const uint BsFlat = 0x00008000;
    private const int BnClicked = 0;

    private readonly Direct2DControlPeer _peer;

    public Direct2DButtonControl(Direct2DWindow owner, int id, BControlOptions options)
    {
        _peer = new Direct2DControlPeer(owner, this, id, "BUTTON", 0, WsTabstop | BsPushButton | BsFlat, options);
    }

    public int Id => _peer.Id;

    public override IntPtr NativeHandle => _peer.NativeHandle;

    public override BRect Bounds
    {
        get => _peer.Bounds;
        set => _peer.Bounds = value;
    }

    public override string Text
    {
        get => _peer.Text;
        set => _peer.Text = value;
    }

    public override bool Enabled
    {
        get => _peer.Enabled;
        set => _peer.Enabled = value;
    }

    public override bool Visible
    {
        get => _peer.Visible;
        set => _peer.Visible = value;
    }

    public override void Focus() => _peer.Focus();

    public override void Dispose() => _peer.Dispose();

    public void HandleCommand(int notificationCode, IntPtr sourceHwnd)
    {
        if (sourceHwnd == NativeHandle && notificationCode == BnClicked)
            OnClicked();
    }
}

[SupportedOSPlatform("windows7.0")]
internal sealed class Direct2DLabelControl : BLabelControl, IDirect2DControl
{
    private const uint SsNoPrefix = 0x00000080;

    private readonly Direct2DControlPeer _peer;

    public Direct2DLabelControl(Direct2DWindow owner, int id, BControlOptions options)
    {
        _peer = new Direct2DControlPeer(owner, this, id, "STATIC", 0, SsNoPrefix, options);
    }

    public int Id => _peer.Id;

    public override IntPtr NativeHandle => _peer.NativeHandle;

    public override BRect Bounds
    {
        get => _peer.Bounds;
        set => _peer.Bounds = value;
    }

    public override string Text
    {
        get => _peer.Text;
        set => _peer.Text = value;
    }

    public override bool Enabled
    {
        get => _peer.Enabled;
        set => _peer.Enabled = value;
    }

    public override bool Visible
    {
        get => _peer.Visible;
        set => _peer.Visible = value;
    }

    public override void Focus()
    {
    }

    public override void Dispose() => _peer.Dispose();

    public void HandleCommand(int notificationCode, IntPtr sourceHwnd)
    {
    }
}

[SupportedOSPlatform("windows7.0")]
internal sealed class Direct2DControlPeer : IDisposable
{
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsClipSiblings = 0x04000000;
    private const uint WmSetFont = 0x0030;
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int DefaultGuiFont = 17;

    private readonly Direct2DWindow _owner;
    private readonly IDirect2DControl _control;
    private BRect _bounds;
    private bool _enabled;
    private bool _visible;
    private bool _disposed;
    private IntPtr _hwnd;

    public Direct2DControlPeer(
        Direct2DWindow owner,
        IDirect2DControl control,
        int id,
        string className,
        uint exStyle,
        uint style,
        BControlOptions options)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _control = control ?? throw new ArgumentNullException(nameof(control));
        Id = id;
        _bounds = options.Bounds;
        _enabled = options.Enabled;
        _visible = options.Visible;

        _hwnd = CreateWindowEx(
            exStyle,
            className,
            options.Text,
            style | WsChild | WsClipSiblings | (options.Visible ? WsVisible : 0),
            ToPixels(_bounds.X),
            ToPixels(_bounds.Y),
            ToPixels(_bounds.Width),
            ToPixels(_bounds.Height),
            owner.NativeHandle,
            new IntPtr(id),
            Direct2DWindow.ModuleHandle,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateWindowEx failed for {className} control.");

        EnableWindow(_hwnd, options.Enabled);
        ApplyDefaultFont();
    }

    public int Id { get; }

    public IntPtr NativeHandle => _hwnd;

    public BRect Bounds
    {
        get => _bounds;
        set
        {
            ThrowIfDisposed();
            _bounds = value;
            MoveWindow(_hwnd, ToPixels(value.X), ToPixels(value.Y), ToPixels(value.Width), ToPixels(value.Height), true);
        }
    }

    public string Text
    {
        get
        {
            ThrowIfDisposed();

            int length = GetWindowTextLength(_hwnd);
            if (length <= 0)
                return string.Empty;

            var builder = new StringBuilder(length + 1);
            _ = GetWindowText(_hwnd, builder, builder.Capacity);
            return builder.ToString();
        }
        set
        {
            ThrowIfDisposed();
            if (!SetWindowText(_hwnd, value ?? string.Empty))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowText failed.");
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            ThrowIfDisposed();
            _enabled = value;
            EnableWindow(_hwnd, value);
        }
    }

    public bool Visible
    {
        get => _visible;
        set
        {
            ThrowIfDisposed();
            _visible = value;
            ShowWindow(_hwnd, value ? SwShow : SwHide);
        }
    }

    public void Focus()
    {
        ThrowIfDisposed();
        SetFocus(_hwnd);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _owner.UnregisterControl(_control);

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private int ToPixels(double dip)
    {
        if (double.IsNaN(dip) || double.IsInfinity(dip))
            return 0;

        return (int)Math.Round(dip * _owner.DpiScale);
    }

    private void ApplyDefaultFont()
    {
        IntPtr font = GetStockObject(DefaultGuiFont);
        if (font != IntPtr.Zero)
            SendMessage(_hwnd, WmSetFont, font, new IntPtr(1));
    }

    public static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hwnd, index, value)
            : new IntPtr(SetWindowLong32(hwnd, index, value.ToInt32()));
    }

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
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindow(IntPtr hwnd, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetWindowTextW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowText(IntPtr hwnd, string text);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextLengthW")]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool enable);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int commandShow);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int objectId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
}
