using System;
using System.Collections.Generic;
using Broiler.Media;
using Broiler.Media.Video;
using Broiler.Media.Video.Windows;

namespace Broiler.Graphics.Windows.Tests;

/// <summary>
/// Coverage for the HWND-backed video presentation target this assembly owns.
/// </summary>
/// <remarks>
/// These assertions used to live in Broiler.Media's Media Foundation suite, which could reach
/// the concrete type because Media referenced this assembly. That reference closed a
/// component-level dependency cycle and was removed (Broiler.Media ADR 0006); the type is
/// declared here, so its tests belong here too. Broiler.Media now tests only its side of the
/// <see cref="IHwndVideoOutput"/> contract, against its own double.
/// </remarks>
internal static class HwndVideoOutputTests
{
    public static void Register(ICollection<(string Name, Action Body)> tests)
    {
        tests.Add(("HwndVideoOutput rejects a zero window handle", RejectsZeroHandle));
        tests.Add(("HwndVideoOutput reports resize, visibility and destruction to its borrower", ReportsLifecycle));
        tests.Add(("HwndVideoOutput refuses use after its owner destroys the window", RefusesUseAfterDestroy));
        tests.Add(("HwndVideoOutput records media output completion and failure", RecordsOutputState));
        tests.Add(("HwndVideoOutput satisfies the borrowed-target contract", SatisfiesContract));
    }

    private static void RejectsZeroHandle() =>
        Assert.Throws<ArgumentException>(() => _ = new HwndVideoOutput(0, "zero", 1, 1, validateNativeWindow: false));

    private static void ReportsLifecycle()
    {
        HwndVideoOutput target = CreateTarget();
        var changes = new List<HwndVideoTargetChangeKind>();
        target.TargetChanged += (_, e) => changes.Add(e.Kind);

        target.Resize(800, 450);
        target.SetVisible(false);
        target.NotifyDestroyed();

        Assert.AreEqual(800, target.Width);
        Assert.AreEqual(450, target.Height);
        Assert.True(!target.IsVisible, "Expected the target to report itself hidden.");
        Assert.True(target.IsDestroyed, "Expected the target to report itself destroyed.");

        HwndVideoTargetChangeKind[] expected =
        [
            HwndVideoTargetChangeKind.Resized,
            HwndVideoTargetChangeKind.VisibilityChanged,
            HwndVideoTargetChangeKind.Destroyed,
        ];

        Assert.AreEqual(expected.Length, changes.Count, "Unexpected number of target-change notifications.");
        for (int i = 0; i < expected.Length; i++)
            Assert.AreEqual(expected[i], changes[i], $"Target-change notification {i} differs.");
    }

    private static void RefusesUseAfterDestroy()
    {
        HwndVideoOutput target = CreateTarget();
        target.NotifyDestroyed();

        Assert.Throws<ObjectDisposedException>(() => target.Resize(1, 1));
        Assert.Throws<ObjectDisposedException>(target.ThrowIfUsableTargetRequired);
    }

    private static void RecordsOutputState()
    {
        HwndVideoOutput completed = CreateTarget();
        completed.CompleteAsync().AsTask().GetAwaiter().GetResult();
        Assert.True(completed.Completed, "Expected the target to record completion.");

        HwndVideoOutput failed = CreateTarget();
        var error = new MediaError(MediaErrorCode.OutputFailed, "target failed");
        failed.FailAsync(error).AsTask().GetAwaiter().GetResult();
        Assert.AreEqual(error, failed.Failure);
    }

    private static void SatisfiesContract()
    {
        // The borrowed-HWND split of ADR 0005 only holds if a borrower reaches this type
        // through the contract; handing one the concrete class is what rebuilt the cycle
        // before. Assert the target is usable purely as IHwndVideoOutput.
        IHwndVideoOutput target = CreateTarget();

        Assert.AreEqual((nint)1234, target.Hwnd);
        Assert.AreEqual(640, target.Width);
        Assert.AreEqual(360, target.Height);
        Assert.True(target.IsVisible, "Expected a freshly created target to be visible.");
        Assert.True(!target.IsDestroyed, "Expected a freshly created target to be live.");
        Assert.AreEqual("test hwnd", target.DisplayName);
        Assert.True(target is IVideoOutput, "The borrowed target must remain a platform-neutral video output.");

        target.ThrowIfUsableTargetRequired();
    }

    private static HwndVideoOutput CreateTarget() =>
        new((nint)1234, "test hwnd", 640, 360, validateNativeWindow: false);
}
