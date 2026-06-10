// SPDX-License-Identifier: MIT OR Apache-2.0
//
// FulgurPdf — the only public type most consumers ever need.
//
// Usage:
//     using FulgurNET;
//
//     var bytes = FulgurPdf.FromHtml("<h1>Hello</h1><p>World</p>");
//     File.WriteAllBytes("out.pdf", bytes);
//
// Or with options:
//     var opts = new FulgurOptions { PageSize = FulgurPageSize.Letter, Bookmarks = true };
//     var bytes = FulgurPdf.FromHtml(html, opts);
//
// Memory ownership: every call into the native layer returns a
// `ByteBuffer` that the native allocator owns. We always pair the
// FFI call with a `fulgur_free_buffer` in a `try/finally` so a C#
// exception between the two can't leak the allocation.

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace FulgurNET;

/// <summary>
/// Static entry points for rendering HTML to PDF using the fulgur
/// engine. All methods are thread-safe — each call constructs its
/// own native engine, so concurrent invocations do not contend on
/// shared state.
/// </summary>
public static class FulgurPdf
{
    /// <summary>
    /// Render an HTML string to PDF bytes using the engine defaults
    /// (A4 portrait, 20mm margin, no metadata, no bookmarks, no
    /// tagging).
    /// </summary>
    /// <param name="html">A UTF-8 HTML document.</param>
    /// <returns>The raw PDF bytes. Never <c>null</c>; an empty array
    /// signals a render failure (and an exception is also raised).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="html"/> is <c>null</c>.</exception>
    /// <exception cref="FulgurException">The native engine returned an error.</exception>
    public static byte[] FromHtml(string html)
    {
        return FromHtml(html, options: null);
    }

    /// <summary>
    /// Render an HTML string to PDF bytes with the given options.
    /// </summary>
    /// <remarks>
    /// In v0.1 the <see cref="FulgurOptions"/> are accepted for API
    /// stability but only <c>Margins</c> flow to the native layer; the
    /// rest of the engine is pinned at defaults. v0.2 will plumb
    /// metadata, page size, and tagging through the FFI surface.
    /// </remarks>
    public static byte[] FromHtml(string html, FulgurOptions? options)
    {
        if (html is null)
        {
            throw new ArgumentNullException(nameof(html));
        }

        // v0.1 only uses `options` to keep the call site stable; the
        // engine itself runs at defaults. We still validate the page
        // size enum so a future v0.2 that wires it up doesn't
        // accidentally accept a junk value.
        if (options is not null && !Enum.IsDefined(typeof(FulgurPageSize), options.PageSize))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), $"Unknown page size: {options.PageSize}");
        }

        // Encode UTF-8 once, on the managed side. The native call
        // accepts a byte* + length, so this stays zero-copy on the
        // return path: the engine writes its own PDF buffer.
        var utf8 = Encoding.UTF8.GetBytes(html);

        // Make sure the native lib is loaded. Idempotent.
        NativeLibraryLoader.EnsureRegistered();

        unsafe
        {
            fixed (byte* inputPtr = utf8)
            {
                ByteBuffer buffer;
                try
                {
                    buffer = NativeMethods.fulgur_render_html(inputPtr, utf8.Length);
                }
                catch (DllNotFoundException ex)
                {
                    throw new FulgurException(
                        $"Could not load '{NativeLibraryLoader.LibraryName}'. " +
                        "Make sure the FulgurNET NuGet package is restored and the " +
                        "current RID has a native binary in runtimes/<rid>/native/.",
                        ex);
                }

                try
                {
                    if (buffer.length <= 0)
                    {
                        // The native side reports failure as a zero-length
                        // buffer. v0.2 will surface the actual error string.
                        throw new FulgurException(
                            "fulgur_render_html returned no bytes. " +
                            "The HTML input may be invalid UTF-8 or the engine rejected the document.");
                    }

                    // `ToArray()` copies into managed memory so the caller
                    // can hold the PDF past the FFI free call.
                    return buffer.ToArray();
                }
                finally
                {
                    // Always release, even on the exception path.
                    NativeMethods.fulgur_free_buffer(buffer);
                }
            }
        }
    }

    /// <summary>
    /// Render an HTML string to a file at <paramref name="path"/>.
    /// Convenience wrapper around <see cref="FromHtml(string)"/> that
    /// handles the I/O.
    /// </summary>
    public static void FromHtmlToFile(string html, string path)
    {
        var bytes = FromHtml(html);
        System.IO.File.WriteAllBytes(path, bytes);
    }

    /// <summary>
    /// The version of the bundled native library, as reported by
    /// <c>fulgur_version</c>. Useful for diagnostics and smoke tests.
    /// </summary>
    public static string NativeVersion
    {
        get
        {
            NativeLibraryLoader.EnsureRegistered();
            unsafe
            {
                var p = NativeMethods.fulgur_version();
                if (p is null)
                {
                    return string.Empty;
                }
                // The native side returns a NUL-terminated UTF-8 string
                // backed by a process-lifetime static. `Marshal.PtrToStringUTF8`
                // (net7+) is the idiomatic way to materialize it as a
                // managed string.
                return Marshal.PtrToStringUTF8((IntPtr)p) ?? string.Empty;
            }
        }
    }
}
