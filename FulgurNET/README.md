# FulgurNET

C# / .NET 10 SDK for the
[fulgur](https://github.com/fulgur-rs/fulgur) HTML-to-PDF engine. One
`cargo build` + one `dotnet build` is enough to go from a Rust source
change to a working NuGet package.

## Quick start

```csharp
using FulgurNET;

var bytes = FulgurPdf.FromHtml("<h1>Hello</h1><p>World</p>");
File.WriteAllBytes("out.pdf", bytes);
```

## What you get

- **Zero-copy interop** — `ByteBuffer.AsSpan()` exposes the native
  PDF bytes as a managed `Span<byte>` without intermediate copies.
- **No leaks** — every `fulgur_render_html` call is paired with
  `fulgur_free_buffer` in a `try/finally` inside the wrapper.
- **Cross-platform** — `runtimes/<rid>/native/*` is the standard NuGet
  layout; the .NET host picks the right binary per RID.
- **No native pointers in your hands** — `FulgurPdf.FromHtml` returns
  `byte[]` and never surfaces a raw pointer.

## Project layout

```
FulgurNET/
├── fulgur-native/      ← Rust FFI shim (cdylib, generates NativeMethods.g.cs)
├── FulgurNET/        ← .NET 8 library (this NuGet package)
├── FulgurNET.Tests/  ← xUnit end-to-end tests
└── examples/HelloFulgur/  ← runnable demo
```

`fulgur` and `csbindgen` are pulled from [crates.io](https://crates.io/)
at build time, not vendored.

## Build from source

Prerequisites: Rust ≥ 1.85, .NET 10 SDK.

```bash
# Regenerate NativeMethods.g.cs and build the FFI shim
cd fulgur-native
cargo build
cargo test
cd ..

# Build the .NET solution (the MSBuild `BuildNative` target runs
# `cargo build` first if you skipped the manual step above)
dotnet build ../FulgurNET.slnx -c Debug -p:HostArchitecture=x64

# Run the end-to-end tests
dotnet test ../FulgurNET.Tests/FulgurNET.Tests.csproj -c Debug -p:HostArchitecture=x64
```

To produce a per-RID NuGet package, build the native library for the
target RID and drop it into `FulgurNET/runtimes/<rid>/native/`:

```bash
cargo build --manifest-path fulgur-native/Cargo.toml --release --target x86_64-pc-windows-msvc
mkdir -p FulgurNET/runtimes/win-x64/native
cp fulgur-native/target/x86_64-pc-windows-msvc/release/fulgur_native.dll \
   FulgurNET/runtimes/win-x64/native/

dotnet pack FulgurNET/FulgurNET.csproj -c Release
```

## Cross-platform native build matrix

| RID          | Native filename              | Cargo target                                |
|--------------|------------------------------|---------------------------------------------|
| win-x64      | `fulgur_native.dll`          | `x86_64-pc-windows-msvc`                    |
| win-x86      | `fulgur_native.dll`          | `i686-pc-windows-msvc`                      |
| win-arm64    | `fulgur_native.dll`          | `aarch64-pc-windows-msvc`                   |
| linux-x64    | `libfulgur_native.so`        | `x86_64-unknown-linux-gnu`                  |
| linux-arm64  | `libfulgur_native.so`        | `aarch64-unknown-linux-gnu`                 |
| osx-x64      | `libfulgur_native.dylib`     | `x86_64-apple-darwin`                       |
| osx-arm64    | `libfulgur_native.dylib`     | `aarch64-apple-darwin`                      |

## Versioning

`FulgurNET` and `fulgur-native` versions are independent.
`FulgurPdf.NativeVersion` exposes the crate version, useful for smoke
tests:

```csharp
Console.WriteLine(FulgurPdf.NativeVersion); // e.g. "0.1.0"
```

## Limitations in v0.1

- Options like `PageSize`, `Bookmarks`, `TaggedPdf`, `Metadata` are
  accepted by the API but not yet plumbed to the native engine —
  v0.2 will plumb them.
- Error reporting is a "no bytes came back" signal. v0.2 adds an
  out-parameter for an UTF-8 error string.
- Every call constructs a fresh `Engine` on the native side. v0.2
  adds `fulgur_engine_new` / `fulgur_engine_free` to amortize.

## License

MIT OR Apache-2.0, matching upstream `fulgur`. See
[LICENSE](https://github.com/YOUR_USERNAME/FulgurNET/blob/main/LICENSE).
