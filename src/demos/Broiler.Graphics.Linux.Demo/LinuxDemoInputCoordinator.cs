using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input;
using Broiler.Input.Keyboard;
using Broiler.Input.Keyboard.Linux;
using Broiler.Input.Linux;
using Broiler.Input.Mouse;
using Broiler.Input.Mouse.Linux;

namespace Broiler.Graphics.Linux.Demo;

internal sealed class LinuxDemoInputCoordinator : IAsyncDisposable
{
    private readonly bool _enabled;
    private readonly Action<string> _log;
    private readonly object _gate = new();
    private LinuxKeyboardProvider? _keyboardProvider;
    private LinuxMouseProvider? _mouseProvider;
    private KeyboardInputDevice? _keyboard;
    private MouseInputDevice? _mouse;
    private bool _active;
    private bool _initialized;
    private bool _disposed;
    private double _pointerX;
    private double _pointerY;
    private int _accentIndex;
    private int _keyEvents;
    private int _mouseMoveEvents;
    private int _mouseButtonEvents;
    private int _mouseWheelEvents;
    private string? _keyboardSummary;
    private string? _mouseSummary;
    private bool _quitRequested;

    public LinuxDemoInputCoordinator(bool enabled, Action<string> log)
    {
        _enabled = enabled;
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public bool IsEnabled => _enabled;

    public bool IsInitialized => _initialized;

    public bool IsActive => _active;

    public bool QuitRequested
    {
        get
        {
            lock (_gate)
                return _quitRequested;
        }
    }

    public LinuxDemoInputSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new LinuxDemoInputSnapshot(
                    _enabled,
                    _initialized,
                    _active,
                    _pointerX,
                    _pointerY,
                    _accentIndex,
                    _keyEvents,
                    _mouseMoveEvents,
                    _mouseButtonEvents,
                    _mouseWheelEvents,
                    _keyboardSummary,
                    _mouseSummary);
            }
        }
    }

    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_enabled || _initialized)
            return;

        if (!OperatingSystem.IsLinux())
        {
            _log("evdev input requested, but Linux input devices are only opened on Linux.");
            return;
        }

        LinuxEvdevProviderOptions options = new(AcknowledgeRawBackgroundInput: true);
        _keyboardProvider = new LinuxKeyboardProvider(options);
        _mouseProvider = new LinuxMouseProvider(options);

        IReadOnlyList<InputDeviceDescriptor> keyboards = await _keyboardProvider.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<InputDeviceDescriptor> mice = await _mouseProvider.GetDevicesAsync(cancellationToken).ConfigureAwait(false);

        _keyboard = await TryOpenKeyboardAsync(keyboards, cancellationToken).ConfigureAwait(false);
        _mouse = await TryOpenMouseAsync(mice, cancellationToken).ConfigureAwait(false);

        if (_keyboard is null && _mouse is null)
        {
            _log("evdev input enabled, but no readable keyboard or mouse event devices were opened.");
            return;
        }

        if (_keyboard is not null)
            _keyboard.KeyChanged += OnKeyChanged;
        if (_mouse is not null)
        {
            _mouse.Moved += OnMouseMoved;
            _mouse.ButtonChanged += OnMouseButtonChanged;
            _mouse.WheelChanged += OnMouseWheelChanged;
        }

        _initialized = true;
        _log("evdev input opened. Events are started only while the X11 window is focused; Escape exits the demo loop.");
    }

    public async ValueTask SetActiveAsync(bool active, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_initialized || _active == active)
            return;

        if (active)
        {
            if (_keyboard is not null)
                await _keyboard.StartAsync(cancellationToken).ConfigureAwait(false);
            if (_mouse is not null)
                await _mouse.StartAsync(cancellationToken).ConfigureAwait(false);
            _log("evdev input resumed for focused X11 window.");
        }
        else
        {
            if (_keyboard is not null)
                await _keyboard.StopAsync(cancellationToken).ConfigureAwait(false);
            if (_mouse is not null)
                await _mouse.StopAsync(cancellationToken).ConfigureAwait(false);
            _log("evdev input paused because the X11 window is not focused.");
        }

        _active = active;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_active)
            await SetActiveAsync(false).ConfigureAwait(false);

        if (_keyboard is not null)
        {
            _keyboard.KeyChanged -= OnKeyChanged;
            await _keyboard.DisposeAsync().ConfigureAwait(false);
        }

        if (_mouse is not null)
        {
            _mouse.Moved -= OnMouseMoved;
            _mouse.ButtonChanged -= OnMouseButtonChanged;
            _mouse.WheelChanged -= OnMouseWheelChanged;
            await _mouse.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async ValueTask<KeyboardInputDevice?> TryOpenKeyboardAsync(
        IReadOnlyList<InputDeviceDescriptor> keyboards,
        CancellationToken cancellationToken)
    {
        if (_keyboardProvider is null)
            return null;

        foreach (InputDeviceDescriptor descriptor in keyboards.Where(static descriptor => descriptor.Availability == InputDeviceAvailability.Available))
        {
            try
            {
                KeyboardInputDevice device = await _keyboardProvider.OpenAsync(descriptor, new KeyboardOpenOptions(ReceiveText: false), cancellationToken).ConfigureAwait(false);
                _keyboardSummary = DescribeDescriptor(descriptor);
                _log("keyboard: " + _keyboardSummary);
                return device;
            }
            catch (Exception exception)
            {
                _log("keyboard open failed for " + descriptor.DisplayName + ": " + exception.Message);
            }
        }

        if (keyboards.Any(static descriptor => descriptor.Availability == InputDeviceAvailability.PermissionDenied))
            _log("keyboard: event devices were found but permission was denied.");

        return null;
    }

    private async ValueTask<MouseInputDevice?> TryOpenMouseAsync(
        IReadOnlyList<InputDeviceDescriptor> mice,
        CancellationToken cancellationToken)
    {
        if (_mouseProvider is null)
            return null;

        foreach (InputDeviceDescriptor descriptor in mice.Where(static descriptor => descriptor.Availability == InputDeviceAvailability.Available))
        {
            try
            {
                MouseInputDevice device = await _mouseProvider.OpenAsync(descriptor, new MouseOpenOptions(), cancellationToken).ConfigureAwait(false);
                _mouseSummary = DescribeDescriptor(descriptor);
                _log("mouse: " + _mouseSummary);
                return device;
            }
            catch (Exception exception)
            {
                _log("mouse open failed for " + descriptor.DisplayName + ": " + exception.Message);
            }
        }

        if (mice.Any(static descriptor => descriptor.Availability == InputDeviceAvailability.PermissionDenied))
            _log("mouse: event devices were found but permission was denied.");

        return null;
    }

    private void OnKeyChanged(KeyboardKeyEvent inputEvent)
    {
        if (inputEvent.Transition != KeyboardKeyTransition.Down)
            return;

        lock (_gate)
        {
            _keyEvents++;
            if (inputEvent.Key.Name.Equals("Escape", StringComparison.Ordinal))
                _quitRequested = true;
            else if (inputEvent.Key.Name.Equals("Space", StringComparison.Ordinal))
                _accentIndex++;
        }
    }

    private void OnMouseMoved(MouseMoveEvent inputEvent)
    {
        lock (_gate)
        {
            _mouseMoveEvents++;
            _pointerX += inputEvent.Position.X;
            _pointerY += inputEvent.Position.Y;
        }
    }

    private void OnMouseButtonChanged(MouseButtonEvent inputEvent)
    {
        if (inputEvent.Transition != MouseButtonTransition.Down)
            return;

        lock (_gate)
        {
            _mouseButtonEvents++;
            _accentIndex++;
        }
    }

    private void OnMouseWheelChanged(MouseWheelEvent inputEvent)
    {
        lock (_gate)
        {
            _mouseWheelEvents++;
            _accentIndex += inputEvent.DeltaNotches > 0 ? 1 : -1;
        }
    }

    private static string DescribeDescriptor(InputDeviceDescriptor descriptor)
    {
        string? eventName = descriptor.Capabilities
            .Where(static capability => capability.Name == "event-device")
            .Select(static capability => capability.Value)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(eventName)
            ? descriptor.DisplayName
            : $"{descriptor.DisplayName} ({eventName})";
    }
}

internal readonly record struct LinuxDemoInputSnapshot(
    bool Enabled,
    bool Initialized,
    bool Active,
    double PointerX,
    double PointerY,
    int AccentIndex,
    int KeyEvents,
    int MouseMoveEvents,
    int MouseButtonEvents,
    int MouseWheelEvents,
    string? KeyboardDevice,
    string? MouseDevice);
