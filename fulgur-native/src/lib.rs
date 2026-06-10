// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Fulgur-native: a thin C-ABI layer over the fulgur HTML/CSS -> PDF
// engine. Lives in its own crate (separate from `fulgur` proper) so we
// can:
//   * Iterate on the SDK surface without touching upstream code.
//   * Bump the FFI shape at a different cadence than the engine.
//   * Ship the C# NuGet package with a single `fulgur_native.{dll,so,dylib}`
//     and one generated `NativeMethods.g.cs`.
//
// Threading: every public function is marked `unsafe extern "C"` because
// that's the C-ABI contract; the *implementation* is single-threaded and
// re-entrant at the call level (each call builds its own `Engine`).
// Concurrent calls are safe — `Engine` does not hold shared mutable state
// between `render_html` invocations — but the resulting `ByteBuffer` must
// be freed on the same thread that received it (the Rust allocator is
// per-thread for jemalloc on some targets).
//
// Error handling: when the engine returns `Err(Error)` we don't try to
// surface the error across the FFI in v1. The C# wrapper treats any
// `length == 0` return as "nothing came back" and reports a generic
// render failure. v2 will introduce an out-parameter for an UTF-8 error
// string; see FULG-7.

#![deny(unsafe_op_in_unsafe_fn)]
#![allow(missing_debug_implementations)]

mod buffer;

use std::slice;
use std::str;

use buffer::ByteBuffer;
use fulgur::Engine;

/// Convert an HTML string to PDF bytes using default settings.
///
/// Input is a UTF-8 byte slice (`html_ptr` / `html_len`). Returned
/// `ByteBuffer` ownership transfers to the caller; it must be released
/// with `fulgur_free_buffer`.
///
/// Returns a buffer with `length == 0` on any failure — see module docs.
/// (`ptr` is not guaranteed to be null on the failure path; the C#
/// wrapper checks `length` only.)
#[no_mangle]
pub unsafe extern "C" fn fulgur_render_html(
    html_ptr: *const u8,
    html_len: i32,
) -> ByteBuffer {
    // 1. Length sanity. A negative `html_len` is always wrong; clamp it
    //    to 0 and treat as empty input rather than panicking across the
    //    FFI boundary (panics across extern "C" are UB).
    let html_len = if html_len < 0 { 0 } else { html_len as usize };

    // 2. Build a UTF-8 slice. We accept `html_ptr == null` with
    //    `html_len == 0` as "empty document" because some C# code paths
    //    pass `fixed (byte* p = null)` for empty arrays. Anything else
    //    with a null pointer is an error.
    let html_slice = if html_ptr.is_null() {
        if html_len == 0 {
            &[][..]
        } else {
            return ByteBuffer::from_vec(Vec::new());
        }
    } else {
        // SAFETY: caller guarantees `html_ptr` is valid for `html_len`
        // readable bytes. We've already rejected null and negative
        // length above.
        unsafe { slice::from_raw_parts(html_ptr, html_len) }
    };

    // 3. Validate UTF-8. `Engine::render_html` is strict about this and
    //    returns `HtmlParse` on invalid bytes; failing fast here gives
    //    a clearer signal than letting the engine produce a degraded PDF.
    let html = match str::from_utf8(html_slice) {
        Ok(s) => s,
        Err(_) => return ByteBuffer::from_vec(Vec::new()),
    };

    // 4. Build an engine with default config and render. The engine
    //    holds CSS, fonts, and a parse cache, but in v1 the FFI surface
    //    doesn't expose any of those knobs — `build_default_engine`
    //    will build a throwaway engine per call. This is fine for a
    //    first cut; v2 adds `fulgur_engine_new` / `fulgur_engine_free`
    //    to amortize construction cost across many calls.
    let engine = build_default_engine();

    match engine.render_html(html) {
        Ok(pdf) => ByteBuffer::from_vec(pdf),
        Err(_) => ByteBuffer::from_vec(Vec::new()),
    }
}

fn build_default_engine() -> Engine {
    // We construct the builder explicitly (rather than calling
    // `Engine::builder()`) so we can later plumb config flags into the
    // underlying `Config` without touching every FFI entry point. Today
    // it is identical to `Engine::builder().build()`.
    Engine::builder().build()
}

/// Release a `ByteBuffer` previously returned by any `fulgur_render_*`
/// function. After this call, the buffer's `ptr` MUST NOT be used.
///
/// Calling `free_buffer` on a zero-length / null buffer is a no-op.
#[no_mangle]
pub unsafe extern "C" fn fulgur_free_buffer(buffer: ByteBuffer) {
    buffer.destroy();
}

/// Return the version of the bundled fulgur engine, as a NUL-terminated
/// UTF-8 C string. The returned pointer is to a process-lifetime static
/// — the caller MUST NOT free it.
///
/// This is mostly useful for diagnostics and NuGet smoke tests, but we
/// expose it from day one because the .NET package version needs to
/// match (and we can't read Cargo.toml at runtime from C#).
#[no_mangle]
pub extern "C" fn fulgur_version() -> *const std::os::raw::c_char {
    // `env!("CARGO_PKG_VERSION")` resolves at compile time. We embed
    // the matching NUL byte by going through a `CString` once.
    concat!(env!("CARGO_PKG_VERSION"), "\0").as_ptr() as *const _
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn render_empty_string_returns_valid_empty_pdf() {
        // A truly empty HTML document still produces a syntactically
        // valid PDF: it has a single blank page (krilla writes the
        // structure even when there's no content). The exact byte count
        // isn't part of the contract — only that we don't return the
        // error sentinel (length 0, ptr null).
        let buf = unsafe { fulgur_render_html(std::ptr::null(), 0) };
        assert!(!buf.ptr.is_null(), "expected non-null buffer for empty input");
        assert!(buf.length > 0, "expected non-zero length for empty input");
        unsafe { fulgur_free_buffer(buf) };
    }

    #[test]
    fn render_simple_html_starts_with_pdf_magic() {
        let html = "<html><body><h1>Hi</h1></body></html>";
        let bytes = html.as_bytes();
        let buf = unsafe { fulgur_render_html(bytes.as_ptr(), bytes.len() as i32) };
        assert!(!buf.ptr.is_null());
        assert!(buf.length > 4, "PDF header is at least 5 bytes");
        let head = unsafe { slice::from_raw_parts(buf.ptr, 5) };
        assert_eq!(&head[..4], b"%PDF", "output must start with %PDF magic");
        unsafe { fulgur_free_buffer(buf) };
    }

    #[test]
    fn render_invalid_utf8_returns_empty_buffer() {
        // 0xFF 0xFE is not legal UTF-8. The FFI contract is to surface
        // this as a zero-length buffer — the C# wrapper then raises a
        // clear exception. (Note: `ptr` is NOT guaranteed to be null on
        // a zero-length buffer; the C# contract checks `length` only.
        // `Vec::new()` on stable Rust leaves the dangling pointer
        // non-null but dangling, and `from_vec` mirrors that.)
        let bytes = [0xFFu8, 0xFE, 0xFD];
        let buf = unsafe { fulgur_render_html(bytes.as_ptr(), bytes.len() as i32) };
        assert_eq!(buf.length, 0, "invalid UTF-8 must produce zero-length buffer");
        // free on an empty buffer must be a no-op (no double-free).
        unsafe { fulgur_free_buffer(buf) };
    }

    #[test]
    fn negative_length_is_clamped() {
        let html = b"<p>x</p>";
        let buf = unsafe { fulgur_render_html(html.as_ptr(), -1) };
        // Negative length clamps to 0 -> we treat as empty document.
        // The engine still produces a valid (single blank page) PDF.
        assert!(buf.length > 0, "negative length should clamp to empty doc, not error");
        unsafe { fulgur_free_buffer(buf) };
    }

    #[test]
    fn version_returns_c_string() {
        let p = fulgur_version();
        assert!(!p.is_null());
        let cstr = unsafe { std::ffi::CStr::from_ptr(p) };
        let s = cstr.to_str().expect("version must be valid UTF-8");
        // Semver has at least one dot.
        assert!(s.contains('.'), "version '{}' should be semver", s);
    }
}
