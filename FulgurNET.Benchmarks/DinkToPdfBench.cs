// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Benchmark for DinkToPdf - the wkhtmltopdf wrapper that has been
// the "default" .NET HTML-to-PDF library for a decade. Comparing
// against it is mandatory for any serious competitor.

using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using DinkToPdf;
using FulgurNET.Benchmarks.Common;

namespace FulgurNET.Benchmarks;

[MemoryDiagnoser]
[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class DinkToPdfBench
{
    private string _html = null!;
    private SynchronizedConverter _converter = null!;
    private int _lastOutputSize;

    [ParamsSource(nameof(DocumentSizes))]
    public DocumentSize Size { get; set; }

    public IEnumerable<DocumentSize> DocumentSizes() => new[]
    {
        DocumentSize.Small,
        DocumentSize.Medium,
        DocumentSize.Large,
    };

    [GlobalSetup]
    public void Setup()
    {
        _html = Size switch
        {
            DocumentSize.Small  => HtmlFixtures.Small,
            DocumentSize.Medium => HtmlFixtures.Medium,
            DocumentSize.Large  => HtmlFixtures.BuildLarge(200),
            _ => throw new ArgumentOutOfRangeException(),
        };

        // wkhtmltopdf's native binary must be findable. DinkToPdf
        // P/Invokes "libwkhtmltox.dll" by name (Unix-style). On Windows
        // the wkhtmltopdf installer drops it as "wkhtmltox.dll" instead.
        // We copy it next to the running assembly as "libwkhtmltox.dll"
        // so the runtime resolver finds it. Idempotent.
        EnsureLibWkhtmltoxPresent();

        _converter = new SynchronizedConverter(new PdfTools());

        var warm = RenderOnce();
        _lastOutputSize = warm.Length;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
    }

    private byte[] RenderOnce()
    {
        var doc = new HtmlToPdfDocument
        {
            GlobalSettings = new GlobalSettings
            {
                PaperSize = PaperKind.A4,
                Orientation = Orientation.Portrait,
            },
            Objects =
            {
                new ObjectSettings
                {
                    HtmlContent = _html,
                    WebSettings = { DefaultEncoding = "utf-8", PrintMediaType = true },
                },
            },
        };
        return _converter.Convert(doc);
    }

    [Benchmark(Description = "DinkToPdf (wkhtmltopdf) cold (fresh document per call)")]
    [BenchmarkCategory("Cold", "DinkToPdf")]
    public byte[] Cold()
    {
        var bytes = RenderOnce();
        _lastOutputSize = bytes.Length;
        return bytes;
    }

    [Benchmark(Description = "DinkToPdf (wkhtmltopdf) warm (reused converter)")]
    [BenchmarkCategory("Warm", "DinkToPdf")]
    public byte[] Warm()
    {
        var bytes = RenderOnce();
        _lastOutputSize = bytes.Length;
        return bytes;
    }

    public int LastOutputSize => _lastOutputSize;

    /// <summary>
    /// Locates wkhtmltopdf's native shared library and copies it to the
    /// running assembly's directory under the name DinkToPdf actually
    /// P/Invokes (<c>libwkhtmltox.dll</c>). On Windows the wkhtmltopdf
    /// installer drops the file as <c>wkhtmltox.dll</c> (no "lib"
    /// prefix), so we bridge that gap. Idempotent.
    /// </summary>
    private static void EnsureLibWkhtmltoxPresent()
    {
        var dest = Path.Combine(AppContext.BaseDirectory, "libwkhtmltox.dll");
        if (File.Exists(dest))
        {
            return; // already staged (e.g. by the StageLibWkhtmltox MSBuild target)
        }

        string[] candidates = OperatingSystem.IsWindows()
            ? new[]
            {
                @"C:\wkhtmltopdf\wkhtmltox.dll",
                @"C:\Program Files\wkhtmltopdf\bin\wkhtmltox.dll",
                @"C:\Program Files (x86)\wkhtmltopdf\bin\wkhtmltox.dll",
            }
            : new[]
            {
                "/usr/local/lib/libwkhtmltox.so",
                "/usr/lib/libwkhtmltox.so",
                "/opt/wkhtmltopdf/lib/libwkhtmltox.dylib",
            };

        foreach (var src in candidates)
        {
            if (File.Exists(src))
            {
                File.Copy(src, dest, overwrite: true);
                return;
            }
        }

        throw new FileNotFoundException(
            "libwkhtmltox was not found in any of the standard install locations. " +
            "Install wkhtmltopdf 0.12.6 (Qt-patched build) from https://wkhtmltopdf.org/.");
    }
}
