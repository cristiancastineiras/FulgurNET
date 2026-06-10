// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Code generation glue: turn the `extern "C"` surface in `src/lib.rs`
// into a C# `NativeMethods.g.cs` with matching `[DllImport]` stubs.
//
// Output path: `../FulgurNET/NativeMethods.g.cs` — relative to this
// crate's manifest dir, which is `<repo>/fulgur-native/`. The .NET
// project tree-sits beside the FFI crate so the generator can keep
// running on every `cargo build` without the user invoking a separate
// command. The .csproj marks the file as `<Compile Remove>`-d and
// re-adds it via the build pipeline, see FulgurNET.csproj.

use csbindgen::Builder;

fn main() {
    println!("cargo:rerun-if-changed=src/lib.rs");
    println!("cargo:rerun-if-changed=src/buffer.rs");
    println!("cargo:rerun-if-changed=build.rs");

    Builder::default()
        // Scan only the top-level lib — we don't expose any helpers from
        // `buffer.rs` directly, only through the FFI surface.
        .input_extern_file("src/lib.rs")
        // The DllImport name must match the produced binary. On Windows
        // the linker emits `fulgur_native.dll`; on macOS / Linux
        // `libfulgur_native.{dylib,so}`. .NET's `NativeLibrary` resolver
        // (see FulgurNET/NativeMethods.g.cs) handles the lib/lib*
        // prefix dance.
        .csharp_dll_name("fulgur_native")
        .csharp_class_name("NativeMethods")
        .csharp_namespace("FulgurNET")
        .csharp_class_accessibility("public")
        // `unsafe` because every entry touches raw pointers.
        .csharp_entry_point_prefix("")
        // Don't import any extra namespaces by default — the generated
        // file uses only `System` and `System.Runtime.InteropServices`.
        // (No `.csharp_import_namespace(...)` call is needed; the default
        // list is empty.)
        // Generate `delegate* unmanaged[Cdecl]<>` (function pointer)
        // syntax — modern .NET 6+ / Unity 2022.3+. We do NOT need
        // MonoPInvokeCallback fallbacks because we don't expose any
        // callbacks in v1.
        .csharp_use_function_pointer(true)
        // KEEP the `__DllName` constant. We want the standard DllImport
        // name to be the canonical one and use a `NativeLibrary`
        // resolver for cross-RID dispatch.
        .generate_csharp_file("../FulgurNET/NativeMethods.g.cs")
        .expect("csbindgen failed to generate NativeMethods.g.cs");
}
