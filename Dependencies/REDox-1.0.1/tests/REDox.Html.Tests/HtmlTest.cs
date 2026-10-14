using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace REDox.Html.Tests;

public class HtmlTest
{
    private const string s_graphHtml = """
                                       <!DOCTYPE html>
                                       <html lang="ja">
                                       <head>
                                         <meta charset="UTF-8">
                                         <title>横棒グラフ（Chart.js）</title>
                                         <script src="https://cdn.jsdelivr.net/npm/chart.js"></script>
                                       </head>
                                       <body>
                                         <canvas id="myChart"></canvas>

                                         <script>
                                           const ctx = document.getElementById('myChart');

                                           new Chart(ctx, {
                                             type: 'bar',
                                             data: {
                                               labels: ['A', 'B', 'C'],
                                               datasets: [{
                                                 label: 'サンプル',
                                                 data: [10, 20, 15],
                                                 backgroundColor: 'rgba(54, 162, 235, 0.5)'
                                               }]
                                             },
                                             options: {
                                               indexAxis: 'y'
                                             }
                                           });
                                         </script>
                                       </body>
                                       </html>           
                                       """;

    private const string s_html5 = """
                                   <!DOCTYPE html>
                                   <html lang="ja">
                                   <head>
                                     <meta charset="utf-8">
                                     <title>HTML5 fixture</title>
                                   </head>
                                   <body>
                                     <main id=app hidden>
                                       <article data-id="42">
                                         <header><h1>HTML5</h1></header>
                                         <p>First paragraph
                                         <p>Second &amp; final</p>
                                         <figure>
                                           <img src="chart.png" alt="chart">
                                           <figcaption>Chart &copy; 2026</figcaption>
                                         </figure>
                                         <video controls>
                                           <source src="movie.webm" type="video/webm">
                                         </video>
                                         <template><section>inside template</section></template>
                                         <script>const fragment = "<article>"; if (fragment.length > 0) { console.log(fragment); }</script>
                                       </article>
                                     </main>
                                   </body>
                                   </html>
                                   """;

    private readonly ITestOutputHelper _output;

    public HtmlTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Parse()
    {
        using var doc = HtmlDocument.Parse(s_graphHtml, SerializerSettings.Default);

        var html = HtmlDocument.EncodeToString(doc.RootElement);

        _output.WriteLine(html);
    }

    [Fact]
    public void ParseOverloadsPreserveSource()
    {
        var bytes = Encoding.UTF8.GetBytes("<main><p>Hello</p></main>");

        using var spanDoc = HtmlDocument.Parse(bytes.AsSpan());
        Assert.True(spanDoc.Source.Span.SequenceEqual(bytes));

        using var stream = new MemoryStream(bytes);
        using var streamDoc = HtmlDocument.Parse(stream);
        Assert.True(streamDoc.Source.Span.SequenceEqual(bytes));
    }

    [Fact]
    public void ParseHtml5()
    {
        using var doc = HtmlDocument.Parse(s_html5, SerializerSettings.Default);

        Assert.True(doc.IsValid);

        var doctype = GetDirective(doc.RootElement, "DOCTYPE");
        Assert.Equal("html", GetArrayItem(doctype, 1).GetString());

        var html = GetFirstElement(doc.RootElement, "html");
        Assert.Equal("ja", html.GetProperty("lang").GetString());

        var head = GetFirstDescendantElement(html, "head");
        var meta = GetFirstDescendantElement(head, "meta");
        Assert.Equal("utf-8", meta.GetProperty("charset").GetString());
        Assert.Equal(0, meta.GetProperty("meta").GetArrayLength());

        var main = GetFirstDescendantElement(html, "main");
        Assert.Equal("app", main.GetProperty("id").GetString());
        Assert.Null(main.GetProperty("hidden").GetString());

        var article = GetFirstDescendantElement(main, "article");
        Assert.Equal("42", article.GetProperty("data-id").GetString());

        var paragraphs = GetChildElements(article.GetProperty("article"), "p");
        Assert.Equal(2, paragraphs.Count);
        Assert.Equal("First paragraph", GetTextContent(paragraphs[0].GetProperty("p")).Trim());
//        Assert.Equal("Second & final", GetTextContent(paragraphs[1].GetProperty("p")).Trim());

        var figure = GetFirstDescendantElement(article, "figure");
        var image = GetFirstDescendantElement(figure, "img");
        Assert.Equal("chart.png", image.GetProperty("src").GetString());
        Assert.Equal("chart", image.GetProperty("alt").GetString());
        Assert.Equal(0, image.GetProperty("img").GetArrayLength());
//        Assert.Equal("Chart © 2026", GetTextContent(GetFirstDescendantElement(figure, "figcaption").GetProperty("figcaption")).Trim());

        var video = GetFirstDescendantElement(article, "video");
        Assert.Null(video.GetProperty("controls").GetString());
        var source = GetFirstDescendantElement(video, "source");
        Assert.Equal("movie.webm", source.GetProperty("src").GetString());
        Assert.Equal("video/webm", source.GetProperty("type").GetString());
        Assert.Equal(0, source.GetProperty("source").GetArrayLength());

        var template = GetFirstDescendantElement(article, "template");
        var section = GetFirstDescendantElement(template, "section");
        Assert.Equal("inside template", GetTextContent(section.GetProperty("section")).Trim());

        var script = GetFirstDescendantElement(article, "script");
        Assert.Contains("""const fragment = "<article>";""", GetTextContent(script.GetProperty("script")));

        /*
        var encoded = doc.EncodeToString();
        Assert.Contains("""<meta charset="utf-8">""", encoded);
        Assert.Contains("""<img src="chart.png" alt="chart">""", encoded);
        Assert.Contains("""<source src="movie.webm" type="video/webm">""", encoded);
        Assert.DoesNotContain("</meta>", encoded);
        Assert.DoesNotContain("</img>", encoded);
        Assert.DoesNotContain("</source>", encoded);
        Assert.Contains("""const fragment = "<article>";""", encoded);
        */
    }

    private static DElement GetDirective(DElement container, string name)
    {
        foreach (var child in container.EnumerateArray())
        {
            if (child.Token.Type != DTokenType.Array || child.GetArrayLength() == 0)
            {
                continue;
            }

            var directiveName = GetArrayItem(child, 0).GetString();
            if (string.Equals(directiveName, name, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        throw new KeyNotFoundException(name);
    }

    private static DElement GetFirstDescendantElement(DElement element, string tagName)
    {
        if (TryGetFirstDescendantElement(element, tagName, out var found))
        {
            return found;
        }

        throw new KeyNotFoundException(tagName);
    }

    private static bool TryGetFirstDescendantElement(DElement element, string tagName, out DElement found)
    {
        if (!element.IsValid)
        {
            found = default;
            return false;
        }

        if (TryGetElement(element, tagName, out found))
        {
            return true;
        }

        if (element.Token.Type == DTokenType.Map)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.Token.Type != DTokenType.Array)
                {
                    continue;
                }

                if (TryGetFirstDescendantElement(property.Value, tagName, out found))
                {
                    return true;
                }
            }
        }

        if (element.Token.Type == DTokenType.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                if (TryGetFirstDescendantElement(child, tagName, out found))
                {
                    return true;
                }
            }
        }

        found = default;
        return false;
    }

    private static DElement GetFirstElement(DElement container, string tagName)
    {
        foreach (var child in container.EnumerateArray())
        {
            if (TryGetElement(child, tagName, out var element))
            {
                return element;
            }
        }

        throw new KeyNotFoundException(tagName);
    }

    private static List<DElement> GetChildElements(DElement container, string tagName)
    {
        var elements = new List<DElement>();

        foreach (var child in container.EnumerateArray())
        {
            if (TryGetElement(child, tagName, out var element))
            {
                elements.Add(element);
            }
        }

        return elements;
    }

    private static bool TryGetElement(DElement element, string tagName, out DElement found)
    {
        if (element.IsValid &&
            element.Token.Type == DTokenType.Map &&
            element.TryGetProperty(tagName, out var children) &&
            children.Token.Type == DTokenType.Array)
        {
            found = element;
            return true;
        }

        found = default;
        return false;
    }

    private static DElement GetArrayItem(DElement array, int index)
    {
        var current = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (current == index)
            {
                return item;
            }

            current++;
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }

    private static string GetTextContent(DElement container)
    {
        var text = string.Empty;

        foreach (var child in container.EnumerateArray())
        {
            if (child.Token.Type == DTokenType.Text)
            {
                text += child.GetString();
            }
        }

        return text;
    }
}