using System;
using System.Collections.Generic;

namespace Broiler.Graphics.Android;

/// <summary>A native library the Android presentation backend needs, and the sonames it may use.</summary>
public sealed record AndroidNativeLibraryRequirement(
    string Id,
    string DisplayName,
    IReadOnlyList<string> CandidateNames);

/// <summary>The outcome of probing one <see cref="AndroidNativeLibraryRequirement"/>.</summary>
public sealed record AndroidNativeLibraryStatus(
    string Id,
    bool IsAvailable,
    string ResolvedName,
    string Diagnostic);

/// <summary>
/// Reports which of the backend's native dependencies are present, mirroring
/// <c>LinuxGraphicsDependencies</c>.
/// </summary>
/// <remarks>
/// The probe exists so a host can print an honest startup diagnostic instead of failing at the
/// first EGL call with an opaque error. On a non-Android host every entry is reported missing,
/// which is the correct answer rather than an error.
/// </remarks>
public static class AndroidGraphicsDependencies
{
    public static AndroidNativeLibraryRequirement Egl { get; } = new(
        "egl",
        "EGL",
        AndroidNativeLibraries.EglCandidates);

    public static AndroidNativeLibraryRequirement OpenGlEs { get; } = new(
        "opengl-es",
        "OpenGL ES 3",
        AndroidNativeLibraries.GlesCandidates);

    public static AndroidNativeLibraryRequirement NativeWindow { get; } = new(
        "android-native-window",
        "Android native window API",
        [AndroidNativeLibraries.AndroidRuntime]);

    public static IReadOnlyList<AndroidNativeLibraryRequirement> PresentationBaseline { get; } =
    [
        Egl,
        OpenGlEs,
        NativeWindow,
    ];

    public static IReadOnlyList<AndroidNativeLibraryStatus> CheckPresentationBaseline() =>
        Check(PresentationBaseline);

    public static IReadOnlyList<AndroidNativeLibraryStatus> Check(IReadOnlyList<AndroidNativeLibraryRequirement> requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        var statuses = new List<AndroidNativeLibraryStatus>(requirements.Count);
        foreach (AndroidNativeLibraryRequirement requirement in requirements)
        {
            if (AndroidNativeLibraries.TryLoadAny(requirement.CandidateNames, out string resolved))
            {
                statuses.Add(new AndroidNativeLibraryStatus(
                    requirement.Id,
                    true,
                    resolved,
                    $"{requirement.DisplayName} resolved to {resolved}."));
                continue;
            }

            statuses.Add(new AndroidNativeLibraryStatus(
                requirement.Id,
                false,
                string.Empty,
                $"{requirement.DisplayName} was not found; tried {string.Join(", ", requirement.CandidateNames)}."));
        }

        return statuses;
    }
}
