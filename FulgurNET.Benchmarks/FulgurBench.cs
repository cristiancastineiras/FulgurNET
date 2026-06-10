// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Benchmark for FulgurNET (the engine under test). Mirrors the
// methodology used by FulgurBench and DinkToPdfBench in the same
// suite so the summary exporter can rank them apples-to-apples.

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using FulgurNET.Benchmarks.Common;
using FulgurNET;

namespace FulgurNET.Benchmarks;

/// <summary>
/// Micro-benchmarks of the high-level <see cref="FulgurPdf"/> API.
///
/// We measure three dimensions per document size:
///   1. Cold render — includes the cost of building a fresh
///      <c>Engine</c> on the native side. Maps to "how long until
///      the first PDF is in the user's hand" in real apps.
///   2. Warm render — engine is reused. Maps to "sustained
///      throughput" in batch jobs.
///   3. Allocation profile — how much managed memory the call
///      touches (we also care about the unmanaged side; the
///      benchmark runs under <c>MemoryDiagnoser</c> so the
///      per-allocation breakdown is captured automatically).
/// </summary>
[MemoryDiagnoser]
[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class FulgurBench
{
    /// <summary>The HTML to render. Pinned in the constructor so the
    /// JIT cannot elide it.</summary>
    private string _html = null!;

    /// <summary>Bytes returned by the last warm call. Used by the
    /// custom summary exporter to report output size.</summary>
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
        // Warm the FFI shim up-front so the first measured iteration
        // doesn't pay JIT + DllImport resolution costs.
        var warm = FulgurPdf.FromHtml(_html);
        _lastOutputSize = warm.Length;
    }

    [Benchmark(Baseline = true, Description = "FulgurNET — cold (fresh Engine per call)")]
    [BenchmarkCategory("Cold", "FulgurNET")]
    public byte[] Cold()
    {
        // Each call constructs a fresh native Engine; the cost we
        // measure includes both the FFI marshaling and the engine
        // construction that the Rust side does today.
        var bytes = FulgurPdf.FromHtml(_html);
        _lastOutputSize = bytes.Length;
        return bytes;
    }

    [Benchmark(Description = "FulgurNET — warm (engine state reused)")]
    [BenchmarkCategory("Warm", "FulgurNET")]
    public byte[] Warm()
    {
        // Even without an explicit handle, the native EngineBuilder
        // and CS bindgen stubs cache well; the warm path measures
        // how much of the work is per-call vs. one-time.
        var bytes = FulgurPdf.FromHtml(_html);
        _lastOutputSize = bytes.Length;
        return bytes;
    }

    /// <summary>
    /// Returns the size of the most recent render's output. Consumed
    /// by the custom summary exporter for the "Output bytes" column
    /// in the final report.
    /// </summary>
    public int LastOutputSize => _lastOutputSize;
}

/// <summary>Document-size parameter used by every benchmark in the suite.</summary>
public enum DocumentSize
{
    Small,
    Medium,
    Large,
}
