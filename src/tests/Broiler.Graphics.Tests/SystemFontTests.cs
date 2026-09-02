using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Broiler.Graphics.Tests;

/// <summary>
/// Coverage for the installed-font list a font picker is built from.
/// </summary>
/// <remarks>
/// The host's own font set cannot be asserted against — a CI container and a developer's box have
/// different fonts, and asserting "at least one" would pass on a machine whose scan silently
/// returned garbage. So the file reader is driven with fonts synthesized here, where the expected
/// name is known exactly, and the registration surface is tested against a stub enumerator.
/// </remarks>
internal static class SystemFontTests
{
    internal static void Register(List<(string Name, Action Body)> tests)
    {
        tests.Add(("Font name table yields the family and its face", ReadsFamilyAndSubfamily));
        tests.Add(("An English Windows name beats the other records", PrefersEnglishWindowsName));
        tests.Add(("A collection yields every font it holds", ReadsCollection));
        tests.Add(("A file that is not a font yields no names", RejectsNonFont));
        tests.Add(("Registered enumerator replaces the directory scan", EnumeratorWins));
        tests.Add(("Family list is sorted, deduplicated and blank-free", NormalizesFamilies));
        tests.Add(("An enumerator that throws yields an empty list", SwallowsEnumeratorFailure));
        tests.Add(("UseIfUnset yields to an enumerator already registered", UseIfUnsetYields));
    }

    private static void ReadsFamilyAndSubfamily()
    {
        using TempFont font = TempFont.Write(BuildFont(
            (Platform: 3, Encoding: 1, Language: 0x0409, NameId: 1, Value: "Broiler Sans"),
            (3, 1, 0x0409, 2, "Bold Italic")));

        FontNameTable.FaceName[] faces = FontNameTable.Read(font.Path);

        AssertEx.AreEqual(1, faces.Length);
        AssertEx.AreEqual("Broiler Sans", faces[0].Family);
        AssertEx.AreEqual("Bold Italic", faces[0].Subfamily);
    }

    /// <summary>
    /// Records are in no useful order, so the reader scores them. A font whose first record is a
    /// Japanese name must still be listed under the name this platform shows.
    /// </summary>
    private static void PrefersEnglishWindowsName()
    {
        using TempFont font = TempFont.Write(BuildFont(
            (Platform: 3, Encoding: 1, Language: 0x0411, NameId: 1, Value: "ブロイラー"),
            (1, 0, 0, 1, "Broiler Mac"),
            (3, 1, 0x0409, 1, "Broiler Sans")));

        FontNameTable.FaceName[] faces = FontNameTable.Read(font.Path);

        AssertEx.AreEqual(1, faces.Length);
        AssertEx.AreEqual("Broiler Sans", faces[0].Family);
    }

    private static void ReadsCollection()
    {
        using TempFont font = TempFont.Write(BuildCollection(
            [(3, 1, 0x0409, 1, "Broiler Sans"), (3, 1, 0x0409, 2, "Regular")],
            [(3, 1, 0x0409, 1, "Broiler Serif"), (3, 1, 0x0409, 2, "Bold")]));

        FontNameTable.FaceName[] faces = FontNameTable.Read(font.Path);

        AssertEx.AreEqual(2, faces.Length);
        AssertEx.AreEqual("Broiler Sans", faces[0].Family);
        AssertEx.AreEqual("Broiler Serif", faces[1].Family);
        AssertEx.AreEqual("Bold", faces[1].Subfamily);
    }

    private static void RejectsNonFont()
    {
        using TempFont font = TempFont.Write(Encoding.ASCII.GetBytes("This is not a font, it only ends in .ttf."));

        AssertEx.AreEqual(0, FontNameTable.Read(font.Path).Length);
    }

    private static void EnumeratorWins()
    {
        using SystemFontRegistration registration = SystemFontRegistration.Of(() => ["Zeta", "Alpha"]);

        IReadOnlyList<string> families = BSystemFonts.GetFamilies();

        AssertEx.IsTrue(BSystemFonts.HasEnumerator, "an enumerator was registered");
        AssertEx.AreEqual(2, families.Count);
        AssertEx.AreEqual("Alpha", families[0]);
        AssertEx.AreEqual("Zeta", families[1]);
    }

    private static void NormalizesFamilies()
    {
        using SystemFontRegistration registration = SystemFontRegistration.Of(
            () => ["  Beta  ", "alpha", "", "  ", "Alpha", "Beta"]);

        IReadOnlyList<string> families = BSystemFonts.GetFamilies();

        AssertEx.AreEqual(2, families.Count);
        AssertEx.AreEqual("alpha", families[0]);
        AssertEx.AreEqual("Beta", families[1]);
    }

    private static void SwallowsEnumeratorFailure()
    {
        using SystemFontRegistration registration =
            SystemFontRegistration.Of(() => throw new InvalidOperationException("no font service"));

        AssertEx.AreEqual(0, BSystemFonts.GetFamilies().Count);
    }

    private static void UseIfUnsetYields()
    {
        using SystemFontRegistration registration = SystemFontRegistration.Of(() => ["Alpha"]);

        bool took = BSystemFonts.UseIfUnset(() => ["Beta"]);

        AssertEx.IsTrue(!took, "the second enumerator was refused");
        AssertEx.AreEqual("Alpha", BSystemFonts.GetFamilies()[0]);
    }

    private static byte[] BuildFont(params (int Platform, int Encoding, int Language, int NameId, string Value)[] records) =>
        BuildFont(0, records);

    /// <summary>
    /// A minimal sfnt carrying nothing but a <c>name</c> table. Every other table is optional as
    /// far as this reader is concerned, and leaving them out keeps the expected bytes readable.
    /// </summary>
    /// <remarks>
    /// A table directory entry holds an offset from the start of the <i>file</i>, not from the
    /// start of the font — which only shows once a font sits inside a collection, and is exactly
    /// what <paramref name="baseOffset"/> accounts for.
    /// </remarks>
    private static byte[] BuildFont(int baseOffset, (int Platform, int Encoding, int Language, int NameId, string Value)[] records)
    {
        var strings = new List<byte[]>();
        foreach ((int platform, int encoding, _, _, string value) in records)
        {
            strings.Add(platform == 1 && encoding == 0
                ? Encoding.Latin1.GetBytes(value)
                : Encoding.BigEndianUnicode.GetBytes(value));
        }

        int storageOffset = 6 + (records.Length * 12);
        var name = new List<byte>();
        WriteUInt16(name, 0);
        WriteUInt16(name, records.Length);
        WriteUInt16(name, storageOffset);

        int running = 0;
        for (int index = 0; index < records.Length; index++)
        {
            (int platform, int encoding, int language, int nameId, _) = records[index];
            WriteUInt16(name, platform);
            WriteUInt16(name, encoding);
            WriteUInt16(name, language);
            WriteUInt16(name, nameId);
            WriteUInt16(name, strings[index].Length);
            WriteUInt16(name, running);
            running += strings[index].Length;
        }

        foreach (byte[] value in strings)
            name.AddRange(value);

        var font = new List<byte>();
        WriteUInt32(font, 0x00010000);
        WriteUInt16(font, 1);   // table count
        WriteUInt16(font, 16);  // search range
        WriteUInt16(font, 0);   // entry selector
        WriteUInt16(font, 0);   // range shift
        WriteUInt32(font, 0x6E616D65);
        WriteUInt32(font, 0);   // checksum, unread
        WriteUInt32(font, (uint)(baseOffset + 28));  // the name table follows the 16-byte directory entry
        WriteUInt32(font, (uint)name.Count);
        font.AddRange(name);
        return [.. font];
    }

    private static byte[] BuildCollection(params (int Platform, int Encoding, int Language, int NameId, string Value)[][] fonts)
    {
        // Two passes: the fonts' lengths decide where each one starts, and where it starts decides
        // the offsets written inside it.
        int offset = 12 + (fonts.Length * 4);
        var offsets = new int[fonts.Length];
        for (int index = 0; index < fonts.Length; index++)
        {
            offsets[index] = offset;
            offset += BuildFont(0, fonts[index]).Length;
        }

        var collection = new List<byte>();
        WriteUInt32(collection, 0x74746366);   // 'ttcf'
        WriteUInt32(collection, 0x00010000);
        WriteUInt32(collection, (uint)fonts.Length);
        foreach (int fontOffset in offsets)
            WriteUInt32(collection, (uint)fontOffset);

        for (int index = 0; index < fonts.Length; index++)
            collection.AddRange(BuildFont(offsets[index], fonts[index]));

        return [.. collection];
    }

    private static void WriteUInt16(List<byte> target, int value)
    {
        target.Add((byte)(value >> 8));
        target.Add((byte)value);
    }

    private static void WriteUInt32(List<byte> target, uint value)
    {
        target.Add((byte)(value >> 24));
        target.Add((byte)(value >> 16));
        target.Add((byte)(value >> 8));
        target.Add((byte)value);
    }

    /// <summary>A font file that deletes itself, so a failing assertion leaves no litter behind.</summary>
    private sealed class TempFont : IDisposable
    {
        private TempFont(string path) => Path = path;

        internal string Path { get; }

        internal static TempFont Write(byte[] bytes)
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "broiler-font-" + Guid.NewGuid().ToString("N") + ".ttf");
            File.WriteAllBytes(path, bytes);
            return new TempFont(path);
        }

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// Registers an enumerator for one test and clears it afterwards. The registration is
    /// process-wide, so a test that left one installed would decide the next test's answer.
    /// </summary>
    private sealed class SystemFontRegistration : IDisposable
    {
        private SystemFontRegistration()
        {
        }

        internal static SystemFontRegistration Of(BFontFamilyEnumerator enumerator)
        {
            BSystemFonts.Use(enumerator);
            return new SystemFontRegistration();
        }

        public void Dispose() => BSystemFonts.Clear();
    }
}
