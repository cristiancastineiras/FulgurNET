// SPDX-License-Identifier: MIT OR Apache-2.0
//
// FFI-safe byte buffer for handing owned `Vec<u8>` allocations from Rust
// to C# without copying.
//
// Contract (must stay in sync with the C# `ByteBuffer` partial struct in
// `FulgurNET/ByteBufferExtensions.cs`):
//
//   * `ptr`     — heap pointer owned by Rust. The C# side MUST treat it
//                 as a borrowed `Span<byte>` of `length` bytes and MUST
//                 NOT call `Marshal.FreeHGlobal` / `Marshal.AllocHGlobal`.
//   * `length`  — number of valid bytes (NOT capacity).
//   * `capacity`— underlying allocation size in bytes. Used to reconstruct
//                 the original `Vec<u8>` on free.
//
// Memory ownership: the buffer is leaked out of the `Vec` via
// `ManuallyDrop`. The matching `fulgur_*_free_*` function reconstructs
// the `Vec` and drops it, returning the memory to the Rust allocator.
//
// The struct is `#[repr(C)]` because csbindgen mirrors it field-for-field
// into C# with `LayoutKind.Sequential`. Do not add `Drop` to the fields
// (e.g. `Vec<u8>`) — we manage the drop manually via `destroy()`.

use std::convert::TryFrom;
use std::mem::ManuallyDrop;

#[repr(C)]
pub struct ByteBuffer {
    pub ptr: *mut u8,
    pub length: i32,
    pub capacity: i32,
}

impl ByteBuffer {
    /// Wrap an owned `Vec<u8>` for hand-off to the C# side. The vector
    /// is **leaked** (via `ManuallyDrop`); ownership returns to Rust in
    /// `destroy()` / `destroy_into_vec()`.
    pub fn from_vec(bytes: Vec<u8>) -> Self {
        let length = i32::try_from(bytes.len())
            .expect("ByteBuffer length cannot fit into i32 (>2 GiB)");
        let capacity = i32::try_from(bytes.capacity())
            .expect("ByteBuffer capacity cannot fit into i32");

        let mut v = ManuallyDrop::new(bytes);
        Self {
            ptr: v.as_mut_ptr(),
            length,
            capacity,
        }
    }

    /// Reconstruct the original `Vec<u8>` and consume the buffer.
    /// Caller takes ownership of the returned vector.
    ///
    /// Safe to call on a buffer built from a zero-length input — returns
    /// an empty `Vec` without dereferencing `ptr`.
    pub fn destroy_into_vec(self) -> Vec<u8> {
        if self.ptr.is_null() {
            return Vec::new();
        }
        let capacity = usize::try_from(self.capacity)
            .expect("ByteBuffer capacity negative or overflowed");
        let length = usize::try_from(self.length)
            .expect("ByteBuffer length negative or overflowed");
        // SAFETY: `ptr` was produced by `from_vec` from a `Vec<u8>` with
        // the same (length, capacity). The C# side is contractually
        // required not to retain `ptr` past the matching free call.
        unsafe { Vec::from_raw_parts(self.ptr, length, capacity) }
    }

    /// Drop the buffer without handing the bytes back. Use this when
    /// the C# side copied what it needed and just wants the allocator
    /// to take it back.
    pub fn destroy(self) {
        drop(self.destroy_into_vec());
    }
}

// Compile-time sanity: keep the struct layout the C# side expects.
// On any platform where this stops holding the build fails loudly
// instead of silently corrupting memory at runtime.
const _: () = {
    // `*mut u8` is 8 bytes on 64-bit, 4 on 32-bit. We pin to 64-bit
    // because Fulgur's native output is x64/arm64 only.
    assert!(std::mem::size_of::<usize>() >= 8, "fulgur-native requires 64-bit target");
    assert!(std::mem::size_of::<ByteBuffer>() == 16, "ByteBuffer layout drift");
};

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn round_trip_non_empty() {
        let original: Vec<u8> = (0u8..=255).cycle().take(1024).collect();
        let buf = ByteBuffer::from_vec(original.clone());
        assert_eq!(buf.length, 1024);
        assert!(buf.capacity >= buf.length);
        assert!(!buf.ptr.is_null());

        let recovered = buf.destroy_into_vec();
        assert_eq!(recovered, original);
    }

    #[test]
    fn round_trip_empty() {
        let buf = ByteBuffer::from_vec(Vec::new());
        assert_eq!(buf.length, 0);
        assert!(buf.ptr.is_null() || buf.capacity == 0);
        let recovered = buf.destroy_into_vec();
        assert!(recovered.is_empty());
    }

    #[test]
    fn destroy_drops_without_panic() {
        let buf = ByteBuffer::from_vec(vec![1, 2, 3, 4, 5]);
        buf.destroy();
    }
}
