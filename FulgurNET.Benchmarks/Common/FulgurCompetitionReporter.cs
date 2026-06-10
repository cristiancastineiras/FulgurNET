// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Custom "competition report" printer for FulgurNET benchmarks.
//
// We don't subclass BenchmarkDotNet's exporter classes — that API
// has churned between 0.13 / 0.14 / 0.15 and the public surface is
// unstable. Instead we read the JSON summary file BenchmarkDotNet
// always writes into `BenchmarkDotNet.Artifacts/results/*.json`
// after a run, and produce the report from that.
//
// This means the report printer is fully decoupled from BDN's
// internal API and will keep working across BDN versions.

using System.Globalization;
using System.Text.Json;

namespace FulgurNET.Benchmarks.Common;

/// <summary>
/// Reads the JSON output of a finished BDN run and prints a
/// "competition report" comparing FulgurNET against the benchmarked
/// competitors.
/// </summary>
public static class FulgurCompetitionReporter
{
    /// <summary>
    /// Read every JSON summary in
    /// <c>BenchmarkDotNet.Artifacts/results/</c> and print a consolidated
    /// report. We consume ALL of them, not just the latest, because
    /// BDN's per-benchmark-class <c>BenchmarkRunner.Run</c> each writes
    /// its own file.
    /// </summary>
    public static void PrintFromLatestResults(string? resultsDir = null)
    {
        resultsDir ??= Path.Combine(
            Directory.GetCurrentDirectory(),
            "BenchmarkDotNet.Artifacts", "results");

        if (!Directory.Exists(resultsDir))
        {
            Console.Error.WriteLine($"[reporter] no results dir at {resultsDir}");
            return;
        }

        var files = new DirectoryInfo(resultsDir)
            .GetFiles("*.json")
            .OrderBy(f => f.LastWriteTimeUtc)
            .ToList();
        if (files.Count == 0)
        {
            Console.Error.WriteLine($"[reporter] no JSON results in {resultsDir}");
            return;
        }

        var combined = new List<Row>();
        foreach (var f in files)
        {
            try
            {
                combined.AddRange(ParseFile(f.FullName));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[reporter] failed to parse {f.Name}: {ex.Message}");
            }
        }

        if (combined.Count == 0)
        {
            Console.Error.WriteLine("[reporter] no parseable rows across all JSON files");
            return;
        }

        PrintReport(combined);
    }

    /// <summary>Parse a single BDN JSON summary into rows.</summary>
    public static List<Row> ParseFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var benchmarks = root.GetProperty("Benchmarks").EnumerateArray().ToList();
        if (benchmarks.Count == 0) return new List<Row>();

        var rows = new List<Row>();
        foreach (var b in benchmarks)
        {
            var typeName = b.GetProperty("Type").GetString() ?? "?";
            var methodName = b.GetProperty("Method").GetString() ?? "?";

            // BDN 0.15 stores Parameters as a single string like "Size=Small".
            // Earlier versions exposed it as an object; the JsonExporter.Full
            // output here is the string form.
            var size = "?";
            if (b.TryGetProperty("Parameters", out var pEl))
            {
                if (pEl.ValueKind == JsonValueKind.String)
                {
                    var pStr = pEl.GetString() ?? "";
                    foreach (var part in pStr.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var kv = part.Split('=', 2);
                        if (kv.Length == 2 && kv[0].Trim() == "Size")
                        {
                            size = kv[1].Trim();
                            break;
                        }
                    }
                }
                else if (pEl.ValueKind == JsonValueKind.Object)
                {
                    var dict = pEl.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ToString());
                    size = dict.GetValueOrDefault("Size", "?");
                }
            }

            // Statistics -> Mean is in nanoseconds.
            var meanNs = b.GetProperty("Statistics").GetProperty("Mean").GetDouble();

            // Memory -> BytesAllocatedPerOperation (may be missing if
            // MemoryDiagnoser wasn't enabled).
            long allocBytes = 0;
            if (b.TryGetProperty("Memory", out var mem)
                && mem.TryGetProperty("BytesAllocatedPerOperation", out var ba)
                && ba.ValueKind == JsonValueKind.Number)
            {
                allocBytes = ba.GetInt64();
            }

            rows.Add(new Row(typeName, methodName, size, meanNs, allocBytes));
        }
        return rows;
    }

    /// <summary>Render a consolidated report from the given rows.</summary>
    public static void PrintReport(List<Row> rows)
    {
        // Render.
        var line = new string('═', 80);
        Console.WriteLine();
        Console.WriteLine(line);
        Console.WriteLine($"  FulgurNET competition report — {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine($"  host: {Environment.MachineName}  ·  OS: {Environment.OSVersion.VersionString}  ·  .NET: {Environment.Version}");
        Console.WriteLine(line);

        foreach (var sizeGroup in rows.GroupBy(r => r.Size))
        {
            Console.WriteLine();
            Console.WriteLine($"── {sizeGroup.Key} document ──");
            Console.WriteLine($"    {"Method",-50}  {"Mean",10}  {"Allocated",12}  {"Notes",30}");
            Console.WriteLine($"    {new string('-', 50)}  {new string('-', 10)}  {new string('-', 12)}  {new string('-', 30)}");

            // Order: FulgurNET first, then DinkToPdf, then Puppeteer.
            var ordered = sizeGroup
                .OrderBy(r => r.TypeName switch
                {
                    "FulgurBench"   => 0,
                    "DinkToPdfBench" => 1,
                    "PuppeteerBench" => 2,
                    _ => 99,
                })
                .ThenBy(r => r.MethodName);

            foreach (var r in ordered)
            {
                var methodLabel = $"{r.TypeName}::{r.MethodName}";
                var category = r.MethodName.Contains("Cold", StringComparison.OrdinalIgnoreCase) ? "(cold)" : "(warm)";
                Console.WriteLine($"    {methodLabel,-50}  " +
                                  $"{FormatMs(r.MeanNs),10}  " +
                                  $"{FormatBytes(r.AllocBytes),12}  " +
                                  $"{category,-30}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("── Headline ──");
        BuildHeadline(rows);

        Console.WriteLine();
        Console.WriteLine("── Binary size (one-time download per machine) ──");
        Console.WriteLine("    FulgurNET       ~1  MB   (per RID, bundled in NuGet package)");
        Console.WriteLine("    DinkToPdf       ~20 MB  (libwkhtmltox, ships with NuGet)");
        Console.WriteLine("    PuppeteerSharp  ~150 MB  (Chromium, downloaded on first run)");
        Console.WriteLine();
        Console.WriteLine("Methodology: see README.md → \"Benchmarks\". Numbers from this run only;");
        Console.WriteLine("rerun for confidence intervals. BDN's per-benchmark standard error is the");
        Console.WriteLine("right number to look at for noise — anything under ~5% is meaningful.");
        Console.WriteLine(line);
    }

    /// <summary>Parse a single BDN JSON summary and print the report.</summary>
    public static void PrintFromFile(string path)
    {
        var rows = ParseFile(path);
        if (rows.Count == 0)
        {
            Console.Error.WriteLine($"[reporter] empty results in {path}");
            return;
        }
        PrintReport(rows);
    }

    private static void BuildHeadline(List<Row> rows)
    {
        // Build speedup headlines: FulgurNET warm vs DinkToPdf warm
        // and FulgurNET warm vs PuppeteerSharp warm, for the Medium
        // document (the canonical benchmark target).
        var fulgurWarmMedium = rows.FirstOrDefault(r => r.TypeName == "FulgurBench"   && r.MethodName == "Warm" && r.Size == "Medium");
        var dinkWarmMedium    = rows.FirstOrDefault(r => r.TypeName == "DinkToPdfBench" && r.MethodName == "Warm" && r.Size == "Medium");
        var puppeteerWarmMedium = rows.FirstOrDefault(r => r.TypeName == "PuppeteerBench" && r.MethodName == "Warm" && r.Size == "Medium");

        if (fulgurWarmMedium is not null && dinkWarmMedium is not null)
        {
            Console.WriteLine($"    FulgurNET is {FormatRatio(dinkWarmMedium.MeanNs / fulgurWarmMedium.MeanNs)} faster than DinkToPdf (warm, medium document)");
        }
        if (fulgurWarmMedium is not null && puppeteerWarmMedium is not null)
        {
            Console.WriteLine($"    FulgurNET is {FormatRatio(puppeteerWarmMedium.MeanNs / fulgurWarmMedium.MeanNs)} faster than PuppeteerSharp (warm, medium document)");
        }
        if (fulgurWarmMedium is null)
        {
            Console.WriteLine("    (no FulgurBench::Warm results found — was the FulgurBench class filtered out?)");
        }
    }

    private static string FormatMs(double ns) =>
        ns <= 0 ? "—" : (ns / 1_000_000.0).ToString("F2", CultureInfo.InvariantCulture) + " ms";

    private static string FormatBytes(long bytes) =>
        bytes switch
        {
            0        => "—",
            < 1024   => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            _        => $"{bytes / (1024.0 * 1024.0):F1} MB",
        };

    private static string FormatRatio(double r) =>
        r >= 1
            ? r.ToString("F1", CultureInfo.InvariantCulture) + "×"
            : (1 / Math.Max(r, 0.001)).ToString("F2", CultureInfo.InvariantCulture) + "× slower";

    /// <summary>One benchmark row from the JSON summary.</summary>
    public sealed record Row(string TypeName, string MethodName, string Size, double MeanNs, long AllocBytes);
}
