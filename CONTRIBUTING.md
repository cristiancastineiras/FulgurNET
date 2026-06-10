# Contributing to FulgurNET

Thanks for your interest in improving FulgurNET! A few notes to
keep the project healthy.

## Code of conduct

Be kind. This is an SDK that other people wire into their
applications — every breaking change has a real cost downstream.

## Development setup

Prerequisites:

- A Rust toolchain (`rustup` ≥ 1.85)
- The .NET 10 SDK or later
- (Optional) `cross` for cross-compiling the FFI shim to non-host RIDs

Clone, build, test:

```bash
git clone https://github.com/YOUR_USERNAME/FulgurNET.git
cd FulgurNET

# Rust side (8/8 unit tests)
cd fulgur-native
cargo build
cargo test
cd ..

# .NET side (9/9 end-to-end tests)
dotnet build FulgurNET.slnx -c Debug -p:HostArchitecture=x64
dotnet test FulgurNET.Tests/FulgurNET.Tests.csproj -c Debug -p:HostArchitecture=x64
```

The MSBuild `BuildNative` target runs `cargo build` automatically, so
the manual `cd fulgur-native` step is optional if you only want to
exercise the .NET surface.

## How to add a new FFI entry point

1. Add the function to `fulgur-native/src/lib.rs` (Rust, `extern "C"`).
2. Add a unit test in the same file (Rust side, 8/8 stays green).
3. Build the .NET side once — `csbindgen` regenerates
   `FulgurNET/NativeMethods.g.cs` automatically.
4. Wrap the raw P/Invoke in a high-level method in
   `FulgurNET/FulgurPdf.cs` (or a new file in that folder). Never
   expose `byte*` or `ByteBuffer` to public consumers — always hand
   back `byte[]` and free the buffer in a `try/finally`.
5. Add an end-to-end xUnit test in `FulgurNET.Tests/FulgurPdfTests.cs`.

## How to bump the bundled `fulgur` version

`fulgur-native/Cargo.toml` pins `fulgur = "0.17"` (semver range). To
bump:

```bash
cd fulgur-native
cargo update -p fulgur     # updates Cargo.lock to the latest 0.17.x
cargo test                 # ensure the new version still works
```

If the new minor (`0.18`) introduces breaking changes, edit
`fulgur-native/Cargo.toml` to `fulgur = "0.18"`, fix the FFI surface
to match, and run the full test suite.

## Style

- **Rust:** `cargo fmt` + `cargo clippy -- -D warnings` before pushing.
- **C#:** `dotnet format` before pushing.
- **Commits:** Conventional Commits (`feat:`, `fix:`, `chore:`).
  Reference the FFI surface affected so reviewers can spot
  cross-language implications.

## Pull request checklist

- [ ] `cargo test` is green in `fulgur-native/`.
- [ ] `dotnet test FulgurNET.Tests` is green.
- [ ] `cargo fmt` and `dotnet format` produced no diff.
- [ ] New public API is documented with XML doc comments.
- [ ] `CHANGELOG.md` (if you create one) has an entry.

## Reporting issues

Include:

- OS and architecture (`win-x64`, `linux-arm64`, `osx-x64`, etc.)
- .NET runtime version (`dotnet --info` output)
- Rust toolchain version (`rustc --version`)
- Minimal HTML snippet that reproduces the issue
- Stack trace, if any
