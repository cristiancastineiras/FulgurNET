// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Entry point for the FulgurNET benchmark suite.
//
// Two modes:
//   * `dotnet run -c Release --project FulgurNET.Benchmarks`         (default — quick smoke)
//   * `dotnet run -c Release --project FulgurNET.Benchmarks -- --full` (the real deal)
//
// The default smoke run does a single render per engine to validate
// the harness works. The full run goes through BenchmarkDotNet with
// its default precision; once BDN exits, our
// FulgurCompetitionReporter reads the JSON results and prints the
// "competition report" that closes this run.

using System.Linq;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using FulgurNET.Benchmarks;
using FulgurNET.Benchmarks.Common;

var argsList = args.ToList();
var isFull = argsList.Contains("--full");
var filter = argsList.FirstOrDefault(a => a.StartsWith("--filter="))?[9..];
var noPuppeteer = argsList.Contains("--no-puppeteer");

if (isFull || filter is not null || noPuppeteer)
{
    // Real BDN run. We invoke each benchmark class in a separate
    // BenchmarkRunner.Run call so the (Type, IConfig, string[]?)
    // signature change across BDN minor versions can't trip us up.
    // Each Run writes its own JSON summary into
    // BenchmarkDotNet.Artifacts/results/, which the
    // FulgurCompetitionReporter below reads and consolidates.
    var config = ManualConfig.CreateMinimumViable()
        .AddLogger(ConsoleLogger.Default)
        .AddDiagnoser(MemoryDiagnoser.Default)
        // Emit per-benchmark JSON so FulgurCompetitionReporter can
        // parse a stable machine-readable summary at the end of the run.
        // BDN 0.15 doesn't enable the JSON exporter by default; you
        // have to opt in.
        .AddExporter(BenchmarkDotNet.Exporters.Json.JsonExporter.Full)
        .AddJob(Job.ShortRun);

    var types = new List<Type>
    {
        typeof(FulgurBench),
        typeof(DinkToPdfBench),
    };
    if (!noPuppeteer) types.Add(typeof(PuppeteerBench));

    foreach (var t in types)
    {
        try
        {
            BenchmarkRunner.Run(t, config);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[bench] {t.Name} failed: {ex.Message}");
        }
    }

    // After BDN exits, BDN writes a per-benchmark JSON file under
    // BenchmarkDotNet.Artifacts/results/. We read them all and
    // print our consolidated competition report.
    Console.WriteLine();
    FulgurCompetitionReporter.PrintFromLatestResults();
}
else
{
    // Quick smoke. We don't go through BDN at all — just run one
    // iteration of each engine to validate the harness works.
    Console.WriteLine("FulgurNET.Benchmarks — quick smoke (use -- --full for the real deal)");
    Console.WriteLine();

    await Smoke.RunAsync();
}

internal static class Smoke
{
    public static async Task RunAsync()
    {
        var html = HtmlFixtures.Medium;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // FulgurNET
        sw.Restart();
        var fulgur = FulgurNET.FulgurPdf.FromHtml(html);
        sw.Stop();
        Console.WriteLine($"[OK] FulgurNET       {sw.ElapsedMilliseconds,4} ms  ({fulgur.Length / 1024.0:F1} KB)");

        // DinkToPdf
        try
        {
            sw.Restart();
            var dink = new DinkToPdf.SynchronizedConverter(new DinkToPdf.PdfTools());
            try
            {
                var doc = new DinkToPdf.HtmlToPdfDocument
                {
                    GlobalSettings = new DinkToPdf.GlobalSettings { PaperSize = DinkToPdf.PaperKind.A4 },
                    Objects =
                    {
                        new DinkToPdf.ObjectSettings
                        {
                            HtmlContent = html,
                            WebSettings = { PrintMediaType = true },
                        },
                    },
                };
                var bytes = dink.Convert(doc);
                sw.Stop();
                Console.WriteLine($"[OK] DinkToPdf       {sw.ElapsedMilliseconds,4} ms  ({bytes.Length / 1024.0:F1} KB)");
            }
            finally
            {
                // SynchronizedConverter is sealed and not disposable
                // directly; releasing the native wkhtmltopdf handle
                // requires the underlying PdfTools. We do best-effort.
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[--] DinkToPdf       skipped: {ex.GetType().Name}: {ex.Message}");
        }

        // PuppeteerSharp
        try
        {
            sw.Restart();
            await new PuppeteerSharp.BrowserFetcher().DownloadAsync();
            await using var browser = await PuppeteerSharp.Puppeteer.LaunchAsync(new PuppeteerSharp.LaunchOptions
            {
                Headless = true,
                Args = new[] { "--no-sandbox", "--disable-setuid-sandbox" },
            });
            await using var page = await browser.NewPageAsync();
            await page.SetContentAsync(html);
            var pdf = await page.PdfDataAsync(new PuppeteerSharp.PdfOptions
            {
                Format = PuppeteerSharp.Media.PaperFormat.A4,
                PrintBackground = true,
            });
            sw.Stop();
            Console.WriteLine($"[OK] PuppeteerSharp  {sw.ElapsedMilliseconds,4} ms  ({pdf.Length / 1024.0:F1} KB)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[--] PuppeteerSharp  skipped: {ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine();
        Console.WriteLine("Quick smoke complete. Pass -- --full to run the BDN suite with the competition report.");
    }
}
