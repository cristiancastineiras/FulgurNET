// SPDX-License-Identifier: MIT OR Apache-2.0
//
// End-to-end smoke tests for the FulgurNET .NET surface. Every test
// here crosses the FFI boundary into `fulgur_native.dll` and round-trips
// a PDF; they double as a regression suite for both the C# wrapper
// and the FFI surface itself.

using System;
using System.Linq;
using System.Text;
using FulgurNET;
using Xunit;

namespace FulgurNET.Tests;

public class FulgurPdfTests
{
    private const string PdfMagic = "%PDF-";

    [Fact]
    public void FromHtml_BasicDocument_ProducesValidPdf()
    {
        var html = "<html><body><h1>Hello Fulgur</h1><p>Smoke test.</p></body></html>";
        var bytes = FulgurPdf.FromHtml(html);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        // PDF header: every conforming file starts with %PDF-1.x
        var header = Encoding.ASCII.GetString(bytes, 0, 5);
        Assert.Equal(PdfMagic, header);
    }

    [Fact]
    public void FromHtml_EmptyString_ProducesValidPdf()
    {
        var bytes = FulgurPdf.FromHtml(string.Empty);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        Assert.StartsWith(PdfMagic, Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void FromHtml_HtmlWithUnicode_ProducesValidPdf()
    {
        // UTF-8 path: emoji + accented Latin + CJK. If the C# wrapper
        // ever drops to ASCII encoding the engine will reject the
        // input and the test will fail in NativeMethods.
        var html = "<h1>Árbol 🌳 木</h1>";
        var bytes = FulgurPdf.FromHtml(html);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        Assert.StartsWith(PdfMagic, Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void FromHtml_ComplexDocument_RespectsParagraphSplitting()
    {
        // Force multiple pages to exercise the layout pipeline.
        var sb = new StringBuilder();
        sb.Append("<html><body>");
        for (int i = 0; i < 200; i++)
        {
            sb.Append($"<p>Paragraph {i}: lorem ipsum dolor sit amet.</p>");
        }
        sb.Append("</body></html>");

        var bytes = FulgurPdf.FromHtml(sb.ToString());

        Assert.NotEmpty(bytes);
        // A 200-paragraph document is well over one page; the PDF
        // page-tree is identifiable by the `/Type /Pages` object
        // count. We don't parse the PDF (that's a full parser),
        // but we do expect the file to be at least a few KB.
        Assert.True(bytes.Length > 5_000, $"PDF too small: {bytes.Length} bytes");
    }

    [Fact]
    public void FromHtml_NullHtml_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => FulgurPdf.FromHtml(html: null!));
    }

    [Fact]
    public void FromHtml_MalformedHtml_DoesNotCrash()
    {
        // Fulgur's HTML parser is deliberately lenient — unclosed
        // tags, missing close quotes, etc. don't raise. The contract
        // is "we always return *some* PDF, even if degraded". This
        // test pins that behavior so a future parser change doesn't
        // silently start throwing on common sloppy input.
        var malformed = "<div><span>oops</span"; // unclosed span
        var bytes = FulgurPdf.FromHtml(malformed);
        Assert.NotEmpty(bytes);
        Assert.StartsWith(PdfMagic, Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [Fact]
    public void FromHtml_ConcurrentCalls_AllSucceed()
    {
        // Each call constructs its own native engine, so concurrent
        // invocations should be independent. If the wrapper had a
        // hidden shared state this would race / fail.
        const int N = 8;
        var tasks = new System.Threading.Tasks.Task<byte[]>[N];
        for (int i = 0; i < N; i++)
        {
            int idx = i;
            tasks[i] = System.Threading.Tasks.Task.Run(() =>
                FulgurPdf.FromHtml($"<p>Task {idx}</p>"));
        }
        System.Threading.Tasks.Task.WaitAll(tasks);

        foreach (var (bytes, i) in tasks.Select((b, i) => (b.Result, i)))
        {
            Assert.NotEmpty(bytes);
            Assert.StartsWith(PdfMagic, Encoding.ASCII.GetString(bytes, 0, 5));
        }
    }

    [Fact]
    public void NativeVersion_IsNonEmpty()
    {
        var v = FulgurPdf.NativeVersion;
        Assert.False(string.IsNullOrWhiteSpace(v),
            "fulgur_version should report a semver string");
        Assert.Contains('.', v); // x.y.z
    }

    [Fact]
    public void FromHtmlToFile_WritesValidFile()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"fulgurnet-test-{Guid.NewGuid():N}.pdf");
        try
        {
            FulgurPdf.FromHtmlToFile("<h1>file test</h1>", path);
            Assert.True(System.IO.File.Exists(path));
            var bytes = System.IO.File.ReadAllBytes(path);
            Assert.NotEmpty(bytes);
            Assert.StartsWith(PdfMagic, Encoding.ASCII.GetString(bytes, 0, 5));
        }
        finally
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
    }
}
