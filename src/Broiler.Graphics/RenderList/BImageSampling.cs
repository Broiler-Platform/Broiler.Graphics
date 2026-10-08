namespace Broiler.Graphics.RenderList;

/// <summary>How an image's pixels are mapped onto the device pixels it is drawn over.</summary>
public enum BImageSampling
{
    /// <summary>
    /// Each device pixel blends the image pixels around it, and the image's edges are antialiased:
    /// the smooth result scaling a picture wants.
    /// </summary>
    Linear,

    /// <summary>
    /// Each device pixel takes the image pixel under its centre, and the image covers the device
    /// pixels whose centres it covers, with no antialiased edge.
    /// </summary>
    /// <remarks>
    /// For tiles: images laid edge to edge then cover every device pixel exactly once, and an image
    /// drawn at a scale that is not a whole number keeps its pixels whole. Acid2 builds the yellow
    /// behind its eyes from two layers of a 2×2 checkerboard, half yellow and half transparent, the
    /// second offset a pixel from the first. At a 150% display scale a 2px tile is 3 device pixels
    /// wide, and blending made its middle pixel half transparent, so the red behind showed through
    /// as an orange dither.
    /// </remarks>
    NearestNeighbor,
}
