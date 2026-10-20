using System;
using System.IO;
using System.Text;

namespace REDox.Ini.Tests;

/// <summary>
///     IniDocument の境界値・異常系・回帰テスト。
///     方針:
///     - 通常の INI として自然に期待できるケースは有効なテストにする。
///     - 現実のファイル入力で問題になりやすい既知ケースも有効なテストにする。
///     （現在の実装では失敗するものを含む。修正後の回帰防止用）
///     - INI に統一仕様がなく、REDox.Ini として仕様決定が必要なケースは Skip にする。
/// </summary>
public sealed class IniComprehensiveTest
{
    private static DElement Parse(string ini, IniDocumentOptions options = default)
    {
        return IniDocument.Parse(ini, options: options).RootElement;
    }

    // ---------------------------------------------------------------------
    // Empty / whitespace / basic boundary cases
    // ---------------------------------------------------------------------

    [Fact]
    public void EmptyDocument_ShouldParse()
    {
        Assert.True(IniDocument.TryParse([], out var document));
        Assert.NotNull(document);
        document!.Dispose();
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    [InlineData(" \t\r\n \t\n")]
    public void WhitespaceOnlyDocument_ShouldParse(string ini)
    {
        Assert.True(IniDocument.TryParse(Encoding.UTF8.GetBytes(ini), out var document));
        Assert.NotNull(document);
        document!.Dispose();
    }

    [Theory]
    [InlineData("; comment")]
    [InlineData("# comment")]
    [InlineData("   ; comment")]
    [InlineData("\t# comment")]
    [InlineData("; comment\n")]
    [InlineData("# comment\r\n")]
    public void CommentOnlyDocument_ShouldParse(string ini)
    {
        Assert.True(IniDocument.TryParse(Encoding.UTF8.GetBytes(ini), out var document));
        Assert.NotNull(document);
        document!.Dispose();
    }

    [Fact]
    public void EmptyValueAtEndOfFile_ShouldParse()
    {
        var root = Parse("key=");

        Assert.Equal(string.Empty, root.GetProperty("key").GetString());
    }

    [Fact]
    public void EmptyValueContainingOnlyWhitespace_ShouldBecomeEmptyString()
    {
        var root = Parse("key=   \t");

        Assert.Equal(string.Empty, root.GetProperty("key").GetString());
    }

    // ---------------------------------------------------------------------
    // UTF-8 / Unicode / BOM
    // ---------------------------------------------------------------------

    [Fact]
    public void Utf8BomBeforeGlobalKey_ShouldBeIgnored()
    {
        // U+FEFF is encoded as EF BB BF by Encoding.UTF8.
        var bytes = Encoding.UTF8.GetBytes("\uFEFFkey=value");

        using var document = IniDocument.Parse(bytes);

        Assert.Equal("value", document.RootElement.GetProperty("key").GetString());
    }

    [Fact]
    public void Utf8BomBeforeSection_ShouldBeIgnored()
    {
        var bytes = Encoding.UTF8.GetBytes("\uFEFF[section]\nkey=value");

        using var document = IniDocument.Parse(bytes);

        Assert.Equal(
            "value",
            document.RootElement
                .GetProperty("section")
                .GetProperty("key")
                .GetString());
    }

    [Fact]
    public void UnicodeKeyValueAndSection_ShouldParse()
    {
        const string ini = """
                           日本語キー=日本語の値
                           [設定]
                           名前=レドックス
                           絵文字=🎮🔥
                           """;

        var root = Parse(ini);

        Assert.Equal("日本語の値", root.GetProperty("日本語キー").GetString());
        Assert.Equal("レドックス", root.GetProperty("設定").GetProperty("名前").GetString());
        Assert.Equal("🎮🔥", root.GetProperty("設定").GetProperty("絵文字").GetString());
    }


    [Fact]
    public void Unicode_ShouldRoundTrip()
    {
        const string ini = """
                           日本語キー=日本語の値
                           [設定]
                           絵文字=🎮🔥
                           """;

        using var document = IniDocument.Parse(ini);
        var encoded = IniDocument.EncodeToString(document.RootElement);
        using var reparsed = IniDocument.Parse(encoded);

        Assert.Equal("日本語の値", reparsed.RootElement.GetProperty("日本語キー").GetString());
        Assert.Equal("🎮🔥", reparsed.RootElement.GetProperty("設定").GetProperty("絵文字").GetString());
    }

    // ---------------------------------------------------------------------
    // Newline handling
    // ---------------------------------------------------------------------

    [Fact]
    public void LfOnly_ShouldParse()
    {
        var root = Parse("a=1\nb=2\n");

        Assert.Equal("1", root.GetProperty("a").GetString());
        Assert.Equal("2", root.GetProperty("b").GetString());
    }

    [Fact]
    public void CrLfOnly_ShouldParse()
    {
        var root = Parse("a=1\r\nb=2\r\n");

        Assert.Equal("1", root.GetProperty("a").GetString());
        Assert.Equal("2", root.GetProperty("b").GetString());
    }

    [Fact]
    public void CrOnly_ShouldParseAsLineSeparator()
    {
        var root = Parse("a=1\rb=2\r");

        Assert.Equal("1", root.GetProperty("a").GetString());
        Assert.Equal("2", root.GetProperty("b").GetString());
    }

    [Fact]
    public void MixedLineEndings_ShouldParse()
    {
        var root = Parse("a=1\nb=2\r\nc=3\rd=4");

        Assert.Equal("1", root.GetProperty("a").GetString());
        Assert.Equal("2", root.GetProperty("b").GetString());
        Assert.Equal("3", root.GetProperty("c").GetString());
        Assert.Equal("4", root.GetProperty("d").GetString());
    }

    // ---------------------------------------------------------------------
    // Key parsing
    // ---------------------------------------------------------------------

    [Fact]
    public void KeyLeadingWhitespace_ShouldBeIgnored()
    {
        var root = Parse(" \t  key=value");

        Assert.Equal("value", root.GetProperty("key").GetString());
    }

    [Fact]
    public void KeyTrailingWhitespaceBeforeEquals_ShouldBeTrimmed()
    {
        var root = Parse("key \t  =value");

        Assert.Equal("value", root.GetProperty("key").GetString());
    }

    [Fact]
    public void KeyInternalSpaces_ShouldBePreserved()
    {
        var root = Parse("my key=value");

        Assert.Equal("value", root.GetProperty("my key").GetString());
    }

    [Fact]
    public void KeyMayContainCommentCharactersWhenNotFirstCharacter()
    {
        var root = Parse("a#b=1\na;b=2");

        Assert.Equal("1", root.GetProperty("a#b").GetString());
        Assert.Equal("2", root.GetProperty("a;b").GetString());
    }

    [Fact]
    public void LineWithoutEquals_ShouldBeSkipped()
    {
        Assert.False(IniDocument.TryParse("this is not a key value\nkey=value"u8, out _));
    }

    [Fact]
    public void LineWithoutEqualsAtEndOfFile_ShouldNotBreakPreviousValues()
    {
        Assert.False(IniDocument.TryParse("key=value\ninvalid line"u8, out _));
    }

    // ---------------------------------------------------------------------
    // Value parsing
    // ---------------------------------------------------------------------

    [Fact]
    public void ValueLeadingWhitespace_ShouldBeTrimmed()
    {
        var root = Parse("key=   \tvalue");

        Assert.Equal("value", root.GetProperty("key").GetString());
    }

    [Fact]
    public void ValueTrailingWhitespace_ShouldBeTrimmed()
    {
        var root = Parse("key=value   \t");

        Assert.Equal("value", root.GetProperty("key").GetString());
    }

    [Fact]
    public void ValueInternalWhitespace_ShouldBePreserved()
    {
        var root = Parse("key=a  b\tc");

        Assert.Equal("a  b\tc", root.GetProperty("key").GetString());
    }

    [Fact]
    public void ValueMayContainAdditionalEqualsCharacters()
    {
        var root = Parse("connection=a=b=c");

        Assert.Equal("a=b=c", root.GetProperty("connection").GetString());
    }

    [Theory]
    [InlineData("a;b")]
    [InlineData("a#b")]
    [InlineData(";not-an-inline-comment")]
    [InlineData("#not-an-inline-comment")]
    [InlineData("[text]")]
    [InlineData("]")]
    [InlineData("https://example.test/path#fragment")]
    public void ValueSpecialCharacters_ShouldBePreserved(string expected)
    {
        var root = Parse($"key={expected}");

        Assert.Equal(expected, root.GetProperty("key").GetString());
    }

    [Fact]
    public void InlineSemicolon_ShouldCurrentlyBePartOfValue()
    {
        var root = Parse("key=value ; comment");

        Assert.Equal("value ; comment", root.GetProperty("key").GetString());
    }

    [Fact]
    public void InlineHash_ShouldCurrentlyBePartOfValue()
    {
        var root = Parse("key=value # comment");

        Assert.Equal("value # comment", root.GetProperty("key").GetString());
    }

    // ---------------------------------------------------------------------
    // Comment parsing
    // ---------------------------------------------------------------------

    [Fact]
    public void SemicolonCommentBeforeKey_ShouldBeIgnored()
    {
        var root = Parse("; comment\nkey=value");

        Assert.Equal("value", root.GetProperty("key").GetString());
    }

    [Fact]
    public void HashCommentBeforeKey_ShouldBeIgnored()
    {
        var root = Parse("# comment\nkey=value");

        Assert.Equal("value", root.GetProperty("key").GetString());
    }

    [Fact]
    public void IndentedComment_ShouldBeIgnored()
    {
        var root = Parse("   ; comment\n\t# comment\nkey=value");

        Assert.Equal("value", root.GetProperty("key").GetString());
    }

    [Fact]
    public void CommentAtEndOfFileWithoutNewLine_ShouldParse()
    {
        Assert.True(IniDocument.TryParse("key=value\n; trailing comment"u8, out var document));
        Assert.NotNull(document);

        using (document)
        {
            Assert.Equal("value", document!.RootElement.GetProperty("key").GetString());
        }
    }

    // ---------------------------------------------------------------------
    // Section parsing
    // ---------------------------------------------------------------------

    [Fact]
    public void SectionAtEndOfFile_ShouldParse()
    {
        Assert.True(IniDocument.TryParse("[empty]"u8, out var document));
        Assert.NotNull(document);

        using (document)
        {
            _ = document!.RootElement.GetProperty("empty");
        }
    }

    [Fact]
    public void SectionMayContainUnicode()
    {
        var root = Parse("[日本語セクション]\nkey=value");

        Assert.Equal(
            "value",
            root.GetProperty("日本語セクション").GetProperty("key").GetString());
    }

    [Fact]
    public void SectionMayContainEqualsCharacter()
    {
        var root = Parse("[a=b]\nkey=value");

        Assert.Equal("value", root.GetProperty("a=b").GetProperty("key").GetString());
    }

    [Fact]
    public void SectionMayContainCommentCharacters()
    {
        var root = Parse("[a#b;c]\nkey=value");

        Assert.Equal("value", root.GetProperty("a#b;c").GetProperty("key").GetString());
    }

    [Fact]
    public void EmptySectionBetweenSections_ShouldNotAffectFollowingSection()
    {
        var root = Parse("[first]\n[second]\nkey=value");

        Assert.Equal("value", root.GetProperty("second").GetProperty("key").GetString());
    }

    [Fact]
    public void SectionValueMayBeEmpty()
    {
        var root = Parse("[section]\nkey=");

        Assert.Equal(string.Empty, root.GetProperty("section").GetProperty("key").GetString());
    }

    [Fact]
    public void CommentsInsideSection_ShouldBeIgnored()
    {
        var root = Parse("""
                         [section]
                         ; comment
                         # comment
                         key=value
                         """);

        Assert.Equal("value", root.GetProperty("section").GetProperty("key").GetString());
    }

    // ---------------------------------------------------------------------
    // Known robustness issues / regression tests
    // These express the safer behavior we normally want from the API.
    // They may FAIL against the current implementation and are intended
    // to become regression tests when the corresponding bug is fixed.
    // ---------------------------------------------------------------------

    [Fact]
    public void StreamParse_ShouldReadFromCurrentPositionToEnd()
    {
        var prefix = Encoding.UTF8.GetBytes("xxxx");
        var ini = Encoding.UTF8.GetBytes("key=value");

        var bytes = new byte[prefix.Length + ini.Length];
        Buffer.BlockCopy(prefix, 0, bytes, 0, prefix.Length);
        Buffer.BlockCopy(ini, 0, bytes, prefix.Length, ini.Length);

        using var stream = new MemoryStream(bytes);
        stream.Position = prefix.Length;

        using var document = IniDocument.Parse(stream);

        Assert.Equal("value", document.RootElement.GetProperty("key").GetString());
    }

    [Fact]
    public void TryParse_UnclosedSection_ShouldReturnFalse()
    {
        Assert.False(IniDocument.TryParse("[section"u8, out var document));
        Assert.Null(document);
    }

    [Fact]
    public void TryParse_SectionNameMustNotCrossLineBoundary()
    {
        Assert.False(IniDocument.TryParse("[section\nname]\nkey=value"u8, out var document));
        Assert.Null(document);
    }

    [Fact]
    public void TryParse_TrailingGarbageAfterSectionHeader_ShouldReturnFalse()
    {
        Assert.False(IniDocument.TryParse("[section]garbage\nkey=value"u8, out var document));
        Assert.Null(document);
    }

    // ---------------------------------------------------------------------
    // PreserveTrivia
    // ---------------------------------------------------------------------

    [Fact]
    public void PreserveTrivia_SemicolonComment_ShouldRemain()
    {
        const string ini = """
                           ; comment
                           [section]
                           key=value
                           """;

        using var document = IniDocument.Parse(
            ini,
            options: new IniDocumentOptions { PreserveTrivia = true });

        var encoded = IniDocument.EncodeToString(
            document.RootElement,
            new IniWriteOptions { PreserveTrivia = true });

        Assert.Contains("; comment", encoded);
    }

    [Fact]
    public void PreserveTrivia_DoxRoundTrip_ShouldRestoreIniComments()
    {
        const string ini = """
                           ; global comment
                           app=Demo
                           # section comment
                           [window]
                           ; width comment
                           width=800
                           """;

        using var iniDocument = IniDocument.Parse(
            ini,
            options: new IniDocumentOptions { PreserveTrivia = true });

        var dox = DoxDocument.Encode(
            iniDocument.RootElement,
            new DoxWriteOptions { PreserveTrivia = true });

        using var doxDocument = DoxDocument.Parse(dox);

        var encoded = IniDocument.EncodeToString(
            doxDocument.RootElement,
            new IniWriteOptions { PreserveTrivia = true, WriteSpaces = true });

        TestContext.Current.TestOutputHelper?.WriteLine(encoded);

        Assert.Contains("; global comment", encoded);
        Assert.Contains("; section comment", encoded);
//        Assert.Contains("; width comment", encoded);
        Assert.Contains("app", encoded);
        Assert.Contains("Demo", encoded);
        Assert.Contains("[window]", encoded);
        Assert.Contains("width", encoded);
        Assert.Contains("800", encoded);
        Assert.DoesNotContain(";; global comment", encoded);
        Assert.DoesNotContain(";# section comment", encoded);
    }

    [Fact]
    public void PreserveTrivia_HashCommentMarker_ShouldRemainHash()
    {
        const string ini = """
                           # comment
                           [section]
                           key=value
                           """;

        using var document = IniDocument.Parse(
            ini,
            options: new IniDocumentOptions { PreserveTrivia = true });

        var encoded = IniDocument.EncodeToString(
            document.RootElement,
            new IniWriteOptions { PreserveTrivia = true });

        Assert.Contains("# comment", encoded);
        Assert.DoesNotContain("; comment", encoded);
    }

    [Fact]
    public void PreserveTrivia_ShouldNotInsertAdditionalBlankLines()
    {
        const string ini = "[section]\nkey=value\n";

        using var document = IniDocument.Parse(
            ini,
            options: new IniDocumentOptions { PreserveTrivia = true });

        var encoded = IniDocument.EncodeToString(
            document.RootElement,
            new IniWriteOptions { PreserveTrivia = true });

        Assert.Equal(ini, encoded);
    }

    [Fact]
    public void PreserveTrivia_BlankLines_ShouldRemainExact()
    {
        const string ini = "a=1\n\n\n[section]\n\nkey=value\n\n";

        using var document = IniDocument.Parse(
            ini,
            options: new IniDocumentOptions { PreserveTrivia = true });

        var encoded = IniDocument.EncodeToString(
            document.RootElement,
            new IniWriteOptions { PreserveTrivia = true });

        Assert.Equal(ini, encoded);
    }

    [Fact]
    public void PreserveTrivia_TrailingCommentWithoutNewLine_ShouldNotGainNewLine()
    {
        const string ini = "key=value\n; trailing";

        using var document = IniDocument.Parse(
            ini,
            options: new IniDocumentOptions { PreserveTrivia = true });

        var encoded = IniDocument.EncodeToString(
            document.RootElement,
            new IniWriteOptions { PreserveTrivia = true });

        Assert.Equal(ini, encoded);
    }

    // The current token model does not appear to store these lexical details.
    // If PreserveTrivia means "semantic trivia only" rather than "byte-for-byte",
    // keep these skipped. If exact lexical preservation is intended, enable them.

    [Fact]
    public void PreserveTrivia_WhitespaceAroundEquals_ShouldRemainExact()
    {
        const string ini = "  key   =   value   \n";

        using var document = IniDocument.Parse(
            ini,
            options: new IniDocumentOptions { PreserveTrivia = true });

        var encoded = IniDocument.EncodeToString(
            document.RootElement,
            new IniWriteOptions { PreserveTrivia = true });

        Assert.Equal(ini, encoded);
    }

    [Fact]
    public void PreserveTrivia_CrLf_ShouldRemainCrLf()
    {
        const string ini = "[section]\r\nkey=value\r\n";

        using var document = IniDocument.Parse(
            ini,
            options: new IniDocumentOptions { PreserveTrivia = true });

        var encoded = IniDocument.EncodeToString(
            document.RootElement,
            new IniWriteOptions { PreserveTrivia = true });

        Assert.Equal(ini, encoded);
    }

    [Theory]
    [InlineData("[ section]\nkey=value\n")]
    [InlineData("[section ]\nkey=value\n")]
    [InlineData("[\tsection]\nkey=value\n")]
    [InlineData("[section\t]\nkey=value\n")]
    public void PreserveTrivia_TrimmedSectionWhitespace_ShouldRemainExact(string ini)
    {
        using var document = IniDocument.Parse(
            ini,
            options: new IniDocumentOptions { PreserveTrivia = true });

        var encoded = IniDocument.EncodeToString(
            document.RootElement,
            new IniWriteOptions { PreserveTrivia = true });

        Assert.Equal(ini, encoded);
    }

    [Theory]
    [InlineData("\0key=value")]
    [InlineData("ke\0y=value")]
    [InlineData("[sec\0tion]\nkey=value")]
    [InlineData("[section]\0\nkey=value")]
    [InlineData("; comm\0ent\nkey=value")]
    [InlineData("# comm\0ent\nkey=value")]
    [InlineData("key=a\0b")]
    public void NullCharacterAnywhere_ShouldFail(string ini)
    {
        Assert.False(
            IniDocument.TryParse(Encoding.UTF8.GetBytes(ini), out var document));

        Assert.Null(document);
    }

    [Theory]
    [InlineData("key=value # comment", "value")]
    [InlineData("key=value ; comment", "value")]
    [InlineData("key=value#fragment", "value#fragment")]
    [InlineData("key=value;fragment", "value;fragment")]
    public void InlineComments_ShouldRequireLeadingWhitespace(
        string ini,
        string expected)
    {
        using var doc = IniDocument.Parse(
            ini,
            options: new IniDocumentOptions
            {
                AllowInlineComments = true
            });

        Assert.Equal(expected, doc.RootElement.GetProperty("key").GetString());
    }

    [Fact]
    public void HashAtBeginningOfValue_WithInlineComments_ShouldBeComment()
    {
        using var doc = IniDocument.Parse(
            "color=#FF8800",
            options: new IniDocumentOptions
            {
                AllowInlineComments = true
            });

        Assert.Equal("", doc.RootElement.GetProperty("color").GetString());
    }

    [Theory]
    [InlineData("key=1\nkey=2")]
    [InlineData("same=value\n[same]\nkey=value")]
    [InlineData("[same]\na=1\n[same]\nb=2")]
    [InlineData("[section]\nkey=1\nkey=2")]
    public void DuplicateNames_ShouldFailByDefault(string ini)
    {
        Assert.False(
            IniDocument.TryParse(
                Encoding.UTF8.GetBytes(ini),
                out var document));

        Assert.Null(document);
    }

    /*
    [Fact]
    public void ParseException_CrOnly_ShouldReportCorrectLine()
    {
        const string ini = "key=value\rinvalid line";

        var ex = Assert.Throws<DocumentParseException>(
            () => IniDocument.Parse(ini));

        Assert.Equal(2, ex.LineNumber);
        Assert.Equal(0, ex.BytePositionInLine);
    }
    */

    // ---------------------------------------------------------------------
    // Encode -> Parse round-trip for representable values
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("a=b=c")]
    [InlineData("a;b")]
    [InlineData("a#b")]
    [InlineData("[text]")]
    [InlineData("https://example.test/a?x=1#fragment")]
    [InlineData("日本語🎮")]
    public void SpecialValue_ShouldRoundTrip(string value)
    {
        using var document = IniDocument.Parse($"key={value}");

        var encoded = IniDocument.EncodeToString(document.RootElement);

        using var reparsed = IniDocument.Parse(encoded);

        Assert.Equal(value, reparsed.RootElement.GetProperty("key").GetString());
    }

    [Fact]
    public void MultipleSections_ShouldRoundTrip()
    {
        const string ini = """
                           global=1
                           [first]
                           a=10
                           [second]
                           b=20
                           """;

        using var document = IniDocument.Parse(ini);
        var encoded = IniDocument.EncodeToString(document.RootElement);
        using var reparsed = IniDocument.Parse(encoded);

        Assert.Equal("1", reparsed.RootElement.GetProperty("global").GetString());
        Assert.Equal("10", reparsed.RootElement.GetProperty("first").GetProperty("a").GetString());
        Assert.Equal("20", reparsed.RootElement.GetProperty("second").GetProperty("b").GetString());
    }

    // ---------------------------------------------------------------------
    // Specification decisions
    // These are deliberately skipped because INI has no single canonical
    // specification. Enable each test after choosing REDox.Ini semantics.
    // ---------------------------------------------------------------------

    [Fact]
    public void EmptyKey_ShouldHaveDefinedBehavior()
    {
        Assert.False(IniDocument.TryParse("=value"u8, out var document));
        Assert.Null(document);
    }

    [Fact]
    public void WhitespaceOnlyKey_ShouldHaveDefinedBehavior()
    {
        Assert.False(IniDocument.TryParse("   =value"u8, out var document));
        Assert.Null(document);
    }

    [Fact]
    public void EmptySectionName_ShouldHaveDefinedBehavior()
    {
        Assert.False(IniDocument.TryParse("[]\nkey=value"u8, out var document));
        Assert.Null(document);
    }

    [Theory]
    [InlineData("[section]\nkey=value")]
    [InlineData("[ section]\nkey=value")]
    [InlineData("[section ]\nkey=value")]
    [InlineData("[  section  ]\nkey=value")]
    [InlineData("[\tsection\t]\nkey=value")]
    public void SectionName_ShouldTrimSurroundingWhitespace(string ini)
    {
        var root = Parse(ini);

        Assert.Equal(
            "value",
            root.GetProperty("section").GetProperty("key").GetString());
    }

    [Fact]
    public void InlineSemicolonComment_ShouldHaveDefinedBehavior()
    {
        var root1 = Parse("key=value ; comment", new IniDocumentOptions { AllowInlineComments = true });

        Assert.Equal("value", root1.GetProperty("key").GetString());

        var root2 = Parse("key=value ; comment", new IniDocumentOptions { AllowInlineComments = false });

        Assert.Equal("value ; comment", root2.GetProperty("key").GetString());
    }

    [Fact]
    public void InlineHashComment_ShouldHaveDefinedBehavior()
    {
        var root1 = Parse("key=value # comment", new IniDocumentOptions { AllowInlineComments = true });

        Assert.Equal("value", root1.GetProperty("key").GetString());

        var root2 = Parse("key=value # comment", new IniDocumentOptions { AllowInlineComments = false });

        Assert.Equal("value # comment", root2.GetProperty("key").GetString());
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("invalid line")]
    [InlineData("key value")]
    [InlineData("123")]
    public void NonEmptyLineWithoutEquals_ShouldFail(string ini)
    {
        Assert.False(IniDocument.TryParse(Encoding.UTF8.GetBytes(ini), out var document));
        Assert.Null(document);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("; comment")]
    [InlineData("# comment")]
    public void NonKeyValueLinesThatAreValid_ShouldParse(string ini)
    {
        Assert.True(IniDocument.TryParse(Encoding.UTF8.GetBytes(ini), out var document));
        document?.Dispose();
    }

    [Fact]
    public void DuplicateKey_ShouldHaveDefinedBehavior()
    {
        Assert.False(IniDocument.TryParse("[section]\nkey=first\nkey=second"u8, out _));

        var root = Parse("[section]\nkey=first\nkey=second", new IniDocumentOptions { AllowDuplicateKeys = true });

        var map = root.GetProperty("section").AsMap();
        Assert.Equal("first", (string?)map[0].Value);
        Assert.Equal("second", (string?)map[1].Value);
    }

    [Fact]
    public void DuplicateGlobalKey_ShouldHaveDefinedBehavior()
    {
        Assert.False(IniDocument.TryParse("key=first\nkey=second"u8, out _));

        var root2 = Parse("key=first\nkey=second", new IniDocumentOptions { AllowDuplicateKeys = true });

        Assert.Equal("first", (string?)root2.AsMap()[0].Value);
        Assert.Equal("second", (string?)root2.AsMap()[1].Value);
    }

    [Fact]
    public void DuplicateSection_ShouldHaveDefinedBehavior()
    {
        var root = Parse("""
                         [section]
                         first=1
                         [section]
                         second=2
                         """, new IniDocumentOptions { AllowDuplicateKeys = true });

        Assert.Equal("1", (string?)root.AsMap()[0].Value["first"]);
        Assert.Equal("2", (string?)root.AsMap()[1].Value["second"]);
    }

    [Fact]
    public void GlobalKeyAndSectionWithSameName_ShouldHaveDefinedBehavior()
    {
        Assert.False(IniDocument.TryParse("same=value\n[same]\nkey=value"u8, out var document));
        Assert.Null(document);

        var ini = Parse("same=value\n[same]\nkey=value", new IniDocumentOptions { AllowDuplicateKeys = true });
        Assert.Equal("value", (string?)ini.AsMap()[0].Value);
        Assert.Equal("value", (string?)ini.AsMap()[1].Value["key"]);
    }

    [Fact]
    public void NullCharacterInValue_ShouldHaveDefinedBehavior()
    {
        Assert.False(IniDocument.TryParse("key=a\0b"u8, out var document));
        Assert.Null(document);
    }

    // ---------------------------------------------------------------------
    // Stress / fuzz safety
    // ---------------------------------------------------------------------

    [Fact]
    public void VeryLongValue_ShouldParse()
    {
        var value = new string('x', 1024 * 1024);

        using var document = IniDocument.Parse($"key={value}");

        Assert.Equal(value, document.RootElement.GetProperty("key").GetString());
    }

    [Fact]
    public void ManyKeys_ShouldParse()
    {
        const int count = 10_000;
        var builder = new StringBuilder(count * 16);

        for (var i = 0; i < count; i++)
        {
            builder.Append("key");
            builder.Append(i);
            builder.Append('=');
            builder.Append(i);
            builder.Append('\n');
        }

        using var document = IniDocument.Parse(builder.ToString());

        Assert.Equal("0", document.RootElement.GetProperty("key0").GetString());
        Assert.Equal("9999", document.RootElement.GetProperty("key9999").GetString());
    }

    [Fact]
    public void ManySections_ShouldParse()
    {
        const int count = 2_000;
        var builder = new StringBuilder(count * 24);

        for (var i = 0; i < count; i++)
        {
            builder.Append("[section");
            builder.Append(i);
            builder.Append("]\nvalue=");
            builder.Append(i);
            builder.Append('\n');
        }

        using var document = IniDocument.Parse(builder.ToString());

        Assert.Equal(
            "0",
            document.RootElement.GetProperty("section0").GetProperty("value").GetString());

        Assert.Equal(
            "1999",
            document.RootElement.GetProperty("section1999").GetProperty("value").GetString());
    }

    [Fact]
    public void RandomBytes_TryParse_ShouldNeverThrowToCaller()
    {
        var random = new Random(0x51A7);

        for (var iteration = 0; iteration < 2_000; iteration++)
        {
            var length = random.Next(0, 257);
            var bytes = new byte[length];
            random.NextBytes(bytes);

            var exception = Record.Exception(() => IniDocument.TryParse(bytes, out _));

            Assert.Null(exception);
        }
    }

    [Fact]
    public void RandomAscii_TryParse_ShouldNeverThrowToCaller()
    {
        var random = new Random(0x1A11);
        var buffer = new char[256];

        for (var iteration = 0; iteration < 2_000; iteration++)
        {
            var length = random.Next(0, buffer.Length + 1);

            for (var i = 0; i < length; i++)
            {
                buffer[i] = (char)random.Next(0x09, 0x7F);
            }

            var text = new string(buffer, 0, length);

            var exception = Record.Exception(() => IniDocument.TryParse(Encoding.UTF8.GetBytes(text), out _));

            Assert.Null(exception);
        }
    }
}