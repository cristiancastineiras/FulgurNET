// SPDX-License-Identifier: MIT OR Apache-2.0

using System;

namespace FulgurNET;

/// <summary>
/// Thrown when the native fulgur engine reports a render failure.
/// In v0.1 the only signal we get is "no bytes came back" — the
/// exception message describes that. v0.2 will plumb the underlying
/// <c>fulgur::Error</c> variant across the FFI as a UTF-8 string.
/// </summary>
public sealed class FulgurException : Exception
{
    public FulgurException(string message) : base(message) { }
    public FulgurException(string message, Exception inner) : base(message, inner) { }
}
