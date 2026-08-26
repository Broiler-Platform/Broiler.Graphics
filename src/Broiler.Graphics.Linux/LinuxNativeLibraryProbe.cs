using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Broiler.Graphics.Linux;

public sealed record LinuxNativeLibraryRequirement(
    string Id,
    string DisplayName,
    IReadOnlyList<string> LibraryNames,
    bool Required = true);

public sealed record LinuxNativeLibraryStatus(
    string Id,
    string DisplayName,
    IReadOnlyList<string> LibraryNames,
    bool IsAvailable,
    string? LoadedLibraryName,
    string Diagnostic);

public static class LinuxNativeLibraryProbe
{
    public static IReadOnlyList<LinuxNativeLibraryStatus> Check(
        IReadOnlyList<LinuxNativeLibraryRequirement> requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        var statuses = new List<LinuxNativeLibraryStatus>(requirements.Count);
        foreach (LinuxNativeLibraryRequirement requirement in requirements)
            statuses.Add(Check(requirement));

        return statuses;
    }

    public static LinuxNativeLibraryStatus Check(LinuxNativeLibraryRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        if (requirement.LibraryNames.Count == 0)
        {
            return new LinuxNativeLibraryStatus(
                requirement.Id,
                requirement.DisplayName,
                requirement.LibraryNames,
                IsAvailable: false,
                LoadedLibraryName: null,
                Diagnostic: "No native library names were supplied for this requirement.");
        }

        foreach (string libraryName in requirement.LibraryNames)
        {
            if (string.IsNullOrWhiteSpace(libraryName))
                continue;

            if (NativeLibrary.TryLoad(libraryName, out IntPtr handle))
            {
                NativeLibrary.Free(handle);
                return new LinuxNativeLibraryStatus(
                    requirement.Id,
                    requirement.DisplayName,
                    requirement.LibraryNames,
                    IsAvailable: true,
                    LoadedLibraryName: libraryName,
                    Diagnostic: $"{requirement.DisplayName} is available via {libraryName}.");
            }
        }

        string tried = string.Join(", ", requirement.LibraryNames);
        string required = requirement.Required ? "required" : "optional";
        return new LinuxNativeLibraryStatus(
            requirement.Id,
            requirement.DisplayName,
            requirement.LibraryNames,
            IsAvailable: false,
            LoadedLibraryName: null,
            Diagnostic: $"{requirement.DisplayName} is {required} but no candidate native library could be loaded. Tried: {tried}.");
    }
}
