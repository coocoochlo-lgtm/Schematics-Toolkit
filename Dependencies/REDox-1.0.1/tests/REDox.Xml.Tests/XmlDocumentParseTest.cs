using System;
using System.IO;
using System.Text;

namespace REDox.Xml.Tests;

public class XmlDocumentParseTest
{
    [Fact]
    public void ParseOverloadsPreserveSource()
    {
        var bytes = Encoding.UTF8.GetBytes("<root><child>value</child></root>");

        using var spanDoc = XmlDocument.Parse(bytes.AsSpan());
        Assert.True(spanDoc.Source.Span.SequenceEqual(bytes));

        using var stream = new MemoryStream(bytes);
        using var streamDoc = XmlDocument.Parse(stream);
        Assert.True(streamDoc.Source.Span.SequenceEqual(bytes));
    }
}