# Changelog

All notable changes to FulgurNET are documented here. The format
follows [Keep a Changelog](https://keepachangelog.com/) and the
project adheres to [Semantic Versioning](https://semver.org/).

## [0.1.0] — 2026-06-10

### Added

- Initial release.
- `FulgurPdf.FromHtml(string)` returning `byte[]` (PDF bytes).
- `FulgurPdf.FromHtml(string, FulgurOptions?)` for options-driven rendering.
- `FulgurPdf.FromHtmlToFile(string, string)` convenience overload.
- `FulgurPdf.NativeVersion` reporting the bundled `fulgur-native` version.
- `FulgurOptions` with `PageSize`, `Landscape`, `Bookmarks`, `TaggedPdf`,
  `Margin`, `Metadata`, `ExtraCss`.
- Multi-RID NuGet packaging for `win-x64`, `win-x86`, `win-arm64`,
  `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`.
- csbindgen-generated `NativeMethods.g.cs` (3 entry points:
  `fulgur_render_html`, `fulgur_free_buffer`, `fulgur_version`).
- `NativeLibraryLoader` resolver for `dotnet test` / non-NuGet flows.
- 8/8 Rust unit tests and 9/9 end-to-end xUnit tests.
- `HelloFulgur` example project.
- `FulgurNET.Benchmarks` suite (BenchmarkDotNet 0.15.3) comparing
  against DinkToPdf 1.0.8 (wkhtmltopdf) and PuppeteerSharp 25.1.0
  (Chromium) with `[MemoryDiagnoser]`, `ShortRun` job, and a custom
  competition reporter that reads BDN's JSON output at end of run.
  See `bench-results.md` for the actual numbers.
- `.github/workflows/build.yml` covering the 7-RID matrix, per-RID
  pack, OIDC trusted publishing to nuget.org via `NuGet/login@v1`.

### Known limitations

- `FulgurOptions` are accepted by the API but not all are plumbed to
  the native engine yet — they all flow at engine defaults.
- Error reporting from the engine is a zero-length buffer signal.
- Every call constructs a fresh native `Engine`; no persistent handle.
- PuppeteerSharp in benchmarks is "warm" only — cold-start Chromium
  is out of scope for the comparison (would unfairly dilute the
  per-call latency story).
