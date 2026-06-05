using System;
using System.Collections.Generic;

namespace Broiler.Graphics.Tests;

/// <summary>
/// Tests for resource disposal lifecycle. Uses in-test fakes implementing the Core interfaces so the
/// tests stay platform-neutral (no backend dependency).
/// </summary>
internal static class LifecycleTests
{
    internal static void Register(List<(string Name, Action Body)> tests)
    {
        tests.Add(("Disposing a surface marks it disposed", SurfaceDisposeFlag));
        tests.Add(("Surface throws after disposal", SurfaceThrowsAfterDispose));
        tests.Add(("Renderer disposes its surfaces", RendererDisposesSurfaces));
        tests.Add(("Double dispose is safe and idempotent", DoubleDisposeIsSafe));
        tests.Add(("Render rejects foreign surface", RenderRejectsForeignSurface));
    }

    private static void SurfaceDisposeFlag()
    {
        var surface = new FakeSurface(new BSize(800, 600), 1.0);
        AssertEx.IsFalse(surface.IsDisposed);
        surface.Dispose();
        AssertEx.IsTrue(surface.IsDisposed);
    }

    private static void SurfaceThrowsAfterDispose()
    {
        var surface = new FakeSurface(new BSize(800, 600), 1.0);
        surface.Dispose();
        AssertEx.Throws<ObjectDisposedException>(() => surface.Resize(new BSize(640, 480), 1.0));
    }

    private static void RendererDisposesSurfaces()
    {
        var renderer = new FakeRenderer();
        var surface = (FakeSurface)renderer.CreateSurface(BSurfaceDescriptor.Default(new BSize(100, 100)));

        AssertEx.IsFalse(surface.IsDisposed);
        renderer.Dispose();
        AssertEx.IsTrue(surface.IsDisposed, "Renderer should dispose surfaces it created.");
    }

    private static void DoubleDisposeIsSafe()
    {
        var renderer = new FakeRenderer();
        renderer.Dispose();
        renderer.Dispose(); // must not throw
        AssertEx.AreEqual(1, renderer.DisposeCount, "Dispose body should run only once.");
    }

    private static void RenderRejectsForeignSurface()
    {
        var renderer = new FakeRenderer();
        var foreign = new FakeSurface(new BSize(10, 10), 1.0);
        var list = new BRenderList();
        list.FillRect(new BRect(0, 0, 1, 1), BColor.Black);

        AssertEx.Throws<ArgumentException>(
            () => renderer.Render(foreign, list, BFrameContext.Default));
    }

    // --- In-test fakes -----------------------------------------------------------------------------

    private sealed class FakeSurface : IBroilerSurface
    {
        public FakeSurface(BSize size, double dpiScale)
        {
            Size = size;
            DpiScale = dpiScale;
        }

        public BSize Size { get; private set; }
        public double DpiScale { get; private set; }
        public bool IsDisposed { get; private set; }
        public bool OwnedByRenderer { get; init; }

        public void Resize(BSize size, double dpiScale)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            Size = size;
            DpiScale = dpiScale;
        }

        public void Dispose() => IsDisposed = true;
    }

    private sealed class FakeRenderer : IBroilerRenderer
    {
        private readonly List<FakeSurface> _surfaces = new();
        private bool _disposed;

        public int DisposeCount { get; private set; }

        public IBroilerSurface CreateSurface(BSurfaceDescriptor descriptor)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var surface = new FakeSurface(descriptor.Size, descriptor.DpiScale) { OwnedByRenderer = true };
            _surfaces.Add(surface);
            return surface;
        }

        public void Render(IBroilerSurface surface, BRenderList renderList, BFrameContext frameContext)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(surface);
            ArgumentNullException.ThrowIfNull(renderList);

            if (surface is not FakeSurface fake || !fake.OwnedByRenderer || !_surfaces.Contains(fake))
                throw new ArgumentException("Surface was not created by this renderer.", nameof(surface));

            renderList.Validate();
            // A real backend would replay here; the fake just validates ownership and the list.
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            DisposeCount++;
            foreach (FakeSurface surface in _surfaces)
                surface.Dispose();
            _surfaces.Clear();
        }
    }
}
