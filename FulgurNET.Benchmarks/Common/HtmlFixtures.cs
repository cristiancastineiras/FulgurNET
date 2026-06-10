// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Shared HTML fixtures used by every competitor in the benchmark suite.
//
// Design constraints (so the comparison is honest):
//   * NO JavaScript — fulgur (the engine under test) is a pure
//     HTML/CSS renderer, so any fixture that requires JS execution
//     would unfairly penalize fulgur and unfairly advantage engines
//     that ship a browser (PuppeteerSharp/Chromium).
//   * CSS only uses features the fulgur CSS support list advertises
//     (https://github.com/fulgur-rs/fulgur/blob/main/docs/css-support.md).
//     DinkToPdf (wkhtmltopdf) and PuppeteerSharp both support the
//     same features and more, so anything fulgur renders correctly
//     should also render correctly in the competitors.
//   * No external assets (images, fonts, etc.) — they would force
//     the benchmarks to do network I/O and break the timing.

namespace FulgurNET.Benchmarks.Common;

/// <summary>
/// Static, deterministic HTML fixtures used by every benchmark.
/// Held as static readonly so the JIT doesn't optimize the literal
/// away (BenchmarkDotNet's documentation explicitly recommends
/// pinning inputs that way).
/// </summary>
public static class HtmlFixtures
{
    /// <summary>
    /// A small (~3 KB) "Hello PDF" document. Useful for measuring
    /// per-call overhead in the absence of significant work.
    /// </summary>
    public static readonly string Small =
        """
        <!doctype html>
        <html><head><meta charset="utf-8"><title>Hello</title>
        <style>
          body { font-family: sans-serif; margin: 2cm; }
          h1 { color: #2b5cb8; }
        </style></head>
        <body>
          <h1>Hello, FulgurNET</h1>
          <p>This is a tiny document used to measure per-call overhead.
             See <code>Medium</code> and <code>Large</code> for realistic
             workloads.</p>
        </body></html>
        """;

    /// <summary>
    /// A medium-sized "invoice" document (~12 KB) with headings, a
    /// table, a couple of inline-styled elements, and basic page
    /// break hints. This is the canonical benchmark target.
    /// </summary>
    public static readonly string Medium =
        """
        <!doctype html>
        <html><head><meta charset="utf-8"><title>Invoice 2026-001</title>
        <style>
          @page { size: A4; margin: 2cm; }
          body { font-family: sans-serif; color: #222; }
          h1 { color: #2b5cb8; border-bottom: 2px solid #2b5cb8; padding-bottom: 4px; }
          h2 { color: #555; margin-top: 1.5em; }
          table { width: 100%; border-collapse: collapse; margin-top: 1em; }
          th { background: #f0f0f0; text-align: left; padding: 6px 8px; border-bottom: 1px solid #ccc; }
          td { padding: 6px 8px; border-bottom: 1px solid #eee; }
          .total { font-weight: bold; text-align: right; margin-top: 1em; }
          .meta { color: #777; font-size: 0.9em; }
        </style></head>
        <body>
          <h1>Invoice #2026-001</h1>
          <p class="meta">Issued 2026-06-10 &middot; Due 2026-07-10 &middot; Acme Corp.</p>

          <h2>Bill to</h2>
          <p>Globex Industries<br>
             123 Market Street<br>
             Springfield, IL 62701</p>

          <h2>Items</h2>
          <table>
            <thead>
              <tr><th>SKU</th><th>Description</th><th>Qty</th><th>Unit</th><th>Line</th></tr>
            </thead>
            <tbody>
              <tr><td>W-001</td><td>Industrial widget, 10mm</td><td>12</td><td>$45.00</td><td>$540.00</td></tr>
              <tr><td>W-002</td><td>Industrial widget, 20mm</td><td>6</td><td>$72.00</td><td>$432.00</td></tr>
              <tr><td>G-100</td><td>Galvanized bracket</td><td>24</td><td>$8.50</td><td>$204.00</td></tr>
              <tr><td>C-050</td><td>Carriage bolt, M10</td><td>100</td><td>$0.35</td><td>$35.00</td></tr>
              <tr><td>C-051</td><td>Carriage bolt, M12</td><td>100</td><td>$0.50</td><td>$50.00</td></tr>
              <tr><td>P-200</td><td>Pneumatic coupler</td><td>4</td><td>$18.00</td><td>$72.00</td></tr>
              <tr><td>F-010</td><td>Inline filter, 1/4"</td><td>8</td><td>$12.00</td><td>$96.00</td></tr>
              <tr><td>S-005</td><td>Silicone seal, 30mm</td><td>40</td><td>$1.20</td><td>$48.00</td></tr>
              <tr><td>H-001</td><td>Hex key set, metric</td><td>2</td><td>$22.00</td><td>$44.00</td></tr>
              <tr><td>T-300</td><td>PTFE thread tape</td><td>20</td><td>$1.50</td><td>$30.00</td></tr>
            </tbody>
          </table>

          <p class="total">Subtotal: $1,551.00<br>
             Tax (8%): $124.08<br>
             <strong>Total: $1,675.08</strong></p>

          <h2>Notes</h2>
          <p>Payment is due within 30 days. Please reference the invoice
             number on your check or wire transfer. Late payments accrue
             a 1.5% monthly service charge. Thank you for your business.</p>
        </body></html>
        """;

    /// <summary>
    /// A larger document (~80 KB, 100+ paragraphs) designed to stress
    /// the layout engine and the page-break logic. Used to measure
    /// how each library scales with document size.
    /// </summary>
    public static string BuildLarge(int paragraphCount = 200)
    {
        var sb = new System.Text.StringBuilder(capacity: 100_000);
        sb.Append("""
            <!doctype html>
            <html><head><meta charset="utf-8"><title>Big document</title>
            <style>
              @page { size: A4; margin: 1.5cm; }
              body { font-family: Georgia, serif; line-height: 1.5; }
              h1 { color: #444; }
              h2 { color: #666; margin-top: 1.2em; }
              p  { text-align: justify; }
            </style></head>
            <body>
              <h1>Quarterly engineering retrospective</h1>
            """);
        for (int i = 0; i < paragraphCount; i++)
        {
            sb.Append("<h2>Section ").Append(i + 1).Append("</h2>");
            sb.Append("<p>Lorem ipsum dolor sit amet, consectetur adipiscing elit. ")
              .Append("Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. ")
              .Append("Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris ")
              .Append("nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in ")
              .Append("reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla ")
              .Append("pariatur. Excepteur sint occaecat cupidatat non proident, sunt in ")
              .Append("culpa qui officia deserunt mollit anim id est laborum. Paragraph index ")
              .Append(i).Append(".</p>");
        }
        sb.Append("</body></html>");
        return sb.ToString();
    }
}
