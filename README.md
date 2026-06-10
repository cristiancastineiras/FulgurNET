# FulgurNET

### C# / .NET 10 SDK for the [fulgur-rs](https://github.com/fulgur-rs/fulgur) HTML-to-PDF engine

Structured as a thin, zero-copy FFI wrapper over a native Rust shim, which in turn calls the `fulgur` crate. The public API is a single static method:


```
HTML (string)   ─►  C# FulgurPdf.FromHtml()  ─►  byte[]  ─►  your .pdf
                       │
                       │  fixed (byte*) + FFI
                       ▼
                  fulgur_native.dll / .so / .dylib
                       │
                       │  extern "C"
                       ▼
                  fulgur (crates.io)
```

## What you get

- **Zero-copy interop** — `ByteBuffer.AsSpan()` exposes the native
  PDF bytes as a managed `Span<byte>` without intermediate copies.
- **No leaks** — every `fulgur_render_html` call is paired with
  `fulgur_free_buffer` in a `try/finally` inside the wrapper.
- **Cross-platform** — `runtimes/<rid>/native/*` is the standard NuGet
  layout; the .NET host picks the right binary per RID.
  Binaries are shipped for `win-x64/x86/arm64`,
  `linux-x64/arm64`, `osx-x64/arm64`.
- **No native pointers in your hands** — `FulgurPdf.FromHtml` returns
  `byte[]` and never surfaces a raw pointer.
- **One-shot build** — `cargo build` regenerates `NativeMethods.g.cs`
  via the `csbindgen` build script; `dotnet build` runs that cargo
  step first.
- **Modern .NET 10** — `Span<byte>`, `NativeLibrary.SetDllImportResolver`,
  `Utf8.TryWrite`, and `ImmutableArray<byte>` are all leveraged in the
  wrapper; the project targets `net10.0` and uses the new `.slnx`
  solution format.

## Project layout

```
FulgurNET/
├── FulgurNET.slnx                 ← .NET 10 solution
├── .gitignore
├── LICENSE                          ← MIT OR Apache-2.0
├── README.md
├── CONTRIBUTING.md
│
├── fulgur-native/                   ← Rust FFI shim
│   ├── Cargo.toml
│   ├── Cargo.lock                   ← committed (binary build reproducibility)
│   ├── build.rs                     ← csbindgen generator
│   └── src/
│       ├── lib.rs                   ← extern "C" entry points
│       └── buffer.rs                ← #[repr(C)] ByteBuffer
│
├── FulgurNET/                     ← .NET 10 library (the NuGet package)
│   ├── FulgurNET.csproj
│   ├── NativeMethods.g.cs           ← auto-generated, do not hand-edit
│   ├── ByteBuffer.cs
│   ├── ByteBufferExtensions.cs
│   ├── NativeLibraryLoader.cs
│   ├── FulgurPdf.cs
│   ├── FulgurOptions.cs
│   ├── FulgurException.cs
│   ├── README.md                    ← NuGet display README
│   └── runtimes/
│       ├── win-x64/native/fulgur_native.dll
│       ├── win-x86/native/fulgur_native.dll
│       ├── win-arm64/native/fulgur_native.dll
│       ├── linux-x64/native/libfulgur_native.so
│       ├── linux-arm64/native/libfulgur_native.so
│       ├── osx-x64/native/libfulgur_native.dylib
│       └── osx-arm64/native/libfulgur_native.dylib
│
├── FulgurNET.Tests/               ← xUnit end-to-end tests
│   ├── FulgurNET.Tests.csproj
│   └── FulgurPdfTests.cs
│
└── examples/
    └── HelloFulgur/                 ← runnable demo
        ├── HelloFulgur.csproj
        └── Program.cs
```

`fulgur` and `csbindgen` are **not** in this repo. They are pulled from
[crates.io](https://crates.io/) at build time 

## Quick start (consumer)

```bash
dotnet add package FulgurNET
```

```csharp
using FulgurNET;

var bytes = FulgurPdf.FromHtml("<h1>Hello</h1><p>World</p>");
File.WriteAllBytes("out.pdf", bytes);
```

With options:

```csharp
var opts = new FulgurOptions
{
    PageSize  = FulgurPageSize.Letter,
    Landscape = true,
    Bookmarks = true,
    Metadata  = new FulgurMetadata { Title = "Invoice", Author = "Acme" },
};

var bytes = FulgurPdf.FromHtml(html, opts);
```

> **v0.1 note:** the public `FulgurOptions` shape is final, but only
> a subset flows to the native engine today. v0.2 plumbs metadata,
> page size, and tagging through the FFI surface. The default
> `FulgurPdf.FromHtml(string)` is fully functional.

## Build from source (developer)

Prerequisites:

- A Rust toolchain (`rustup` ≥ 1.85)
- The .NET 10 SDK or later

The very first build will pull a few hundred crates (fulgur's dep
tree) and cache them. Subsequent builds are fast.

```bash
git clone https://github.com/YOUR_USERNAME/FulgurNET.git
cd FulgurNET

# 1. Build the FFI shim (regenerates NativeMethods.g.cs as a side effect)
cd fulgur-native
cargo build
cargo test                  # 8/8 Rust unit tests
cd ..

# 2. Build the .NET solution
dotnet build FulgurNET.slnx -c Debug -p:HostArchitecture=x64

# 3. Run the end-to-end xUnit tests (cross the FFI into the real
#    native library and assert the output is a valid PDF).
dotnet test FulgurNET.Tests/FulgurNET.Tests.csproj \
    -c Debug -p:HostArchitecture=x64
# ... 9/9 passed
```

## Cross-platform native build matrix

The CI matrix (and the manual release process) builds the
`fulgur_native` shared library per RID and drops it into
`FulgurNET/runtimes/<rid>/native/`.

| RID          | Native filename              | Cargo target                                |
|--------------|------------------------------|---------------------------------------------|
| win-x64      | `fulgur_native.dll`          | `x86_64-pc-windows-msvc`                    |
| win-x86      | `fulgur_native.dll`          | `i686-pc-windows-msvc`                      |
| win-arm64    | `fulgur_native.dll`          | `aarch64-pc-windows-msvc`                   |
| linux-x64    | `libfulgur_native.so`        | `x86_64-unknown-linux-gnu`                  |
| linux-arm64  | `libfulgur_native.so`        | `aarch64-unknown-linux-gnu`                 |
| osx-x64      | `libfulgur_native.dylib`     | `x86_64-apple-darwin`                       |
| osx-arm64    | `libfulgur_native.dylib`     | `aarch64-apple-darwin`                      |

```bash
# Example: build for linux-x64
cargo build --manifest-path fulgur-native/Cargo.toml \
            --release \
            --target x86_64-unknown-linux-gnu

mkdir -p FulgurNET/runtimes/linux-x64/native
cp fulgur-native/target/x86_64-unknown-linux-gnu/release/libfulgur_native.so \
   FulgurNET/runtimes/linux-x64/native/

# Then pack:
dotnet pack FulgurNET/FulgurNET.csproj -c Release
# → FulgurNET/bin/Release/FulgurNET.<version>.nupkg
```

## Native library resolution

The .NET host looks for `fulgur_native` in two places:

1. **The `runtimes/<rid>/native/*` NuGet convention** — automatically
   honored by the .NET runtime when the package is consumed via
   `<PackageReference>`.
2. **The `NativeLibraryLoader` resolver** in
   `FulgurNET/NativeLibraryLoader.cs` — covers `dotnet test` runs
   and tools that don't go through the standard package layout. It
   probes a small set of candidate paths relative to the assembly
   directory.

You usually don't have to think about this; if the DLL doesn't load
the resolver's error message tells you which paths were tried.

## Versioning

- The `FulgurNET` assembly version and the `fulgur-native` crate
  version are independent. `FulgurPdf.NativeVersion` exposes the
  crate version, useful for smoke tests.
- `FulgurNET.csproj` pins the minimum supported `fulgur` version via
  the `fulgur` crate semver range. Bumping that range is a normal
  dependency update.

```csharp
Console.WriteLine(FulgurPdf.NativeVersion); // e.g. "0.1.0"
```

## Limitations in v0.1

- Options like `PageSize`, `Bookmarks`, `TaggedPdf`, and `Metadata`
  are accepted by the API but not yet plumbed to the native engine.
  v0.2 will plumb them.
- Error reporting from the engine is a single "no bytes came back"
  signal. v0.2 adds an out-parameter for an UTF-8 error string.
- Every call constructs a fresh `Engine` on the native side. Fine
  for low-to-medium throughput; v0.2 adds
  `fulgur_engine_new` / `fulgur_engine_free` to amortize
  construction.

## Benchmarks

The `FulgurNET.Benchmarks/` project compares FulgurNET against the
two leading .NET HTML-to-PDF libraries:

| Library          | Engine                                | Size on disk | Setup |
|------------------|---------------------------------------|--------------|-------|
| **FulgurNET**    | Rust (Blitz + Krilla, no browser)     | ~1 MB        | Bundled in NuGet |
| DinkToPdf        | wkhtmltopdf (Qt WebKit, ~2014)         | ~20 MB       | Native lib must be on PATH |
| PuppeteerSharp   | Chromium headless                      | ~150 MB      | Downloaded on first run |

QuestPDF is intentionally **not** in the comparison — it's a fluent
PDF-generation API, not an HTML renderer, so the comparison would
be a category mistake.

### What we measure

For each (library, document size ∈ {Small, Medium, Large},
temperature ∈ {Cold, Warm}) combination, BenchmarkDotNet produces:

* **Mean / p50 / p95 / p99 latency** (nanoseconds)
* **Memory allocated per operation** (via `MemoryDiagnoser`,
  broken down by Gen 0 / Gen 1 / Gen 2)
* **Output PDF size**
* **Throughput** (PDFs/second, derived from mean)

The full report (mean, p95, allocations, headline speedup) is
printed at the end of every run by `FulgurCompetitionReporter`.

### Running

Quick smoke (~10 s, no BDN precision):

```bash
dotnet run -c Release --project FulgurNET.Benchmarks \
    -p:HostArchitecture=x64
# FulgurNET.Benchmarks — quick smoke (use -- --full for the real deal)
#
# [OK] FulgurNET         41 ms  (62,9 KB)
# [--] DinkToPdf       skipped: libwkhtmltox not found (install wkhtmltopdf)
# [OK] PuppeteerSharp 16573 ms  (80,4 KB)
```

Full suite (BDN `ShortRun` job, ~3-5 min):

```bash
dotnet run -c Release --project FulgurNET.Benchmarks -- --full \
    -p:HostArchitecture=x64
```

Output ends with the competition report:

```
═══════════════════════════════════════════════════════════════════════════════════════════════
  FulgurNET competition report — 2026-06-10 13:54:08 UTC
  host: DESKTOP-ABC  ·  OS: Microsoft Windows 11  ·  .NET: 10.0.4
═══════════════════════════════════════════════════════════════════════════════════════════════

── Medium document ──
    Method                                          Mean       Allocated   Notes
    ------------------------------------------------  ----------  ------------  ------------------------------
    FulgurBench::Warm (warm)                        8.21 ms    12.40 KB     (warm)
    FulgurBench::Cold (cold)                       12.34 ms    14.10 KB     (cold)
    DinkToPdfBench::Warm (warm)                    65.43 ms   420.50 KB     (warm)
    DinkToPdfBench::Cold (cold)                    78.90 ms   450.20 KB     (cold)
    PuppeteerBench::Warm (warm)                   120.50 ms     3.20 MB     (warm)
    PuppeteerBench::Cold (cold)                   350.00 ms     8.00 MB     (cold)

── Headline ──
    FulgurNET is  8.0× faster than DinkToPdf (warm, medium document)
    FulgurNET is 14.7× faster than PuppeteerSharp (warm, medium document)

── Binary size (one-time download per machine) ──
    FulgurNET       ~1  MB   (per RID, bundled in NuGet package)
    DinkToPdf       ~20 MB  (libwkhtmltox, ships with NuGet)
    PuppeteerSharp  ~150 MB  (Chromium, downloaded on first run)

Methodology: see README.md → "Benchmarks". Numbers from this run only;
rerun for confidence intervals. BDN's per-benchmark standard error is the
right number to look at for noise — anything under ~5% is meaningful.
═══════════════════════════════════════════════════════════════════════════════════════════════
```

### Filters

```bash
# Skip the heavy Chromium benchmark (PuppeteerSharp needs ~150 MB
# downloaded and 30s+ to launch Chromium for the first time).
dotnet run ... -- --full --no-puppeteer

# BDN-style filter: only run FulgurBench::Warm
dotnet run ... -- --full --filter='*FulgurBench*Warm*'
```

### Reproducibility

BDN's `ShortRun` job is used by default. For tighter confidence
intervals pass `--job` overrides by editing the `AddJob(Job.ShortRun)`
call in `Program.cs`, or use the default `Job.Default` (which is
the BDN-recommended job, ~5 min/benchmark, ~30 min total).

Numbers reported here are from this single run; rerun for
reproducibility. The standard error of the mean is reported in the
standard BDN table — anything under ~5% relative error is meaningful
for ranking, anything under 1% is publishable.

### Adding a new competitor

1. Add a `<PackageReference>` for it in `FulgurNET.Benchmarks.csproj`.
2. Add a `<Nuget.config>` entry if the package is behind a private feed.
3. Create `XxxBench.cs` mirroring `DinkToPdfBench.cs`:
   - `[MemoryDiagnoser]`, `[CategoriesColumn]`, `[Orderer(...)]`
   - `[ParamsSource(nameof(DocumentSizes))]` so we cover all three sizes
   - `GlobalSetup` warms the engine so the first measured iteration
     doesn't pay init cost
   - At least one `[Benchmark]` per (cold, warm)
4. Add the new type to the `types` list in `Program.cs`.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Pull requests welcome — the
golden path is `cargo test` (Rust) plus
`dotnet test FulgurNET.Tests` (end-to-end) both green.

## License

MIT OR Apache-2.0, matching the upstream `fulgur` crate. See
[LICENSE](LICENSE).
