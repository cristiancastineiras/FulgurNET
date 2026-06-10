// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Native library resolver. The `runtimes/<rid>/native/*` convention
// is the standard NuGet layout, but the .NET host only loads the
// binary automatically when it lives in the consumer's output dir at
// the right RID-relative path. In a `<PackageReference>` flow the
// host does the right thing; in a `<Reference Include="..\foo.dll">`
// flow or when the package is consumed from a global packages cache
// without a `dotnet restore`, the resolver kicks in.
//
// We expose a static `EnsureRegistered()` so consumers (and our own
// tests) can force registration in either order. It is idempotent.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace FulgurNET;

/// <summary>
/// Resolves the <c>fulgur_native</c> library from the per-RID
/// <c>runtimes/</c> folder, falling back to the BCL's default search
/// path (which honors the standard NuGet layout when the package is
/// consumed via <c>PackageReference</c>).
/// </summary>
internal static class NativeLibraryLoader
{
    internal const string LibraryName = "fulgur_native";

    private static int s_registered;

    /// <summary>
    /// Install the resolver exactly once for this process. Safe to
    /// call from multiple threads; subsequent calls are no-ops.
    /// </summary>
    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref s_registered, 1) != 0)
        {
            return;
        }
        NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, Resolve);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
        {
            // Let the BCL try the default search path for any unrelated
            // library another component in the process might need.
            return IntPtr.Zero;
        }

        // Probe a small list of candidate locations. The first hit wins.
        // The list is ordered from "most specific" (RID-keyed) to "least
        // specific" (process CWD) so unit tests and `dotnet test` flows
        // work even when the package isn't fully restored.
        foreach (var candidate in EnumerateCandidates(assembly))
        {
            if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    private static IEnumerable<string> EnumerateCandidates(Assembly assembly)
    {
        var (fileName, ext) = FileNameForCurrentPlatform();
        var arch = ArchitectureSegment();
        var rid = CurrentRid();

        // 1. The standard NuGet layout, resolved relative to the
        //    assembly directory. This is what `dotnet test` and most
        //    hosts use; relative-to-CWD is fragile (the working
        //    directory can be anything the launch tool set).
        var asmDir = Path.GetDirectoryName(assembly.Location);
        if (!string.IsNullOrEmpty(asmDir))
        {
            yield return Path.Combine(asmDir, "runtimes", rid, "native", $"{fileName}{ext}");
            yield return Path.Combine(asmDir, "runtimes", "native", arch, $"{fileName}{ext}");

            // 2. Flat next to the assembly — covers `dotnet test` runs
            //    that copy native binaries next to the test assembly.
            yield return Path.Combine(asmDir, $"{fileName}{ext}");
        }

        // 3. Last-ditch: probe the process CWD with the NuGet layout.
        //    Useful for command-line tools that `cd` to a working dir
        //    and expect `runtimes/<rid>/native/<lib>` to be there.
        yield return $"runtimes/{rid}/native/{fileName}{ext}";
    }

    private static (string fileName, string ext) FileNameForCurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ("fulgur_native", ".dll");
        }
        // Linux and macOS both use the `lib<name>.<ext>` form, but
        // macOS uses .dylib and Linux .so. The csbindgen-generated
        // `__DllName` is "fulgur_native" (no prefix) so the resolver
        // picks the right on-disk filename.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return ("libfulgur_native", ".dylib");
        }
        return ("libfulgur_native", ".so");
    }

    private static string ArchitectureSegment() =>
        RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "x64",
        };

    private static string CurrentRid()
    {
        // RuntimeInformation.RuntimeIdentifier is .NET 8+; for
        // netstandard2.1 we synthesize the well-known rid from
        // OS + arch. This covers the platforms we actually ship
        // binaries for.
        string os;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            os = "win";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            os = "osx";
        }
        else
        {
            os = "linux";
        }
        return $"{os}-{ArchitectureSegment()}";
    }
}
