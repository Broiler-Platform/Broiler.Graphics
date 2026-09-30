using System;
using System.Collections.Generic;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.Windowing;

namespace Broiler.Graphics.Tests;

internal static class RenderOptionsTests
{
    public static void Register(List<(string Name, Action Body)> tests)
    {
        tests.Add(("BRenderOptions.Default has Antialias, VSync and SubpixelText enabled", () =>
        {
            var options = BRenderOptions.Default;
            AssertEx.IsTrue(options.Antialias, "Default Antialias should be true");
            AssertEx.IsTrue(options.VSync, "Default VSync should be true");
            AssertEx.IsTrue(options.SubpixelText, "Default SubpixelText should be true");
        }));

        tests.Add(("new BRenderOptions() parameterless constructor matches Default", () =>
        {
            var options = new BRenderOptions();
            AssertEx.IsTrue(options.Antialias, "Parameterless new() Antialias should be true");
            AssertEx.IsTrue(options.VSync, "Parameterless new() VSync should be true");
            AssertEx.IsTrue(options.SubpixelText, "Parameterless new() SubpixelText should be true");
            AssertEx.AreEqual(BRenderOptions.Default, options, "new() should equal Default");
        }));

        tests.Add(("default(BRenderOptions) produces all false zero-initialized struct", () =>
        {
            var options = default(BRenderOptions);
            AssertEx.IsFalse(options.Antialias, "default struct Antialias should be false");
            AssertEx.IsFalse(options.VSync, "default struct VSync should be false");
            AssertEx.IsFalse(options.SubpixelText, "default struct SubpixelText should be false");
        }));

        tests.Add(("BRenderOptions.LowQuality has Antialias, VSync and SubpixelText disabled", () =>
        {
            var options = BRenderOptions.LowQuality;
            AssertEx.IsFalse(options.Antialias, "LowQuality Antialias should be false");
            AssertEx.IsFalse(options.VSync, "LowQuality VSync should be false");
            AssertEx.IsFalse(options.SubpixelText, "LowQuality SubpixelText should be false");
            AssertEx.AreEqual(new BRenderOptions(false, false, false), options);
        }));

        tests.Add(("BWindowOptions.RenderOptions defaults to BRenderOptions.Default", () =>
        {
            var windowOptions = new BWindowOptions();
            AssertEx.AreEqual(BRenderOptions.Default, windowOptions.RenderOptions);
            AssertEx.IsTrue(windowOptions.RenderOptions.Antialias);
            AssertEx.IsTrue(windowOptions.RenderOptions.VSync);
            AssertEx.IsTrue(windowOptions.RenderOptions.SubpixelText);
        }));

        tests.Add(("BRenderOptions explicit positional flags are preserved", () =>
        {
            var custom = new BRenderOptions(Antialias: false, VSync: true, SubpixelText: false);
            AssertEx.IsFalse(custom.Antialias);
            AssertEx.IsTrue(custom.VSync);
            AssertEx.IsFalse(custom.SubpixelText);
        }));

        tests.Add(("BWindowOptions MinClientWidth and MinClientHeight support", () =>
        {
            var windowOptions = new BWindowOptions
            {
                MinClientWidth = 640,
                MinClientHeight = 480,
            };
            AssertEx.AreEqual(640, windowOptions.MinClientWidth);
            AssertEx.AreEqual(480, windowOptions.MinClientHeight);
        }));
    }
}
