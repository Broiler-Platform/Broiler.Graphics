using System;

namespace Broiler.Graphics.Linux.OpenGL;

public sealed class LinuxOpenGlException : InvalidOperationException
{
    public LinuxOpenGlException(string message)
        : base(message)
    {
    }

    public LinuxOpenGlException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
