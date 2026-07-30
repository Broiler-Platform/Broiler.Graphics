using System;
using System.Runtime.InteropServices;

namespace Broiler.Graphics.Android;

/// <summary>
/// The OpenGL ES entry points this backend uses, imported directly rather than loaded through
/// <c>eglGetProcAddress</c>.
/// </summary>
/// <remarks>
/// The set is deliberately small. Broiler rasterizes every frame on the CPU
/// (<see cref="BImageRenderer"/>) and the GPU's only job is to upload that frame as a texture and
/// blit it to the window — there is no shader pipeline, no vertex data, and no draw call. Adding
/// one later is a separate decision, not a prerequisite.
///
/// <c>glBlitFramebuffer</c> is the one call that fixes the feature floor: it is ES 3.0, not ES 2.0.
/// An ES 2 fallback would have to draw a textured quad instead, which needs the shader pipeline this
/// backend otherwise avoids.
/// </remarks>
internal static class AndroidGlesNative
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

    [DllImport(AndroidNativeLibraries.Gles, EntryPoint = "glGenTextures")]
    public static extern void GenTextures(int count, out uint textures);

    [DllImport(AndroidNativeLibraries.Gles, EntryPoint = "glDeleteTextures")]
    public static extern void DeleteTextures(int count, ref uint textures);

    [DllImport(AndroidNativeLibraries.Gles, EntryPoint = "glBindTexture")]
    public static extern void BindTexture(int target, uint texture);

    [DllImport(AndroidNativeLibraries.Gles, EntryPoint = "glTexParameteri")]
    public static extern void TexParameteri(int target, int name, int value);

    [DllImport(AndroidNativeLibraries.Gles, EntryPoint = "glTexImage2D")]
    public static extern void TexImage2D(
        int target,
        int level,
        int internalFormat,
        int width,
        int height,
        int border,
        int format,
        int type,
        IntPtr pixels);

    [DllImport(AndroidNativeLibraries.Gles, EntryPoint = "glGenFramebuffers")]
    public static extern void GenFramebuffers(int count, out uint framebuffers);

    [DllImport(AndroidNativeLibraries.Gles, EntryPoint = "glDeleteFramebuffers")]
    public static extern void DeleteFramebuffers(int count, ref uint framebuffers);

    [DllImport(AndroidNativeLibraries.Gles, EntryPoint = "glBindFramebuffer")]
    public static extern void BindFramebuffer(int target, uint framebuffer);

    [DllImport(AndroidNativeLibraries.Gles, EntryPoint = "glFramebufferTexture2D")]
    public static extern void FramebufferTexture2D(int target, int attachment, int textureTarget, uint texture, int level);

    [DllImport(AndroidNativeLibraries.Gles, EntryPoint = "glCheckFramebufferStatus")]
    public static extern uint CheckFramebufferStatus(int target);

    [DllImport(AndroidNativeLibraries.Gles, EntryPoint = "glViewport")]
    public static extern void Viewport(int x, int y, int width, int height);

    [DllImport(AndroidNativeLibraries.Gles, EntryPoint = "glClearColor")]
    public static extern void ClearColor(float red, float green, float blue, float alpha);

    [DllImport(AndroidNativeLibraries.Gles, EntryPoint = "glClear")]
    public static extern void Clear(int mask);

    [DllImport(AndroidNativeLibraries.Gles, EntryPoint = "glReadPixels")]
    public static extern void ReadPixels(int x, int y, int width, int height, int format, int type, IntPtr pixels);

    /// <summary>OpenGL ES 3.0 and later only.</summary>
    [DllImport(AndroidNativeLibraries.Gles, EntryPoint = "glBlitFramebuffer")]
    public static extern void BlitFramebuffer(
        int srcX0,
        int srcY0,
        int srcX1,
        int srcY1,
        int dstX0,
        int dstY0,
        int dstX1,
        int dstY1,
        int mask,
        int filter);

    [DllImport(AndroidNativeLibraries.Gles, EntryPoint = "glPixelStorei")]
    public static extern void PixelStorei(int name, int value);

    [DllImport(AndroidNativeLibraries.Gles, EntryPoint = "glEnable")]
    public static extern void Enable(int capability);

    [DllImport(AndroidNativeLibraries.Gles, EntryPoint = "glDisable")]
    public static extern void Disable(int capability);

    [DllImport(AndroidNativeLibraries.Gles, EntryPoint = "glScissor")]
    public static extern void Scissor(int x, int y, int width, int height);

    [DllImport(AndroidNativeLibraries.Gles, EntryPoint = "glFlush")]
    public static extern void Flush();

    [DllImport(AndroidNativeLibraries.Gles, EntryPoint = "glFinish")]
    public static extern void Finish();

    [DllImport(AndroidNativeLibraries.Gles, EntryPoint = "glGetError")]
    public static extern int GetError();

    [DllImport(AndroidNativeLibraries.Gles, EntryPoint = "glGetString")]
    public static extern IntPtr GetString(uint name);

    public static string GetStringValue(uint name)
    {
        IntPtr pointer = GetString(name);
        return pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(pointer) ?? string.Empty;
    }

    public static void ThrowIfError(string operation)
    {
        int error = GetError();
        if (error != GL_NO_ERROR)
            throw new AndroidOpenGlEsException($"{operation} failed with OpenGL ES error 0x{error:X}.");
    }

    public static AndroidOpenGlEsDriverInfo GetDriverInfo() =>
        new(
            GetStringValue(GL_VENDOR),
            GetStringValue(GL_RENDERER),
            GetStringValue(GL_VERSION),
            GetStringValue(GL_SHADING_LANGUAGE_VERSION));
}
