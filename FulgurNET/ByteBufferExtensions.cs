// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Extension methods for the FFI `<ByteBuffer>` handle. Lives in a
// `partial` struct so we can keep the field declaration in `ByteBuffer.cs`
// (where the layout contract is documented) and the ergonomics here.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FulgurNET;

public unsafe partial struct ByteBuffer
{
    /// <summary>
    /// Borrow the buffer's bytes as a managed <see cref="Span{T}"/>.
    /// The span is invalidated as soon as the matching
    /// <c>NativeMethods.fulgur_free_buffer</c> is called — do not retain
    /// across that boundary.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> AsSpan()
    {
        // `ptr` may be null for an empty buffer; both Span and
        // `new Span<byte>(null, 0)` are well-defined per the BCL.
        return new Span<byte>(ptr, length);
    }

    /// <summary>
    /// Copy the buffer's bytes into a fresh managed array. Useful when
    /// the caller needs to outlive the underlying FFI buffer (e.g. to
    /// write to a `Stream` or `IFormFile`).
    /// </summary>
    public byte[] ToArray()
    {
        if (length == 0)
        {
            return Array.Empty<byte>();
        }
        return AsSpan().ToArray();
    }
}
