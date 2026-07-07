#nullable disable
using System;
using System.Collections.Generic;

namespace Broiler.Graphics;

/// <summary>
/// A shaped glyph: a resolved glyph index, its advance, and a positioning
/// offset (all in font design units).  The offset carries GPOS mark
/// attachment; it is added to the pen position when drawing without affecting
/// the advance.
/// </summary>
public readonly struct ShapedGlyph(int glyph, int advance, int xOffset, int yOffset)
{
    public int Glyph { get; } = glyph;
    public int Advance { get; } = advance;
    public int XOffset { get; } = xOffset;
    public int YOffset { get; } = yOffset;
}

/// <summary>
/// Minimal complex-text shaper for cursive scripts (Arabic/Persian) and a
/// pragmatic subset of the Unicode Bidirectional Algorithm.  It performs:
/// <list type="number">
///   <item>Arabic joining-form selection (isolated/initial/medial/final) per
///         UAX #24 joining types;</item>
///   <item>GSUB substitution of the contextual form (init/medi/fina) and of
///         required/standard ligatures (rlig/calt/liga) via
///         <see cref="TrueTypeFont"/>;</item>
///   <item>bidi level assignment (first-strong base direction) and reordering
///         of the glyph buffer into visual (left-to-right) order.</item>
/// </list>
/// This is not a full UBA/HarfBuzz implementation (no GPOS mark positioning,
/// no contextual GSUB beyond ligatures), but renders common Arabic/Persian and
/// mixed bidi runs correctly enough to match reference rendering closely.
/// </summary>
public static class ComplexTextShaper
{
    private enum JoiningType { NonJoining, Right, Left, Dual, Causing, Transparent }
    private enum ArabicForm { None, Isolated, Initial, Medial, Final }

    /// <summary>
    /// True when <paramref name="text"/> contains characters that need complex
    /// shaping or bidi reordering (right-to-left or cursive scripts).  LTR-only
    /// text takes the simpler per-codepoint path in the caller.
    /// </summary>
    public static bool RequiresShaping(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        for (int i = 0; i < text.Length; i++)
        {
            if (IsRightToLeft(text[i]) || IsArabicJoiner(text[i]))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when shaping is needed: complex/RTL text, or any author-requested
    /// OpenType features (font-feature-settings) to apply via GSUB.
    /// </summary>
    public static bool RequiresShaping(string text, string fontFeatures) =>
        !string.IsNullOrEmpty(fontFeatures) || RequiresShaping(text);

    /// <summary>
    /// Shapes <paramref name="text"/> with <paramref name="font"/> into a
    /// visually-ordered glyph run.  Advances are in font design units.
    /// <paramref name="fontFeatures"/> is a space-separated list of GSUB feature
    /// tags (from font-feature-settings) to apply as single substitutions.
    /// </summary>
    public static List<ShapedGlyph> Shape(TrueTypeFont font, string text, string fontFeatures = null)
    {
        var codepoints = ToCodepoints(text);
        int n = codepoints.Count;
        var result = new List<ShapedGlyph>(n);
        if (n == 0)
            return result;

        string[] featureTags = string.IsNullOrEmpty(fontFeatures)
            ? null
            : fontFeatures.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        bool baseRtl = FirstStrongIsRtl(codepoints);

        // 1. Joining types and contextual forms.
        var joining = new JoiningType[n];
        for (int i = 0; i < n; i++)
            joining[i] = GetJoiningType(codepoints[i]);
        var forms = ComputeArabicForms(joining);

        // 2. Map to glyphs, apply the per-glyph form feature, and assign a bidi
        //    level to each glyph (carried through ligature merging/reordering).
        var glyphs = new List<int>(n);
        var levels = new List<int>(n);
        for (int i = 0; i < n; i++)
        {
            int glyph = font.GetGlyphIndex(codepoints[i]);
            glyph = forms[i] switch
            {
                ArabicForm.Initial => font.ApplySingleSubstitution("init", glyph),
                ArabicForm.Medial => font.ApplySingleSubstitution("medi", glyph),
                ArabicForm.Final => font.ApplySingleSubstitution("fina", glyph),
                ArabicForm.Isolated => font.ApplySingleSubstitution("isol", glyph),
                _ => glyph,
            };

            // Author-requested features (e.g. stylistic sets "ss05") applied as
            // single substitutions on top of any contextual form.
            if (featureTags != null)
                foreach (var tag in featureTags)
                    glyph = font.ApplySingleSubstitution(tag, glyph);

            glyphs.Add(glyph);
            levels.Add(ResolveLevel(codepoints[i], baseRtl));
        }

        // 3. Ligatures over the (form-substituted) logical glyph buffer.
        ApplyLigatures(font, glyphs, levels);
        int m = glyphs.Count;

        // 4. Advances and mark classification.  Combining marks carry no
        //    advance (they overlay their base); GPOS supplies their offset.
        var advances = new int[m];
        var isMark = new bool[m];
        for (int k = 0; k < m; k++)
        {
            isMark[k] = font.IsMarkGlyph(glyphs[k]);
            advances[k] = isMark[k] ? 0 : font.GetAdvanceWidth(glyphs[k]);
        }

        // 5. GPOS mark attachment (logical order): each mark attaches to the
        //    preceding mark (mark-to-mark) or the nearest preceding base
        //    (mark-to-base).  Anchor deltas are stored against the target.
        var attachTarget = new int[m];
        var anchorDx = new int[m];
        var anchorDy = new int[m];
        for (int i = 0; i < m; i++)
        {
            attachTarget[i] = -1;
            if (!isMark[i] || i == 0)
                continue;

            int prev = i - 1;
            if (isMark[prev] && font.TryGetMarkToMarkAnchor(glyphs[prev], glyphs[i], out int mdx, out int mdy))
            {
                attachTarget[i] = prev;
                anchorDx[i] = mdx;
                anchorDy[i] = mdy;
                continue;
            }

            int b = prev;
            while (b >= 0 && isMark[b])
                b--;
            if (b >= 0 && font.TryGetMarkToBaseAnchor(glyphs[b], glyphs[i], out int bdx, out int bdy))
            {
                attachTarget[i] = b;
                anchorDx[i] = bdx;
                anchorDy[i] = bdy;
            }
        }

        // 6. Reorder into visual order, then resolve mark offsets against the
        //    visual pen origins (so attachment is correct regardless of the
        //    base appearing before or after its mark after RTL reversal).
        var order = ReorderVisual(levels);
        var inverse = new int[m];
        for (int v = 0; v < m; v++)
            inverse[order[v]] = v;

        var origin = new long[m];
        long acc = 0;
        for (int v = 0; v < m; v++)
        {
            origin[v] = acc;
            acc += advances[order[v]];
        }

        var xOffset = new int[m];
        var yOffset = new int[m];
        for (int i = 0; i < m; i++) // logical order: targets resolve before dependents
        {
            int t = attachTarget[i];
            if (t < 0)
                continue;
            int vm = inverse[i];
            int vt = inverse[t];
            xOffset[vm] = (int)(origin[vt] - origin[vm]) + xOffset[vt] + anchorDx[i];
            yOffset[vm] = yOffset[vt] + anchorDy[i];
        }

        for (int v = 0; v < m; v++)
        {
            int idx = order[v];
            result.Add(new ShapedGlyph(glyphs[idx], advances[idx], xOffset[v], yOffset[v]));
        }
        return result;
    }

    // ── Arabic joining ────────────────────────────────────────────────────

    private static ArabicForm[] ComputeArabicForms(JoiningType[] joining)
    {
        int n = joining.Length;
        var forms = new ArabicForm[n];
        for (int i = 0; i < n; i++)
        {
            JoiningType jt = joining[i];
            if (jt is JoiningType.NonJoining or JoiningType.Transparent or JoiningType.Causing)
            {
                forms[i] = ArabicForm.None;
                continue;
            }

            bool connectsPrev = ConnectsToPrevious(jt);
            bool connectsNext = ConnectsToNext(jt);

            int prev = PreviousNonTransparent(joining, i);
            int next = NextNonTransparent(joining, i);

            bool joinPrev = connectsPrev && prev >= 0 && ConnectsToNext(joining[prev]);
            bool joinNext = connectsNext && next >= 0 && ConnectsToPrevious(joining[next]);

            forms[i] = (joinPrev, joinNext) switch
            {
                (true, true) => ArabicForm.Medial,
                (true, false) => ArabicForm.Final,
                (false, true) => ArabicForm.Initial,
                _ => ArabicForm.Isolated,
            };
        }
        return forms;
    }

    // A glyph "connects to the previous" letter (has a joining stroke on the
    // side facing the preceding character) for dual- and right-joining types.
    private static bool ConnectsToPrevious(JoiningType jt) =>
        jt is JoiningType.Dual or JoiningType.Right or JoiningType.Causing;

    // A glyph "connects to the next" letter for dual- and left-joining types.
    private static bool ConnectsToNext(JoiningType jt) =>
        jt is JoiningType.Dual or JoiningType.Left or JoiningType.Causing;

    private static int PreviousNonTransparent(JoiningType[] joining, int i)
    {
        for (int j = i - 1; j >= 0; j--)
            if (joining[j] != JoiningType.Transparent)
                return j;
        return -1;
    }

    private static int NextNonTransparent(JoiningType[] joining, int i)
    {
        for (int j = i + 1; j < joining.Length; j++)
            if (joining[j] != JoiningType.Transparent)
                return j;
        return -1;
    }

    // ── Ligatures ───────────────────────────────────────────────────────────

    private static readonly string[] LigatureFeatures = ["rlig", "calt", "liga"];

    private static void ApplyLigatures(TrueTypeFont font, List<int> glyphs, List<int> levels)
    {
        foreach (string feature in LigatureFeatures)
        {
            int i = 0;
            while (i < glyphs.Count)
            {
                if (font.TryApplyLigature(feature, glyphs, i, out int lig, out int count) && count > 1)
                {
                    glyphs[i] = lig;
                    glyphs.RemoveRange(i + 1, count - 1);
                    levels.RemoveRange(i + 1, count - 1);
                }
                i++;
            }
        }
    }

    // ── Bidi (pragmatic subset) ──────────────────────────────────────────────

    private static int ResolveLevel(int codepoint, bool baseRtl)
    {
        if (IsRightToLeft(codepoint))
            return 1; // RTL run (lowest odd level)
        if (IsStrongLtr(codepoint) || IsNumber(codepoint))
            return baseRtl ? 2 : 0; // LTR content (even level)
        return baseRtl ? 1 : 0; // neutrals follow the base direction
    }

    /// <summary>
    /// Returns the visual-order permutation of the glyph buffer per the
    /// Unicode bidi reordering rule: from the highest level down to the lowest
    /// odd level, reverse every contiguous span of glyphs at that level or above.
    /// </summary>
    private static int[] ReorderVisual(List<int> levels)
    {
        int n = levels.Count;
        var order = new int[n];
        for (int i = 0; i < n; i++)
            order[i] = i;

        int maxLevel = 0;
        int minOdd = int.MaxValue;
        foreach (int lv in levels)
        {
            if (lv > maxLevel) maxLevel = lv;
            if ((lv & 1) == 1 && lv < minOdd) minOdd = lv;
        }
        if (minOdd == int.MaxValue)
            return order; // no RTL content

        for (int level = maxLevel; level >= minOdd; level--)
        {
            int i = 0;
            while (i < n)
            {
                if (levels[order[i]] >= level)
                {
                    int j = i;
                    while (j < n && levels[order[j]] >= level)
                        j++;
                    Array.Reverse(order, i, j - i);
                    i = j;
                }
                else
                {
                    i++;
                }
            }
        }
        return order;
    }

    // ── Character classification ─────────────────────────────────────────────

    private static bool IsRightToLeft(int cp) =>
        (cp >= 0x0590 && cp <= 0x05FF) ||   // Hebrew
        (cp >= 0x0600 && cp <= 0x06FF) ||   // Arabic
        (cp >= 0x0750 && cp <= 0x077F) ||   // Arabic Supplement
        (cp >= 0x08A0 && cp <= 0x08FF) ||   // Arabic Extended-A
        (cp >= 0xFB1D && cp <= 0xFB4F) ||   // Hebrew presentation forms
        (cp >= 0xFB50 && cp <= 0xFDFF) ||   // Arabic presentation forms-A
        (cp >= 0xFE70 && cp <= 0xFEFF);     // Arabic presentation forms-B

    private static bool IsArabicJoiner(int cp) =>
        (cp >= 0x0600 && cp <= 0x06FF) || cp == 0x200D;

    private static bool IsStrongLtr(int cp) =>
        (cp >= 'A' && cp <= 'Z') || (cp >= 'a' && cp <= 'z') ||
        (cp >= 0x00C0 && cp <= 0x024F) ||   // Latin-1 supplement / extended
        (cp >= 0x0370 && cp <= 0x03FF) ||   // Greek
        (cp >= 0x0400 && cp <= 0x04FF);     // Cyrillic

    private static bool IsNumber(int cp) => cp >= '0' && cp <= '9';

    private static bool FirstStrongIsRtl(List<int> codepoints)
    {
        foreach (int cp in codepoints)
        {
            if (IsRightToLeft(cp))
                return true;
            if (IsStrongLtr(cp))
                return false;
        }
        return false;
    }

    private static List<int> ToCodepoints(string text)
    {
        var list = new List<int>(text.Length);
        for (int i = 0; i < text.Length;)
        {
            list.Add(UnicodeCodepointReader.ReadCodePoint(text, i, out int nextIndex));
            i = nextIndex;
        }
        return list;
    }

    // ── Arabic joining-type table (UAX #24 subset for the main Arabic block) ──

    private static JoiningType GetJoiningType(int cp)
    {
        // Transparent: combining marks (harakat, hamza above/below, Quranic marks).
        if ((cp >= 0x0610 && cp <= 0x061A) ||
            (cp >= 0x064B && cp <= 0x065F) ||
            cp == 0x0670 ||
            (cp >= 0x06D6 && cp <= 0x06DC) ||
            (cp >= 0x06DF && cp <= 0x06E4) ||
            cp == 0x06E7 || cp == 0x06E8 ||
            (cp >= 0x06EA && cp <= 0x06ED))
            return JoiningType.Transparent;

        // Join-causing: tatweel and ZERO WIDTH JOINER.
        if (cp == 0x0640 || cp == 0x200D)
            return JoiningType.Causing;

        // Right-joining letters (connect only toward the preceding character).
        switch (cp)
        {
            case 0x0622: // alef madda
            case 0x0623: // alef hamza above
            case 0x0624: // waw hamza
            case 0x0625: // alef hamza below
            case 0x0627: // alef
            case 0x0629: // teh marbuta
            case 0x062F: // dal
            case 0x0630: // thal
            case 0x0631: // reh
            case 0x0632: // zain
            case 0x0648: // waw
            case 0x0671: // alef wasla
            case 0x0672: case 0x0673: case 0x0675:
            case 0x0688: // ddal (Urdu)
            case 0x0691: // rreh (Urdu)
            case 0x0698: // jeh (Persian)
            case 0x06C0:
            case 0x06C3:
            case 0x06CD:
            case 0x06D2: // yeh barree
            case 0x06D3:
                return JoiningType.Right;
        }

        // Remaining Arabic-block letters are dual-joining.
        if ((cp >= 0x0620 && cp <= 0x064A) ||
            (cp >= 0x066E && cp <= 0x066F) ||
            (cp >= 0x0671 && cp <= 0x06D3) ||
            cp == 0x06D5 ||
            (cp >= 0x06EE && cp <= 0x06EF) ||
            (cp >= 0x06FA && cp <= 0x06FC) ||
            cp == 0x06FF ||
            (cp >= 0x0750 && cp <= 0x077F))
            return JoiningType.Dual;

        return JoiningType.NonJoining;
    }
}
