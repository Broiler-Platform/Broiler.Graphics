namespace Broiler.Graphics;

/// <summary>
/// PROTOTYPE bridge (vertical writing-mode flow, Stage 2): carries the
/// clockwise glyph-rotation requested for the current text draw from the
/// paint walker to the text shaper without widening the abstract
/// <see cref="RGraphics.DrawString"/> signature (and every backend override).
///
/// The paint backend sets <see cref="RotationDeg"/> immediately before a
/// <c>DrawString</c> call and resets it to 0 afterwards; the shaper reads it
/// while rasterising the run.  Thread-static so concurrent renders don't
/// interfere.  Only ever non-zero when the experimental flag is enabled.
/// </summary>
public static class VerticalGlyphContext
{
    [System.ThreadStatic]
    private static float _rotationDeg;

    public static float RotationDeg
    {
        get => _rotationDeg;
        set => _rotationDeg = value;
    }
}
