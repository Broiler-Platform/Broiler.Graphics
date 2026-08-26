using System;

namespace Broiler.Graphics;

/// <summary>
/// Thrown when the underlying GPU device is lost or reset (e.g. DXGI <c>DXGI_ERROR_DEVICE_REMOVED</c>).
/// Callers should recreate the renderer/surfaces and retry. Platform-neutral: carries an optional
/// backend-specific HRESULT-like code without exposing any native types.
/// </summary>
public sealed class BDeviceLostException : Exception
{
    /// <summary>Optional backend error code (e.g. an HRESULT). Zero when not applicable.</summary>
    public int BackendCode { get; }

    public BDeviceLostException()
        : base("The graphics device was lost.")
    {
    }

    public BDeviceLostException(string message)
        : base(message)
    {
    }

    public BDeviceLostException(string message, int backendCode)
        : base(message)
    {
        BackendCode = backendCode;
    }

    public BDeviceLostException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
