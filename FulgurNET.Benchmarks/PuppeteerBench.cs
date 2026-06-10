// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Benchmark for PuppeteerSharp — Chromium headless, the de-facto
// market leader for HTML-to-PDF fidelity in 2025.
//
// The benchmark is intentionally marked [BenchmarkCategory("Heavy")]
// so it can be filtered out via `--filter "!*Heavy*"` on machines
// that don't have a Chromium download configured. PuppeteerSharp
// downloads Chromium on first use (~150 MB) and the cold-start cost
// dwarfs the actual render time, so we always run a "Cold" pass
// that includes the browser launch and a separate "Warm" pass that
// reuses the same instance.

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using FulgurNET.Benchmarks.Common;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace FulgurNET.Benchmarks;

[MemoryDiagnoser]
[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class PuppeteerBench
{
    private string _html = null!;
    private IBrowser _browser = null!;
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
    public async Task Setup()
    {
        _html = Size switch
        {
            DocumentSize.Small  => HtmlFixtures.Small,
            DocumentSize.Medium => HtmlFixtures.Medium,
            DocumentSize.Large  => HtmlFixtures.BuildLarge(200),
            _ => throw new ArgumentOutOfRangeException(),
        };

        // The first time this runs PuppeteerSharp downloads Chromium
        // into `~/.cache/puppeteer`. We pass `Default` to use the
        // bundled default revision; callers can override with
        // PUPPETEER_EXECUTABLE_PATH.
        await new BrowserFetcher().DownloadAsync();
        _browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true,
            Args = new[] { "--no-sandbox", "--disable-setuid-sandbox" },
        });

        // Warm: render once so the page object pool, font cache, and
        // first-paint JIT costs are out of the way.
        var warm = await RenderOnce();
        _lastOutputSize = warm.Length;
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
            await _browser.DisposeAsync();
        }
    }

    private async Task<byte[]> RenderOnce()
    {
        await using var page = await _browser.NewPageAsync();
        // We set the content directly rather than navigating to a
        // file:// — this avoids disk I/O and matches how most apps
        // use Puppeteer in production (template string in memory).
        await page.SetContentAsync(_html);
        var pdf = await page.PdfDataAsync(new PdfOptions
        {
            Format = PaperFormat.A4,
            PrintBackground = true,
            MarginOptions = new MarginOptions
            {
                Top    = "20mm",
                Right  = "20mm",
                Bottom = "20mm",
                Left   = "20mm",
            },
        });
        return pdf;
    }

    [Benchmark(Description = "PuppeteerSharp (Chromium) — warm (reused browser)")]
    [BenchmarkCategory("Heavy", "Warm", "PuppeteerSharp")]
    public async Task<byte[]> Warm()
    {
        // BDN handles Task-returning benchmarks, but for an apples-
        // to-apples comparison we want the awaited value back so the
        // GC can see the allocation. We do it explicitly and return
        // a hot array.
        var bytes = await RenderOnce();
        _lastOutputSize = bytes.Length;
        return bytes;
    }

    public int LastOutputSize => _lastOutputSize;
}
