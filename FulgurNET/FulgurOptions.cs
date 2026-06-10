// SPDX-License-Identifier: MIT OR Apache-2.0
//
// FulgurOptions: high-level, hand-written configuration object that
// the FFI layer eventually translates into a `fulgur::EngineBuilder`.
// In v1 only a subset is wired up (everything maps to defaults), but
// the shape is what users will interact with, so it pays to settle it
// up front rather than break it later.

using System;
using System.Collections.Generic;

namespace FulgurNET;

/// <summary>
/// Document metadata written to the PDF's <c>Info</c> dictionary.
/// </summary>
public sealed class FulgurMetadata
{
    public string? Title { get; set; }
    public string? Author { get; set; }
    public IReadOnlyList<string>? Authors { get; set; }
    public string? Description { get; set; }
    public IReadOnlyList<string>? Keywords { get; set; }
    public string? Creator { get; set; }
    public string? Producer { get; set; }
    public string? Language { get; set; }
}

/// <summary>
/// Page geometry. Default is A4 portrait with the fulgur default
/// 20mm uniform margin.
/// </summary>
public enum FulgurPageSize
{
    A4,
    Letter,
    A3,
}

public sealed class FulgurOptions
{
    /// <summary>Logical page size. Default: <see cref="FulgurPageSize.A4"/>.</summary>
    public FulgurPageSize PageSize { get; set; } = FulgurPageSize.A4;

    /// <summary>Render in landscape orientation. Default: <c>false</c>.</summary>
    public bool Landscape { get; set; }

    /// <summary>
    /// Page margins in points (1 pt = 1/72 inch). Use <c>null</c> for
    /// the fulgur default (20mm uniform).
    /// </summary>
    public FulgurMargin? Margin { get; set; }

    /// <summary>Emit a PDF outline from the document's <c>h1</c>–<c>h6</c> tree.</summary>
    public bool Bookmarks { get; set; }

    /// <summary>Emit a tagged PDF structure tree (PDF/UA-1 friendly).</summary>
    public bool TaggedPdf { get; set; }

    /// <summary>Document metadata for the PDF <c>Info</c> dictionary.</summary>
    public FulgurMetadata? Metadata { get; set; }

    /// <summary>Extra CSS to inject after the document's own styles.</summary>
    public string? ExtraCss { get; set; }
}

/// <summary>
/// Page margins in points. Construct via <see cref="Uniform"/> for the
/// common case or set the four sides individually.
/// </summary>
public readonly struct FulgurMargin
{
    public float Top { get; }
    public float Right { get; }
    public float Bottom { get; }
    public float Left { get; }

    public FulgurMargin(float top, float right, float bottom, float left)
    {
        Top = top;
        Right = right;
        Bottom = bottom;
        Left = left;
    }

    /// <summary>Same margin on all four sides, in points.</summary>
    public static FulgurMargin Uniform(float pt) => new(pt, pt, pt, pt);

    /// <summary>Vertical and horizontal pairs, in points.</summary>
    public static FulgurMargin Symmetric(float vertical, float horizontal) =>
        new(vertical, horizontal, vertical, horizontal);
}
