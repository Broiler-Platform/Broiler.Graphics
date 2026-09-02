using System;

namespace Broiler.Graphics;

/// <summary>
/// What a font's own <c>OS/2</c> <c>fsType</c> field says about embedding it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is a technical signal, not a licence.</strong> It is what the
/// font's designer encoded in the file, and it is an input to a decision rather
/// than the decision: a permissive <c>fsType</c> does not grant what a font's
/// EULA withholds, and neither this type nor anything reading it establishes a
/// caller's legal title to embed anything. A host still records a licence
/// disposition of its own; this only tells it what the file claims.
/// </para>
/// <para>
/// The values are ordered by how much they permit, so a caller can compare them,
/// but the ordering is a convenience and not a licence hierarchy.
/// </para>
/// </remarks>
public enum BFontEmbeddingPermission
{
    /// <summary>
    /// The font declares nothing this reader understands, or carries no
    /// <c>OS/2</c> table to declare it in. Distinct from
    /// <see cref="Restricted"/>: the font is not refusing, it is silent, and a
    /// caller that fails closed treats silence as a refusal anyway.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Restricted licence (bit 1). The font may not be embedded or exchanged
    /// without the vendor's permission.
    /// </summary>
    Restricted,

    /// <summary>
    /// Preview and print only (bit 2). A document embedding it may be viewed and
    /// printed but not edited with the font.
    /// </summary>
    PreviewAndPrint,

    /// <summary>Editable embedding (bit 3): a document may be edited with it.</summary>
    Editable,

    /// <summary>The font sets no restriction bit, which the format calls installable.</summary>
    Installable,
}

/// <summary>
/// A font's declared embedding permissions, as read from <c>OS/2</c>.
/// </summary>
public readonly record struct BFontEmbeddingRights(
    BFontEmbeddingPermission Permission,
    bool NoSubsetting,
    bool BitmapEmbeddingOnly,
    ushort RawFsType)
{
    /// <summary>What a font with no readable <c>OS/2</c> table reports.</summary>
    public static BFontEmbeddingRights Unknown { get; } =
        new(BFontEmbeddingPermission.Unknown, NoSubsetting: false, BitmapEmbeddingOnly: false, RawFsType: 0);

    /// <summary>
    /// Reads the permission bits out of a raw <c>fsType</c> value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The restriction bits are mutually exclusive by the specification and are
    /// not always so in the wild. When more than one is set this reports the
    /// <em>most</em> restrictive, because a file that contradicts itself about
    /// what it permits is not a file to give the benefit of the doubt to.
    /// </para>
    /// <para>
    /// Bit 0 is reserved and ignored, which is what makes a zero value
    /// installable rather than unknown: the font had an <c>OS/2</c> table and set
    /// no restriction in it.
    /// </para>
    /// </remarks>
    public static BFontEmbeddingRights FromFsType(ushort fsType)
    {
        BFontEmbeddingPermission permission =
            (fsType & 0x0002) != 0 ? BFontEmbeddingPermission.Restricted
            : (fsType & 0x0004) != 0 ? BFontEmbeddingPermission.PreviewAndPrint
            : (fsType & 0x0008) != 0 ? BFontEmbeddingPermission.Editable
            : BFontEmbeddingPermission.Installable;

        return new BFontEmbeddingRights(
            permission,
            NoSubsetting: (fsType & 0x0100) != 0,
            BitmapEmbeddingOnly: (fsType & 0x0200) != 0,
            RawFsType: fsType);
    }

    /// <summary>
    /// A short description for a diagnostic: what the font declared, in words,
    /// without asserting what a caller may therefore do.
    /// </summary>
    public string Describe()
    {
        string permission = Permission switch
        {
            BFontEmbeddingPermission.Restricted => "restricted licence embedding",
            BFontEmbeddingPermission.PreviewAndPrint => "preview-and-print embedding",
            BFontEmbeddingPermission.Editable => "editable embedding",
            BFontEmbeddingPermission.Installable => "installable embedding",
            _ => "no readable embedding declaration",
        };

        string subsetting = NoSubsetting ? ", no subsetting" : string.Empty;
        string bitmaps = BitmapEmbeddingOnly ? ", bitmap embedding only" : string.Empty;
        return permission + subsetting + bitmaps;
    }
}
