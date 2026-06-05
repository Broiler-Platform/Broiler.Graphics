using System;
using Broiler.Graphics.Windows.Native;

namespace Broiler.Graphics.Windows;

/// <summary>
/// The Windows / Direct2D implementation of <see cref="IBroilerRenderer"/>. Owns a
/// <see cref="Direct2DDevice"/> and replays <see cref="BRenderList"/> instances onto
/// <see cref="Direct2DSurface"/> targets.
/// </summary>
/// <remarks>
/// The drawing translation (Core command → Direct2D vtable call) is laid out here with one method per
/// command. The methods currently throw <see cref="NotImplementedException"/> but show exactly where
/// each DirectX call belongs, so the backend can be completed incrementally without restructuring.
/// </remarks>
public sealed class Direct2DRenderer : IBroilerRenderer
{
    private readonly Direct2DDevice _device;
    private bool _disposed;

    public Direct2DRenderer()
    {
        _device = new Direct2DDevice();
        _device.Initialize();
    }

    public IBroilerSurface CreateSurface(BSurfaceDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new Direct2DSurface(_device, descriptor);
    }

    public void Render(IBroilerSurface surface, BRenderList renderList, BFrameContext frameContext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(renderList);

        if (surface is not Direct2DSurface d2dSurface)
            throw new ArgumentException("Surface was not created by this renderer.", nameof(surface));

        // Guarantee the command stream is well-formed before touching the GPU.
        renderList.Validate();

        BeginDraw(d2dSurface, frameContext);
        try
        {
            foreach (BRenderCommand command in renderList.Commands)
                Dispatch(d2dSurface, command);
        }
        finally
        {
            EndDraw(d2dSurface, frameContext);
        }
    }

    private static void BeginDraw(Direct2DSurface surface, BFrameContext frame)
    {
        // TODO: ID2D1DeviceContext::BeginDraw, then Clear(frame.ClearColor as D2D1_COLOR_F).
        _ = surface;
        _ = frame;
    }

    private static void EndDraw(Direct2DSurface surface, BFrameContext frame)
    {
        // TODO: ID2D1DeviceContext::EndDraw (inspect HRESULT for device-lost), then surface.Present.
        surface.Present(frame.Options.VSync);
    }

    /// <summary>Routes a single command to its Direct2D translation. The switch is exhaustive.</summary>
    private void Dispatch(Direct2DSurface surface, BRenderCommand command)
    {
        switch (command)
        {
            case BRenderCommand.FillRect c:
                FillRect(surface, c);
                break;
            case BRenderCommand.StrokeRect c:
                StrokeRect(surface, c);
                break;
            case BRenderCommand.DrawText c:
                DrawText(surface, c);
                break;
            case BRenderCommand.DrawImage c:
                DrawImage(surface, c);
                break;
            case BRenderCommand.PushClip c:
                PushClip(surface, c);
                break;
            case BRenderCommand.PopClip:
                PopClip(surface);
                break;
            case BRenderCommand.PushTransform c:
                PushTransform(surface, c);
                break;
            case BRenderCommand.PopTransform:
                PopTransform(surface);
                break;
            default:
                throw new NotSupportedException($"Unknown render command: {command.GetType().Name}");
        }
    }

    // --- Per-command translation. Each shows where the Direct2D/DirectWrite call lives. ---

    private void FillRect(Direct2DSurface surface, BRenderCommand.FillRect c)
    {
        // TODO: create/cache an ID2D1SolidColorBrush for c.Color, then
        //       ID2D1DeviceContext::FillRectangle(ToRectF(c.Rect), brush).
        _ = surface;
        throw new NotImplementedException("Direct2D FillRectangle not yet wired up.");
    }

    private void StrokeRect(Direct2DSurface surface, BRenderCommand.StrokeRect c)
    {
        // TODO: ID2D1DeviceContext::DrawRectangle(ToRectF(c.Rect), brush, (float)c.Thickness).
        _ = surface;
        throw new NotImplementedException("Direct2D DrawRectangle not yet wired up.");
    }

    private void DrawText(Direct2DSurface surface, BRenderCommand.DrawText c)
    {
        // TODO: build an IDWriteTextFormat from c.Text.Font (DWriteNative.ToDWrite helpers),
        //       then ID2D1DeviceContext::DrawText with c.Text.Text and a brush for c.Text.Color.
        _ = surface;
        throw new NotImplementedException("DirectWrite DrawText not yet wired up.");
    }

    private void DrawImage(Direct2DSurface surface, BRenderCommand.DrawImage c)
    {
        // TODO: resolve c.Image.Handle to an ID2D1Bitmap, then
        //       ID2D1DeviceContext::DrawBitmap(bitmap, dest, (float)c.Opacity, interpMode, source).
        _ = surface;
        throw new NotImplementedException("Direct2D DrawBitmap not yet wired up.");
    }

    private void PushClip(Direct2DSurface surface, BRenderCommand.PushClip c)
    {
        // TODO: ID2D1DeviceContext::PushAxisAlignedClip(ToRectF(c.Rect), antialiasMode).
        _ = surface;
        throw new NotImplementedException("Direct2D PushAxisAlignedClip not yet wired up.");
    }

    private void PopClip(Direct2DSurface surface)
    {
        // TODO: ID2D1DeviceContext::PopAxisAlignedClip().
        _ = surface;
        throw new NotImplementedException("Direct2D PopAxisAlignedClip not yet wired up.");
    }

    private void PushTransform(Direct2DSurface surface, BRenderCommand.PushTransform c)
    {
        // TODO: maintain a managed transform stack, multiply, and ID2D1DeviceContext::SetTransform.
        _ = surface;
        throw new NotImplementedException("Direct2D SetTransform (push) not yet wired up.");
    }

    private void PopTransform(Direct2DSurface surface)
    {
        // TODO: pop the managed transform stack and ID2D1DeviceContext::SetTransform to the previous.
        _ = surface;
        throw new NotImplementedException("Direct2D SetTransform (pop) not yet wired up.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _device.Dispose();
    }
}
