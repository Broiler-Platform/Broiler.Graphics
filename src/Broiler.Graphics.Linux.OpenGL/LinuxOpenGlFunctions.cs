using System;
using System.Runtime.InteropServices;

namespace Broiler.Graphics.Linux.OpenGL;

internal sealed class LinuxOpenGlFunctions
{
    public const int GL_NO_ERROR = 0;
    public const int GL_TEXTURE_2D = 0x0DE1;
    public const int GL_RGBA = 0x1908;
    public const int GL_RGBA8 = 0x8058;
    public const int GL_UNSIGNED_BYTE = 0x1401;
    public const int GL_TEXTURE_MIN_FILTER = 0x2801;
    public const int GL_TEXTURE_MAG_FILTER = 0x2800;
    public const int GL_TEXTURE_WRAP_S = 0x2802;
    public const int GL_TEXTURE_WRAP_T = 0x2803;
    public const int GL_LINEAR = 0x2601;
    public const int GL_NEAREST = 0x2600;
    public const int GL_CLAMP_TO_EDGE = 0x812F;
    public const int GL_FRAMEBUFFER = 0x8D40;
    public const int GL_READ_FRAMEBUFFER = 0x8CA8;
    public const int GL_DRAW_FRAMEBUFFER = 0x8CA9;
    public const int GL_COLOR_ATTACHMENT0 = 0x8CE0;
    public const int GL_FRAMEBUFFER_COMPLETE = 0x8CD5;
    public const int GL_COLOR_BUFFER_BIT = 0x4000;
    public const int GL_PACK_ALIGNMENT = 0x0D05;
    public const int GL_UNPACK_ALIGNMENT = 0x0CF5;
    public const int GL_SCISSOR_TEST = 0x0C11;
    public const uint GL_VENDOR = 0x1F00;
    public const uint GL_RENDERER = 0x1F01;
    public const uint GL_VERSION = 0x1F02;
    public const uint GL_SHADING_LANGUAGE_VERSION = 0x8B8C;

    private readonly GlGenTexturesProc _genTextures;
    private readonly GlDeleteTexturesProc _deleteTextures;
    private readonly GlBindTextureProc _bindTexture;
    private readonly GlTexParameteriProc _texParameteri;
    private readonly GlTexImage2DProc _texImage2D;
    private readonly GlGenFramebuffersProc _genFramebuffers;
    private readonly GlDeleteFramebuffersProc _deleteFramebuffers;
    private readonly GlBindFramebufferProc _bindFramebuffer;
    private readonly GlFramebufferTexture2DProc _framebufferTexture2D;
    private readonly GlCheckFramebufferStatusProc _checkFramebufferStatus;
    private readonly GlViewportProc _viewport;
    private readonly GlClearColorProc _clearColor;
    private readonly GlClearProc _clear;
    private readonly GlReadPixelsProc _readPixels;
    private readonly GlBlitFramebufferProc _blitFramebuffer;
    private readonly GlPixelStoreiProc _pixelStorei;
    private readonly GlEnableProc _enable;
    private readonly GlDisableProc _disable;
    private readonly GlScissorProc _scissor;
    private readonly GlFlushProc _flush;
    private readonly GlGetErrorProc _getError;
    private readonly GlGetStringProc _getString;

    private LinuxOpenGlFunctions()
    {
        _genTextures = Load<GlGenTexturesProc>("glGenTextures");
        _deleteTextures = Load<GlDeleteTexturesProc>("glDeleteTextures");
        _bindTexture = Load<GlBindTextureProc>("glBindTexture");
        _texParameteri = Load<GlTexParameteriProc>("glTexParameteri");
        _texImage2D = Load<GlTexImage2DProc>("glTexImage2D");
        _genFramebuffers = Load<GlGenFramebuffersProc>("glGenFramebuffers");
        _deleteFramebuffers = Load<GlDeleteFramebuffersProc>("glDeleteFramebuffers");
        _bindFramebuffer = Load<GlBindFramebufferProc>("glBindFramebuffer");
        _framebufferTexture2D = Load<GlFramebufferTexture2DProc>("glFramebufferTexture2D");
        _checkFramebufferStatus = Load<GlCheckFramebufferStatusProc>("glCheckFramebufferStatus");
        _viewport = Load<GlViewportProc>("glViewport");
        _clearColor = Load<GlClearColorProc>("glClearColor");
        _clear = Load<GlClearProc>("glClear");
        _readPixels = Load<GlReadPixelsProc>("glReadPixels");
        _blitFramebuffer = Load<GlBlitFramebufferProc>("glBlitFramebuffer");
        _pixelStorei = Load<GlPixelStoreiProc>("glPixelStorei");
        _enable = Load<GlEnableProc>("glEnable");
        _disable = Load<GlDisableProc>("glDisable");
        _scissor = Load<GlScissorProc>("glScissor");
        _flush = Load<GlFlushProc>("glFlush");
        _getError = Load<GlGetErrorProc>("glGetError");
        _getString = Load<GlGetStringProc>("glGetString");
    }

    public static LinuxOpenGlFunctions LoadCurrentContext() => new();

    public void GenTextures(int n, out uint texture) => _genTextures(n, out texture);

    public void DeleteTextures(int n, ref uint texture) => _deleteTextures(n, ref texture);

    public void BindTexture(int target, uint texture) => _bindTexture(target, texture);

    public void TexParameteri(int target, int pname, int param) => _texParameteri(target, pname, param);

    public void TexImage2D(int target, int level, int internalFormat, int width, int height, int border, int format, int type, IntPtr pixels) =>
        _texImage2D(target, level, internalFormat, width, height, border, format, type, pixels);

    public void GenFramebuffers(int n, out uint framebuffer) => _genFramebuffers(n, out framebuffer);

    public void DeleteFramebuffers(int n, ref uint framebuffer) => _deleteFramebuffers(n, ref framebuffer);

    public void BindFramebuffer(int target, uint framebuffer) => _bindFramebuffer(target, framebuffer);

    public void FramebufferTexture2D(int target, int attachment, int textureTarget, uint texture, int level) =>
        _framebufferTexture2D(target, attachment, textureTarget, texture, level);

    public uint CheckFramebufferStatus(int target) => _checkFramebufferStatus(target);

    public void Viewport(int x, int y, int width, int height) => _viewport(x, y, width, height);

    public void ClearColor(float red, float green, float blue, float alpha) => _clearColor(red, green, blue, alpha);

    public void Clear(int mask) => _clear(mask);

    public void ReadPixels(int x, int y, int width, int height, int format, int type, IntPtr pixels) =>
        _readPixels(x, y, width, height, format, type, pixels);

    public void BlitFramebuffer(
        int srcX0,
        int srcY0,
        int srcX1,
        int srcY1,
        int dstX0,
        int dstY0,
        int dstX1,
        int dstY1,
        int mask,
        int filter) =>
        _blitFramebuffer(srcX0, srcY0, srcX1, srcY1, dstX0, dstY0, dstX1, dstY1, mask, filter);

    public void PixelStorei(int pname, int param) => _pixelStorei(pname, param);

    public void Enable(int cap) => _enable(cap);

    public void Disable(int cap) => _disable(cap);

    public void Scissor(int x, int y, int width, int height) => _scissor(x, y, width, height);

    public void Flush() => _flush();

    public string GetString(uint name)
    {
        IntPtr value = _getString(name);
        return value == IntPtr.Zero
            ? "unavailable"
            : Marshal.PtrToStringAnsi(value) ?? "unavailable";
    }

    public LinuxOpenGlDriverInfo GetDriverInfo() =>
        new(
            GetString(GL_VENDOR),
            GetString(GL_RENDERER),
            GetString(GL_VERSION),
            GetString(GL_SHADING_LANGUAGE_VERSION));

    public void ThrowIfError(string operation)
    {
        uint error = _getError();
        if (error != GL_NO_ERROR)
            throw new LinuxOpenGlException($"{operation} failed with OpenGL error 0x{error:X}.");
    }

    private static TDelegate Load<TDelegate>(string name)
        where TDelegate : Delegate
    {
        IntPtr address = LinuxEglNative.GetProcAddress(name);
        if (address == IntPtr.Zero)
            throw new LinuxOpenGlException($"OpenGL function {name} is not available from eglGetProcAddress.");

        return Marshal.GetDelegateForFunctionPointer<TDelegate>(address);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlGenTexturesProc(int n, out uint textures);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlDeleteTexturesProc(int n, ref uint textures);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlBindTextureProc(int target, uint texture);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlTexParameteriProc(int target, int pname, int param);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlTexImage2DProc(int target, int level, int internalFormat, int width, int height, int border, int format, int type, IntPtr pixels);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlGenFramebuffersProc(int n, out uint framebuffers);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlDeleteFramebuffersProc(int n, ref uint framebuffers);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlBindFramebufferProc(int target, uint framebuffer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlFramebufferTexture2DProc(int target, int attachment, int textureTarget, uint texture, int level);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint GlCheckFramebufferStatusProc(int target);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlViewportProc(int x, int y, int width, int height);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlClearColorProc(float red, float green, float blue, float alpha);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlClearProc(int mask);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlReadPixelsProc(int x, int y, int width, int height, int format, int type, IntPtr pixels);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlBlitFramebufferProc(int srcX0, int srcY0, int srcX1, int srcY1, int dstX0, int dstY0, int dstX1, int dstY1, int mask, int filter);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlPixelStoreiProc(int pname, int param);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlEnableProc(int cap);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlDisableProc(int cap);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlScissorProc(int x, int y, int width, int height);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlFlushProc();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint GlGetErrorProc();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GlGetStringProc(uint name);
}
