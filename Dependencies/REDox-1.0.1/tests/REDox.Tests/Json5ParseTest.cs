using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using REDox.Json;

namespace REDox.Tests;

public sealed class Json5ParseTest
{
    private static readonly DoxSerializerSettings s_settings = new();

    private static readonly string ParsingDir =
        FindTestSuiteDirectory(string.Empty);

    // (Input JSON5 String, Expected Standard JSON String)
    private static readonly List<(string Input, string Expected)> TestData = new()
    {
        // --- Objects ---
        ("{}", "{}"), // parses empty objects
        ("{\"a\":1}", "{\"a\":1}"), // parses double string property names
        ("{'a':1}", "{\"a\":1}"), // parses single string property names
        ("{a:1}", "{\"a\":1}"), // parses unquoted property names
        ("{$_:1,_$:2,a\u200C:3}", "{\"$_\":1,\"_$\":2,\"a\u200C\":3}"), // parses special character property names
        ("{ùńîċõďë:9}", "{\"ùńîċõďë\":9}"), // parses unicode property names
        ("{\\u0061\\u0062:1,\\u0024\\u005F:2,\\u005F\\u0024:3}",
            "{\"ab\":1,\"$_\":2,\"_$\":3}"), // parses escaped property names
        ("{\"__proto__\":1}", "{\"__proto__\":1}"), // preserves __proto__ property names
        ("{abc:1,def:2}", "{\"abc\":1,\"def\":2}"), // parses multiple properties
        ("{a:{b:2}}", "{\"a\":{\"b\":2}}"), // parses nested objects

        // --- Arrays ---
        ("[]", "[]"), // parses empty arrays
        ("[1]", "[1]"), // parses array values
        ("[1,2]", "[1,2]"), // parses multiple array values
        ("[1,[2,3]]", "[1,[2,3]]"), // parses nested arrays

        // --- Nulls ---
        ("null", "null"), // parses nulls

        // --- Booleans ---
        ("true", "true"), // parses true
        ("false", "false"), // parses false

        // --- Numbers ---
        ("[0,0.,0e0]", "[0,0,0]"), // parses leading zeroes
        ("[1,23,456,7890]", "[1,23,456,7890]"), // parses integers
        ("[-1,+2,-.1,-0]", "[-1,2,-0.1,-0]"), // parses signed numbers (Note: -0 usually serializes to 0)
        ("[.1,.23]", "[0.1,0.23]"), // parses leading decimal points
        ("[1.0,1.23]", "[1,1.23]"), // parses fractional numbers
        ("[1e0,1e1,1e01,1.e0,1.1e0,1e-1,1e+1]", "[1,10,10,1,1.1,0.1,10]"), // parses exponents
        ("[0x1,0x10,0xff,0xFF]",
            "[1,16,255,255]"), // parses hexadecimal numbers -> converted to decimal for standard JSON
        ("[Infinity,-Infinity]",
            "[\"Infinity\",\"-Infinity\"]"), // parses signed and unsigned Infinity (Note: Not standard JSON, handled by some parsers)
        ("NaN", "\"NaN\""), // parses NaN (Note: Not standard JSON)
        ("-NaN", "\"NaN\""), // parses signed NaN (treated as NaN)
        ("1", "1"), // parses 1
        ("+1.23e100", "1.23E+100"), // parses +1.23e100
        ("0x1", "1"), // parses bare hexadecimal number
        ("-0x0123456789abcdefABCDEF",
            "-1375488932539311409843695"), // parses bare long hexadecimal number (Decimal value of input)
        // --- Strings ---
        ("\"abc\"", "\"abc\""), // parses double quoted strings
        ("'abc'", "\"abc\""), // parses single quoted strings
        ("['\"',\"'\"]", "[\"\\\"\",\"'\"]"), // parses quotes in strings
        // 下記はエスケープ文字のテスト。C#のリテラルとして正しく表現するために@を使用せずエスケープしています
        ("'\\b\\f\\n\\r\\t\\v\\0\\x0f\\u01fF\\\n\\\r\n\\\r\\\u2028\\\u2029\\a\\'\\\"'",
            "\"\\b\\f\\n\\r\\t\\u000b\\u0000\\u000fǿa'\\\"\""), // parses escaped characters
        ("'\u2028\u2029'", "\"\u2028\u2029\""), // parses line and paragraph separators

        // --- Comments ---
        ("{//comment\n}", "{}"), // parses single-line comments
        ("{}//comment", "{}"), // parses single-line comments at end of input
        ("{/*comment\n** */}", "{}"), // parses multi-line comments

        // --- Whitespace ---
        ("{\t\v\f \u00A0\uFEFF\n\r\u2028\u2029\u2003}", "{}"), // parses whitespace

        // ex
        ("true//comment", "true"),
        ("null//comment", "null"),
        ("123//comment", "123"),
        // ex2
        ("[1,]", "[1]"), // Array trailing comma
        ("[1,2,]", "[1,2]"),
        ("{\"a\":1,}", "{\"a\":1}"), // Object trailing comma   

        // --- Objects: Keywords as Keys ---
        // JSON5/ES5では予約語(reserved words)もクォートなしでキーにできます
        ("{null:null, true:true, false:false}", "{\"null\":null,\"true\":true,\"false\":false}"),
        ("{new:1, try:2, catch:3, function:4}", "{\"new\":1,\"try\":2,\"catch\":3,\"function\":4}"),
        ("{Infinity:1, NaN:2, undefined:3}", "{\"Infinity\":1,\"NaN\":2,\"undefined\":3}"), // これらは値ではなく識別子として扱われる

        // --- Numbers: Edge Cases ---
        // 末尾のドット (Valid in JSON5, Invalid in JSON)
        ("[1., 0., +1.]", "[1,0,1]"),
        // 小数点直後の指数 (1.e2 は 100)
        ("[1.e2, .5e2]", "[100,50]"),
        // 明示的なプラス符号付き整数
        ("[+1, +0]", "[1,0]"),

        // --- Whitespace: Vertical Tab ---
        // \v (0x0B) はJSON5では有効な空白文字
        ("{\v\"a\":\v1\v}", "{\"a\":1}")
    };


    [Theory]
    [InlineData("\"\"")] // 空文字
    [InlineData("\"abc\"")] // 通常文字
    [InlineData("\"a\\\\b\"")] // バックスラッシュ
    [InlineData("\"\\\"\"")] // ダブルクォート
    [InlineData("\"\\n\\r\\t\"")] // 制御文字
    [InlineData("\"\\b\\f\\v\"")] // JSON5拡張エスケープ
    [InlineData("\"\\0\"")] // null 文字    
    [InlineData("\"line1\\\nline2\"")] // LF 行継続
    [InlineData("\"line1\\\rline2\"")] // CR 行継続
    [InlineData("\"line1\\\r\nline2\"")] // CRLF 行継続
    [InlineData("\"line1\\\u2028line2\"")] // LS 行継続
    [InlineData("\"line1\\\u2029line2\"")] // PS 行継続
    [InlineData("\"\\x00\"")]
    [InlineData("\"\\x7F\"")]
    [InlineData("\"\\x80\"")]
    [InlineData("\"\\xFF\"")]
    [InlineData("\"\\u0000\"")]
    [InlineData("\"\\u0041\"")] // 'A'
    [InlineData("\"\\u00E9\"")] // é
    [InlineData("\"\\uD7FF\"")] // サロゲート直前
    [InlineData("\"\\uE000\"")] // サロゲート直後
    [InlineData("\"\\uD83D\\uDE00\"")] // 😀
    [InlineData("\"\\uDBFF\\uDFFF\"")] // 最大サロゲートペア    
    [InlineData("\"\\a\"")] // JSON5では有効（そのまま 'a'）
    [InlineData("\"\\q\"")] // JSON5では有効
    [InlineData("\"\\$\"")] // JSON5では有効
    [InlineData("\"\u2028\"")]
    [InlineData("\"\u2029\"")]
    [InlineData(
        "{\u0020\u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u202F\u205F\u3000}")]
    public void REDoxJson5Parse(string json)
    {
        var doc = Json5Document.Parse(json, s_settings,
            new Json5DocumentOptions { EnableValueValidation = true, PreserveTrivia = true });

        Assert.Equal(
            Json5Document.EncodeToString(doc.RootElement, new Json5WriteOptions { PreserveTrivia = true }),
            json);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"\\x\"")] // 桁不足
    [InlineData("\"\\x0\"")]
    [InlineData("\"\\xGG\"")] // 非16進
    [InlineData("\"\\u\"")] // 桁不足
    [InlineData("\"\\u123\"")]
    [InlineData("\"\\uZZZZ\"")] // 非16進
    [InlineData("\"")] // 閉じクォートなし
    [InlineData("\"abc")]
    [InlineData("\"\\")] // エスケープ未完
    [InlineData("\"\n\"")] // 生の改行
    [InlineData("\"\r\"")]
    [InlineData("\"\\uD83D\"")] // 上位サロゲート単独
    [InlineData("\"\\uDE00\"")] // 下位サロゲート単独
    [InlineData("\"\\uD83Dabc\"")] // 上位サロゲート未完
    [InlineData("\"\\uD83D\\u0041\"")] // 上位＋非下位
    [InlineData("\"\\1\"")] // 8進数エスケープは禁止 (JSON5仕様)
    [InlineData("\"\\7\"")] // 同上
    [InlineData("\"\\9\"")] // 8進数でなくても、\+数字(0以外)は禁止
    [InlineData("\"\\01\"")] // \0 はOKだが、直後に数字があるため無効
    [InlineData("\"\\08\"")] // 8は8進数ではないが、数字が続くこと自体が禁止
    [InlineData("\"abc'")] // ダブルで開始、シングルで終了
    [InlineData("'abc\"")] // シングルで開始、ダブルで終了
    [InlineData("'abc")] // シングルクォートの閉じ忘れ
    [InlineData("\"\\x1G\"")] // 1桁目はOKだが2桁目が不正なHex
    [InlineData("\"\\u123G\"")] // 3桁目までOKだが4桁目が不正なUnicode
    // バックスラッシュの後にスペース(0x20)があり、その後に改行がある
    // → "Escaped space" + "Raw newline" となり、Raw newlineでエラーになるはず
    [InlineData("\"\\\u0020\n\"")]
    public void REDoxJson5InvalidParse(string json)
    {
        Assert.ThrowsAny<DocumentParseException>(() =>
        {
            using var doc = Json5Document.Parse(json, s_settings,
                new Json5DocumentOptions { EnableValueValidation = true });
        });
    }

    [Fact]
    public void EncodeToStringTest()
    {
        var arr = DValue.From(new[] { 1.0, 2.5, 3.3 }, SerializerSettings.Default);

        Assert.Equal("[1,2.5,3.3]", Json5Document.EncodeToString(arr));
    }

    [Theory]
    [InlineData("[1,,2]")] // Sparse arrays (疎な配列) はJSON5でも禁止 (例: [1, , 2])
    [InlineData("[,1]")] // 先頭のカンマは禁止
    [InlineData("[,]")] // 要素なしのカンマのみは禁止
    [InlineData("{,}")] // オブジェクトのキーなしカンマは禁止
    [InlineData("{a:1,,b:2}")] // ダブルカンマは禁止
    [InlineData("[01]")] // '0'以外の数字で始まる先行ゼロ(Octal)は禁止 (0xはOK, 0.はOK)
    [InlineData("[08]")] // 8進数でなくても先行ゼロは禁止
    [InlineData("[1.e]")] // 指数部の数字欠損
    [InlineData("[.e1]")] // 小数点前の数字がなく、かつ小数点後もない
    [InlineData("[0x]")] // 16進数の桁欠損
    [InlineData("[0xG]")] // 不正な16進文字
    [InlineData("[+ +-1]")] // 符号の重複
    [InlineData("{1a:1}")] // 識別子は数字から開始できない
    [InlineData("{-a:1}")] // 識別子は演算子から開始できない
    [InlineData("[True]")] // 大文字小文字の区別 (JSON5の値は小文字のみ)
    [InlineData("[Null]")] // 同上
    [InlineData("[infinity]")] // 同上 (Infinityは先頭大文字)
    [InlineData("[-infinity]")] // 同上 (Infinityは先頭大文字)
    [InlineData("[+infinity]")] // 同上 (Infinityは先頭大文字)
    [InlineData("[INFINITY]")] // 同上 (Infinityは先頭大文字)
    [InlineData("[-INFINITY]")] // 同上 (Infinityは先頭大文字)
    [InlineData("[+INFINITY]")] // 同上 (Infinityは先頭大文字)
    [InlineData("[nan]")] // 同上 (Infinityは先頭大文字)
    [InlineData("[NAN]")] // 同上 (Infinityは先頭大文字)
    [InlineData("/* unclosed")] // 閉じられていないブロックコメント
    public void REDoxJson5StructureInvalid(string json)
    {
        Assert.ThrowsAny<DocumentParseException>(() =>
        {
            using var doc = Json5Document.Parse(json, s_settings,
                new Json5DocumentOptions { EnableValueValidation = true });
        });
    }

    [Fact]
    public void Json5Parse_WithBOM()
    {
        // UTF-8 BOM + "{}"
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, 0x7B, 0x7D };

        // バイト配列からのパースでBOMによりクラッシュしたりエラーにならないか
        using var doc = Json5Document.Parse(bytes, s_settings);
        Assert.Equal("{}", doc.RootElement.ToJsonString());
    }

    [Fact]
    public void DeeplyNestedJson5_ShouldHandleGracefully()
    {
        // 64階層を超えるネストを作成
        var depth = 100;
        var sb = new StringBuilder();
        for (var i = 0; i < depth; i++)
        {
            sb.Append('[');
        }

        for (var i = 0; i < depth; i++)
        {
            sb.Append(']');
        }

        var json = sb.ToString();

        Assert.ThrowsAny<Exception>(() =>
        {
            using var doc = Json5Document.Parse(json, s_settings);
        });

        try
        {
            using var doc = Json5Document.Parse(json, s_settings, new Json5DocumentOptions
            {
                MaxDepth = depth
            });
        }
        catch (DocumentParseException)
        {
        }
    }

    public static IEnumerable<object[]> GetJson5TestData()
    {
        // 元のリストを object[] の列挙に変換
        foreach (var (input, expected) in TestData)
        {
            yield return new object[] { input, expected };
        }
    }

    [Theory]
    [MemberData(nameof(GetJson5TestData))]
    public void Json5Parse(string input, string expected)
    {
        using var doc = Json5Document.Parse(input, s_settings,
            new Json5DocumentOptions { EnableValueValidation = true });

        var src = expected;
        var tgt = doc.RootElement.ToJsonString();

        Assert.Equal(src, tgt);
    }

    [Theory]
    [MemberData(nameof(GetJson5TestData))]
    public void Json5TryParse(string input, string expected)
    {
        if (Json5Document.TryParse(Encoding.UTF8.GetBytes(input), out var doc, s_settings,
                new Json5DocumentOptions { EnableValueValidation = true }) && doc != null)
        {
            Assert.True(doc.IsValid);

            var src = expected;
            var tgt = doc.RootElement.ToJsonString();

            Assert.Equal(src, tgt);

            doc.Dispose();
        }
        else
        {
            Assert.Fail();
        }
    }

    [Theory]
    [MemberData(nameof(GetJson5TestData))]
    public void Json5Write(string input, string expected)
    {
        using var doc = Json5Document.Parse(input, s_settings,
            new Json5DocumentOptions { PreserveTrivia = true, EnableValueValidation = true });

        Assert.True(doc.IsValid);

        var src = input;
        var tgt = Json5Document.EncodeToString(doc.RootElement,
            new Json5WriteOptions { PreserveTrivia = true });

        if (src != tgt)
        {
            var tokens = doc.GetTokens();

            Console.WriteLine(tokens.Length);
        }

        Assert.Equal(src, tgt);
        Assert.NotNull(expected);
    }

    [Theory]
    [MemberData(nameof(GetParseFiles), "*.json")]
    public void Json5StandardParse(string filePath)
    {
        if (!filePath.Contains("npm"))
        {
            return;
        }

        var bytes = File.ReadAllBytes(filePath);

        try
        {
            using var src = Json5Document.Parse(bytes, s_settings,
                new Json5DocumentOptions { EnableValueValidation = true });

            Assert.True(src.IsValid);

            using var tgt = JsonDocument.Parse(bytes, s_settings,
                new JsonDocumentOptions { EnableValueValidation = true });

            Assert.True(tgt.IsValid);


            var tokens = src.GetTokens();


            var srcJson = src.RootElement.ToJsonString();
            var tgtJson = tgt.RootElement.ToJsonString();

            Assert.Equal(srcJson, tgtJson);
        }
        catch (Exception e)
        {
            Assert.Fail(e.ToString());
        }
    }


    [Theory]
    [MemberData(nameof(GetParseFiles), "*.json")]
    public void Json5StandardWrite(string filePath)
    {
        if (!filePath.Contains("npm"))
        {
            return;
        }

        var bytes = File.ReadAllBytes(filePath);

        try
        {
            using var src = Json5Document.Parse(bytes, s_settings,
                new Json5DocumentOptions { EnableValueValidation = true, PreserveTrivia = true });

            Assert.True(src.IsValid);

            var srcJson = Json5Document.EncodeToString(src.RootElement,
                new Json5WriteOptions { PreserveTrivia = true });
            var tgtJson = Encoding.UTF8.GetString(bytes);

            Assert.Equal(srcJson, tgtJson);
        }
        catch (Exception e)
        {
            Assert.Fail(e.ToString());
        }
    }

    [Theory]
    [MemberData(nameof(GetParseFiles), "*.js")]
    public void JsonAbnormalParse(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var success = false;

        var text = Encoding.UTF8.GetString(bytes);

        try
        {
            using var src = Json5Document.Parse(bytes, s_settings,
                new Json5DocumentOptions { EnableValueValidation = true });

            Assert.True(src.IsValid);

            success = true;
        }
        catch (Exception)
        {
        }

        if (success)
        {
            Assert.Fail(Encoding.UTF8.GetString(bytes));
        }

        if (Json5Document.TryParse(bytes, out var doc, s_settings,
                new Json5DocumentOptions { EnableValueValidation = true }))
        {
            Assert.Fail(Encoding.UTF8.GetString(bytes));

            doc.Dispose();
        }
    }

    [Theory]
    [MemberData(nameof(GetParseFiles), "*.txt")]
    public void JsonAbnormal2Parse(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var success = false;

        try
        {
            using var src = Json5Document.Parse(bytes, s_settings,
                new Json5DocumentOptions { EnableValueValidation = true });

            Assert.True(src.IsValid);

            success = true;
        }
        catch (Exception)
        {
        }

        if (success)
        {
            Assert.Fail(Path.GetFileName(filePath) + "@" + Encoding.UTF8.GetString(bytes));
        }

        if (Json5Document.TryParse(bytes, out var doc, s_settings,
                new Json5DocumentOptions { EnableValueValidation = true }))
        {
            Assert.Fail(Encoding.UTF8.GetString(bytes));
        }
    }

    [Theory]
    [MemberData(nameof(GetParseFiles), "*.json5")]
    public void Json5NormalParse(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);

        try
        {
            using var src = Json5Document.Parse(bytes,
                s_settings, new Json5DocumentOptions { EnableValueValidation = true });

            Assert.True(src.IsValid);
        }
        catch (Exception e)
        {
            Assert.Fail(e.ToString());
        }
    }

    [Theory]
    [MemberData(nameof(GetParseFiles), "*.json5")]
    public void Json5NormalWrite(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);

        try
        {
            using var src = Json5Document.Parse(bytes,
                s_settings, new Json5DocumentOptions { EnableValueValidation = true, PreserveTrivia = true });

            Assert.True(src.IsValid);

            var srcJson = Json5Document.EncodeToString(src.RootElement,
                new Json5WriteOptions { PreserveTrivia = true });
            var tgtJson = Encoding.UTF8.GetString(bytes);

            Assert.Equal(srcJson, tgtJson);
        }
        catch (Exception e)
        {
            Assert.Fail(e.ToString());
        }
    }

    [Fact]
    public void Json5ModifyWrite()
    {
        //readme-example.json5
        var path = Path.Combine(ParsingDir, "misc", "readme-example.json5");
        var bytes = File.ReadAllBytes(path);

        using var doc = Json5Document.Parse(bytes, s_settings, new Json5DocumentOptions { PreserveTrivia = true });

        Assert.True(doc.IsValid);

        var root = doc.RootElement.AsObject();
        root["hex"] = 128;
        root["foo"] = "my name";
        root["while"] = false;
        root["this"] = DValue.Create("replaced string", StringKind.MultilineDoubleQuote);

        var here = root.GetPropertyEntry("here");

        foreach (var trivia in here.LeadingTrivia)
        {
            if (trivia.Kind == TriviaKind.LineComment)
            {
                Console.WriteLine(trivia.GetString());
            }
        }

        root["here"] = "with comment";

        foreach (var trivia in root["here"].EnumerateTrivia())
        {
            if (trivia.Kind == TriviaKind.LineComment)
            {
                Console.WriteLine(trivia.GetString());
            }
        }

        var dest = Json5Document.EncodeToString(doc.RootElement, new Json5WriteOptions
        {
            PreserveTrivia = true,
            PropertyNameStyle = Json5QuoteStyle.PreserveOrDouble,
            StringStyle = Json5QuoteStyle.PreserveOrDouble
        });

        Console.WriteLine(dest);

        var json = doc.RootElement.ToJsonString();

        Console.WriteLine(json);
    }

    public static IEnumerable<object[]> GetParseFiles(string filter)
    {
        return Directory.EnumerateFiles(ParsingDir, filter, SearchOption.AllDirectories)
            .Select(path => new object[] { path });
    }

    private static string FindTestSuiteDirectory(string subDirectory)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var path = Path.Combine(
                current.FullName,
                "external",
                "json5-tests",
                subDirectory);

            if (Directory.Exists(path))
            {
                return path;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"JSONTestSuite directory not found: {subDirectory}");
    }
}