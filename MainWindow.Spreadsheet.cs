using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Utils;

namespace Noted;

public partial class MainWindow
{
    private bool _spreadsheetSumSyncSuppress;

    /// <summary>Spaces between padded columns in legacy (non-pipe) rows.</summary>
    private const string SpreadsheetColumnGap = "  ";

    /// <summary>One space after each gap before numeric cells so digits sit one column right of the grid line.</summary>
    private const string SpreadsheetNumericLeadingMargin = " ";

    /// <summary>Spaces before the leading <c>|</c> so the table sits slightly inset from the chrome edge.</summary>
    private const string SpreadsheetPresentationLeftMargin = "  ";

    private readonly struct SpreadsheetColumnBounds
    {
        public int DescStart { get; init; }
        public int QtyStart { get; init; }
        public int UnitStart { get; init; }
        public int TotalStart { get; init; }

        /// <summary>2 = legacy <c>"  "</c> only between columns; 3 = gap + numeric margin; 0 = infer.</summary>
        public byte InterColumnSepLength { get; init; }

        /// <summary>
        /// Indices of the five <c>|</c> delimiters for <c>|cell|cell|cell|cell|</c> rows; <c>-1</c> when using legacy spacing.
        /// </summary>
        public int Pipe0 { get; init; }
        public int Pipe1 { get; init; }
        public int Pipe2 { get; init; }
        public int Pipe3 { get; init; }
        public int Pipe4 { get; init; }

        public bool IsPipeDelimited => Pipe0 >= 0;
    }

    private static bool TryParsePipeDelimitedSpreadsheetHeader(string headerLine, out SpreadsheetColumnBounds bounds)
    {
        bounds = default;
        var pipes = new List<int>(8);
        for (int i = 0; i < headerLine.Length; i++)
        {
            if (headerLine[i] == '|')
                pipes.Add(i);
        }

        if (pipes.Count < 5)
            return false;

        int p0 = pipes[0], p1 = pipes[1], p2 = pipes[2], p3 = pipes[3], p4 = pipes[4];
        if (p1 <= p0 || p2 <= p1 || p3 <= p2 || p4 <= p3)
            return false;

        string c0 = SliceSpreadsheetColumn(headerLine, p0 + 1, p1);
        string c1 = SliceSpreadsheetColumn(headerLine, p1 + 1, p2);
        string c2 = SliceSpreadsheetColumn(headerLine, p2 + 1, p3);
        string c3 = SliceSpreadsheetColumn(headerLine, p3 + 1, p4);
        if (!IsHeaderCells(new List<string> { c0, c1, c2, c3 }))
            return false;

        bounds = new SpreadsheetColumnBounds
        {
            Pipe0 = p0,
            Pipe1 = p1,
            Pipe2 = p2,
            Pipe3 = p3,
            Pipe4 = p4,
            DescStart = p0 + 1,
            QtyStart = p1 + 1,
            UnitStart = p2 + 1,
            TotalStart = p3 + 1,
            InterColumnSepLength = 0
        };
        return true;
    }

    private static bool TryGetSpreadsheetColumnBoundsFromHeader(string headerLine, out SpreadsheetColumnBounds bounds)
    {
        bounds = default;
        if (TryParsePipeDelimitedSpreadsheetHeader(headerLine, out bounds))
            return true;

        int d = headerLine.IndexOf("Description", StringComparison.OrdinalIgnoreCase);
        if (d < 0)
            return false;

        int gapLen = SpreadsheetColumnGap.Length;
        int g0 = headerLine.IndexOf(SpreadsheetColumnGap, d, StringComparison.Ordinal);
        if (g0 < 0)
            return false;

        // Column bounds must be the padded cell edges (same as emitted pipe/legacy rows), not IndexOf(label):
        // chrome grid lines used QtyStart/UnitStart/TotalStart and looked centered vs right-aligned numbers.
        ReadOnlySpan<char> qtyWord = "Quantity";
        ReadOnlySpan<char> unitWord = "Unit price";

        for (int marginExtra = SpreadsheetNumericLeadingMargin.Length; marginExtra >= 0; marginExtra--)
        {
            int sep = gapLen + marginExtra;
            int qtyStart = g0 + sep;

            int q = headerLine.IndexOf("Quantity", qtyStart, StringComparison.OrdinalIgnoreCase);
            if (q < qtyStart)
                continue;

            int g1 = headerLine.IndexOf(SpreadsheetColumnGap, q + qtyWord.Length, StringComparison.Ordinal);
            if (g1 < 0)
                continue;

            int unitStart = g1 + sep;

            int u = headerLine.IndexOf("Unit price", unitStart, StringComparison.OrdinalIgnoreCase);
            if (u < unitStart)
                continue;

            int g2 = headerLine.IndexOf(SpreadsheetColumnGap, u + unitWord.Length, StringComparison.Ordinal);
            if (g2 < 0)
                continue;

            int totalStart = g2 + sep;

            bounds = new SpreadsheetColumnBounds
            {
                DescStart = d,
                QtyStart = qtyStart,
                UnitStart = unitStart,
                TotalStart = totalStart,
                InterColumnSepLength = (byte)sep,
                Pipe0 = -1,
                Pipe1 = -1,
                Pipe2 = -1,
                Pipe3 = -1,
                Pipe4 = -1
            };

            if (IsHeaderCells(SplitSpreadsheetCells(headerLine, bounds)))
                return true;
        }

        int qFallback = headerLine.IndexOf("Quantity", StringComparison.OrdinalIgnoreCase);
        int uFallback = headerLine.IndexOf("Unit price", StringComparison.OrdinalIgnoreCase);
        int tWord = headerLine.IndexOf("Total", StringComparison.OrdinalIgnoreCase);
        if (qFallback <= d || uFallback <= qFallback || tWord <= uFallback)
            return false;

        bounds = new SpreadsheetColumnBounds
        {
            DescStart = d,
            QtyStart = qFallback,
            UnitStart = uFallback,
            TotalStart = tWord,
            InterColumnSepLength = 0,
            Pipe0 = -1,
            Pipe1 = -1,
            Pipe2 = -1,
            Pipe3 = -1,
            Pipe4 = -1
        };
        return IsHeaderCells(SplitSpreadsheetCells(headerLine, bounds));
    }

    private static string SpreadsheetTableLeadingPrefix(string lineText, SpreadsheetColumnBounds bounds)
    {
        if (bounds.IsPipeDelimited)
        {
            int take = Math.Clamp(bounds.Pipe0, 0, lineText.Length);
            return lineText[..take];
        }

        int d = Math.Clamp(bounds.DescStart, 0, lineText.Length);
        return lineText[..d];
    }

    /// <summary>
    /// True for <c>**Sum**</c>, or plain <c>Sum</c> when not part of a longer word (e.g. not <c>Summer</c>).
    /// </summary>
    private static bool SpreadsheetLineStartsWithSumLabel(ReadOnlySpan<char> t)
    {
        if (t.StartsWith("**Sum**", StringComparison.Ordinal))
            return true;
        if (!t.StartsWith("Sum", StringComparison.Ordinal))
            return false;
        return t.Length == 3 || !char.IsLetterOrDigit(t[3]);
    }

    /// <summary>Detects the spreadsheet total row using column geometry (preferred).</summary>
    private static bool IsSpreadsheetSumRowLine(string raw, SpreadsheetColumnBounds bounds)
    {
        var trimmedLine = raw.TrimStart();
        if (trimmedLine.StartsWith("**Sum**", StringComparison.Ordinal))
            return true;

        if (bounds.IsPipeDelimited)
        {
            if (raw.IndexOf('|') >= 0)
            {
                var cells = SplitSpreadsheetCells(raw, bounds);
                return cells.Count >= 1 && cells[0].Trim().Equals("Sum", StringComparison.Ordinal);
            }

            // Pipe-less sum row: aligns at the same column as the first cell on pipe data rows (Pipe0).
            if (bounds.Pipe0 <= raw.Length)
            {
                ReadOnlySpan<char> tail = raw.AsSpan(bounds.Pipe0).TrimStart();
                return SpreadsheetLineStartsWithSumLabel(tail);
            }

            return false;
        }

        var legacyCells = SplitSpreadsheetCells(raw, bounds);
        return legacyCells.Count >= 1 && legacyCells[0].Trim().Equals("Sum", StringComparison.Ordinal);
    }

    /// <summary>Detects sum row when header bounds are not available (e.g. scanning backward for the sum line).</summary>
    private static bool IsSpreadsheetSumRowLineLoose(string raw)
    {
        var t = raw.TrimStart();
        if (t.StartsWith("**Sum**", StringComparison.Ordinal))
            return true;

        int i0 = t.IndexOf('|');
        if (i0 >= 0)
        {
            int i1 = t.IndexOf('|', i0 + 1);
            if (i1 > i0)
            {
                var cell0 = t.AsSpan(i0 + 1, i1 - i0 - 1).Trim();
                if (cell0.SequenceEqual("Sum".AsSpan()))
                    return true;
            }
        }

        if (raw.IndexOf('|') < 0)
            return SpreadsheetLineStartsWithSumLabel(t);

        return false;
    }

    private static string SliceSpreadsheetColumn(string line, int start, int endExclusive)
    {
        start = Math.Clamp(start, 0, line.Length);
        endExclusive = Math.Clamp(endExclusive, start, line.Length);
        if (start >= endExclusive)
            return "";
        return line.Substring(start, endExclusive - start).Trim();
    }

    private static List<string> SplitSpreadsheetCells(string rawLine, SpreadsheetColumnBounds bounds)
    {
        if (bounds.IsPipeDelimited)
        {
            return
            [
                SliceSpreadsheetColumn(rawLine, bounds.DescStart, bounds.Pipe1),
                SliceSpreadsheetColumn(rawLine, bounds.QtyStart, bounds.Pipe2),
                SliceSpreadsheetColumn(rawLine, bounds.UnitStart, bounds.Pipe3),
                SliceSpreadsheetColumn(rawLine, bounds.TotalStart, bounds.Pipe4)
            ];
        }

        return
        [
            SliceSpreadsheetColumn(rawLine, bounds.DescStart, bounds.QtyStart),
            SliceSpreadsheetColumn(rawLine, bounds.QtyStart, bounds.UnitStart),
            SliceSpreadsheetColumn(rawLine, bounds.UnitStart, bounds.TotalStart),
            SliceSpreadsheetColumn(rawLine, bounds.TotalStart, rawLine.Length)
        ];
    }

    private static List<string> SplitSpreadsheetCellsLegacy(string rawLine)
    {
        var t = rawLine.TrimEnd();
        if (t.IndexOf('\t') >= 0)
            return t.Split('\t').Select(s => s.Trim()).ToList();
        if (t.IndexOf('|') >= 0)
            return t.Split('|').Select(s => s.Trim()).ToList();
        return Regex.Split(t.Trim(), @"\s{2,}")
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }

    /// <summary>Parses a row when column positions are unknown (e.g. legacy). Header lines match via embedded titles.</summary>
    private static List<string> SplitSpreadsheetCells(string rawLine)
    {
        if (TryGetSpreadsheetColumnBoundsFromHeader(rawLine, out var bounds))
            return SplitSpreadsheetCells(rawLine, bounds);
        return SplitSpreadsheetCellsLegacy(rawLine);
    }

    private static string PadSpreadsheetCellLeft(string text, int width)
    {
        if (text.Length >= width)
            return text;
        return text.PadRight(width);
    }

    /// <summary>Right-align numeric columns so the widest value sits flush before the next gap (grid line).</summary>
    private static string PadSpreadsheetNumericCell(string text, int width)
    {
        if (text.Length >= width)
            return text;
        return text.PadLeft(width);
    }

    /// <summary>Space + currency reserved on every Total cell so sums align like a spreadsheet (digits stack; SEK only on sum).</summary>
    private static int SpreadsheetTotalCurrencySuffixWidth(string currency)
    {
        currency = string.IsNullOrWhiteSpace(currency)
            ? SpreadsheetAmountHelpers.DefaultCurrency
            : currency.Trim();
        return 1 + currency.Length;
    }

    private static string SpreadsheetFormatTotalHeaderCell(int numericWidth, int totalColumnWidth, string currency)
    {
        int minTail = SpreadsheetTotalCurrencySuffixWidth(currency);
        int tail = Math.Max(minTail, totalColumnWidth - numericWidth);
        return PadSpreadsheetNumericCell("Total", numericWidth) + new string(' ', tail);
    }

    private static string SpreadsheetFormatTotalDataCell(
        string numericText, int numericWidth, int totalColumnWidth, string currency)
    {
        // Match Quantity / Unit price: right-align the value in the full column width. A split
        // numeric+suffix zone would leave short values hugging the left edge of the Total cell.
        _ = numericWidth;
        _ = currency;
        return PadSpreadsheetNumericCell(numericText, totalColumnWidth);
    }

    private static string SpreadsheetFormatTotalSumCell(
        string numericText, int numericWidth, int totalColumnWidth, string currency)
    {
        currency = string.IsNullOrWhiteSpace(currency)
            ? SpreadsheetAmountHelpers.DefaultCurrency
            : currency.Trim();
        string core = PadSpreadsheetNumericCell(numericText, numericWidth) + " " + currency;
        if (core.Length >= totalColumnWidth)
            return core;
        return PadSpreadsheetNumericCell(core, totalColumnWidth);
    }

    /// <summary><c>|description|qty|unit|total|</c> — numeric columns right-aligned inside fixed widths.</summary>
    private static string FormatSpreadsheetPipeRow(
        string c0, string c1, string c2, string c3,
        int w0, int w1, int w2, int w3)
    {
        return string.Concat(
            "|",
            PadSpreadsheetCellLeft(c0, w0),
            "|",
            PadSpreadsheetNumericCell(c1, w1),
            "|",
            PadSpreadsheetNumericCell(c2, w2),
            "|",
            PadSpreadsheetNumericCell(c3, w3),
            "|");
    }

    /// <summary>Legacy space-separated row (still parsed from old notes).</summary>
    private static string FormatSpreadsheetLegacyGapRow(
        string c0, string c1, string c2, string c3,
        int w0, int w1, int w2, int w3)
    {
        return string.Concat(
            PadSpreadsheetCellLeft(c0, w0),
            SpreadsheetColumnGap,
            SpreadsheetNumericLeadingMargin,
            PadSpreadsheetNumericCell(c1, w1),
            SpreadsheetColumnGap,
            SpreadsheetNumericLeadingMargin,
            PadSpreadsheetNumericCell(c2, w2),
            SpreadsheetColumnGap,
            SpreadsheetNumericLeadingMargin,
            PadSpreadsheetNumericCell(c3, w3));
    }

    /// <summary>
    /// Same column boundaries as <see cref="FormatSpreadsheetPipeRow"/> but with a space instead of <c>|</c>
    /// so totals line up with pipe rows while keeping the sum line pipe-free.
    /// </summary>
    private static string FormatSpreadsheetPipelessAlignedRow(
        string c0, string c1, string c2, string c3,
        int w0, int w1, int w2, int w3)
    {
        const string Sep = " ";
        return string.Concat(
            Sep,
            PadSpreadsheetCellLeft(c0, w0),
            Sep,
            PadSpreadsheetNumericCell(c1, w1),
            Sep,
            PadSpreadsheetNumericCell(c2, w2),
            Sep,
            PadSpreadsheetNumericCell(c3, w3));
    }

    private static void ComputeSpreadsheetColumnWidths(
        IReadOnlyList<SpreadsheetEditRowModel> rows,
        out int w0, out int w1, out int w2, out int w3)
    {
        w0 = "Description".Length;
        w1 = "Quantity".Length;
        w2 = "Unit price".Length;
        w3 = "Total".Length;
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Description)
                && string.IsNullOrWhiteSpace(row.Quantity)
                && string.IsNullOrWhiteSpace(row.UnitPrice))
                continue;

            string desc = string.IsNullOrWhiteSpace(row.Description) ? "Item" : row.Description.Trim();
            string qty = string.IsNullOrWhiteSpace(row.Quantity) ? "0" : row.Quantity.Trim();
            SpreadsheetAmountHelpers.TryParseDecimal(
                string.IsNullOrWhiteSpace(row.UnitPrice) ? "0" : row.UnitPrice.Trim(),
                out var uParsed);
            string unitMoney = SpreadsheetAmountHelpers.FormatNumber(uParsed);
            string totMoney = SpreadsheetAmountHelpers.FormatNumber(row.LineTotalValue);
            w0 = Math.Max(w0, desc.Length);
            w1 = Math.Max(w1, qty.Length);
            w2 = Math.Max(w2, unitMoney.Length);
            w3 = Math.Max(w3, totMoney.Length);
        }

        // Total column numeric zone width (suffix for currency is added when building / syncing rows).
        w3 = Math.Max(w3, Math.Max("Total".Length, SpreadsheetAmountHelpers.FormatNumber(0).Length));
    }

    private static bool TryFindSpreadsheetHeaderBoundsForDocumentLine(
        TextDocument doc, DocumentLine line, out SpreadsheetColumnBounds bounds)
    {
        bounds = default;
        if (!DocumentLineShowsSpreadsheetChrome(doc, line))
            return false;

        int openLineNum = -1;
        for (int i = line.LineNumber - 1; i >= 1; i--)
        {
            var ln = doc.GetLineByNumber(i);
            if (IsSpreadsheetOpenFenceLine(doc, ln))
            {
                openLineNum = i;
                break;
            }

            if (IsBareMarkdownCloseFenceLine(doc, ln))
                return false;
        }

        if (openLineNum < 0)
            return false;

        for (int i = openLineNum + 1; i <= doc.LineCount; i++)
        {
            var ln = doc.GetLineByNumber(i);
            if (IsBareMarkdownCloseFenceLine(doc, ln))
                return false;
            var raw = doc.GetText(ln.Offset, ln.Length);
            if (IsHeaderRowLine(raw, out bounds))
                return true;
        }

        return false;
    }

    private static List<int> FindSpreadsheetColumnBoundaryOffsetsInLine(string _, SpreadsheetColumnBounds bounds)
    {
        if (bounds.IsPipeDelimited)
            return [bounds.Pipe0, bounds.Pipe1, bounds.Pipe2, bounds.Pipe3, bounds.Pipe4];
        return [bounds.QtyStart, bounds.UnitStart, bounds.TotalStart];
    }

    private static List<int> FindLegacyTabOrPipeSeparatorIndices(string raw)
    {
        char sep = raw.Contains('\t') ? '\t' : '|';
        if (!raw.Contains(sep))
            return [];
        var list = new List<int>();
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == sep)
                list.Add(i);
        }
        return list;
    }

    private TextEditor? TryFindShortTermTabEditor(TextView textView)
    {
        foreach (var doc in _docs.Values)
        {
            if (ReferenceEquals(doc.Editor.TextArea.TextView, textView))
                return doc.Editor;
        }

        return null;
    }

    private bool SpreadsheetChromeActiveForTextView(TextView textView)
        => _renderSpreadsheet && TryFindShortTermTabEditor(textView) != null;

    private static bool IsSpreadsheetOpenFenceLine(TextDocument doc, DocumentLine line)
    {
        if (line.Length < 3)
            return false;
        var t = doc.GetText(line.Offset, line.Length).Trim();
        return t.Equals("```spreadsheet", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBareMarkdownCloseFenceLine(TextDocument doc, DocumentLine line)
    {
        if (line.Length < 3)
            return false;
        var t = doc.GetText(line.Offset, line.Length).Trim();
        return t == "```";
    }

    private static bool DocumentLineIsSpreadsheetFenceDelimiter(TextDocument doc, DocumentLine line)
    {
        if (IsSpreadsheetOpenFenceLine(doc, line))
            return true;

        bool insideSpreadsheet = false;
        foreach (var prior in doc.Lines)
        {
            if (prior.Offset >= line.Offset)
                break;
            if (IsSpreadsheetOpenFenceLine(doc, prior))
                insideSpreadsheet = true;
            else if (insideSpreadsheet && IsBareMarkdownCloseFenceLine(doc, prior))
                insideSpreadsheet = false;
        }

        return insideSpreadsheet && IsBareMarkdownCloseFenceLine(doc, line);
    }

    private static bool DocumentLineShowsSpreadsheetChrome(TextDocument doc, DocumentLine line)
    {
        bool inside = false;
        foreach (var prior in doc.Lines)
        {
            if (prior.Offset >= line.Offset)
                break;
            if (IsSpreadsheetOpenFenceLine(doc, prior))
                inside = true;
            else if (inside && IsBareMarkdownCloseFenceLine(doc, prior))
                inside = false;
        }

        if (IsSpreadsheetOpenFenceLine(doc, line))
            return true;
        if (inside && IsBareMarkdownCloseFenceLine(doc, line))
            return true;
        return inside;
    }

    internal static bool TryGetSpreadsheetBlockSpanContainingOffset(
        TextDocument doc, int offset, out int spanStart, out int spanLength)
    {
        spanStart = 0;
        spanLength = 0;
        if (doc.LineCount == 0)
            return false;

        offset = Math.Max(0, Math.Min(offset, doc.TextLength));
        var caretLineNum = doc.GetLineByOffset(offset).LineNumber;

        bool inside = false;
        int openLineNum = 0;
        int openOffset = 0;

        for (int lineNum = 1; lineNum <= doc.LineCount; lineNum++)
        {
            var line = doc.GetLineByNumber(lineNum);
            if (inside)
            {
                if (IsBareMarkdownCloseFenceLine(doc, line))
                {
                    int closeEnd = line.EndOffset;
                    inside = false;
                    if (caretLineNum >= openLineNum && caretLineNum <= lineNum)
                    {
                        spanStart = openOffset;
                        spanLength = closeEnd - openOffset;
                        return spanLength > 0;
                    }
                }
            }
            else if (IsSpreadsheetOpenFenceLine(doc, line))
            {
                inside = true;
                openLineNum = lineNum;
                openOffset = line.Offset;
            }
        }

        return false;
    }

    private static bool IsHeaderCells(IReadOnlyList<string> parts)
    {
        if (parts.Count < 4)
            return false;
        return parts[0].Equals("Description", StringComparison.OrdinalIgnoreCase)
               && parts[1].Equals("Quantity", StringComparison.OrdinalIgnoreCase)
               && parts[2].Equals("Unit price", StringComparison.OrdinalIgnoreCase)
               && parts[3].Equals("Total", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHeaderRowLine(string raw, out SpreadsheetColumnBounds bounds)
        => TryGetSpreadsheetColumnBoundsFromHeader(raw, out bounds);

    private static bool IsHeaderRowLine(string raw)
        => IsHeaderRowLine(raw, out _);

    /// <summary>Matches trailing <c>amount CUR</c> on sum rows (e.g. <c>150457.5 USD</c>).</summary>
    private static readonly Regex SpreadsheetSumLineTrailingCurrency = new(
        @"(\d+(?:\.\d+)?)\s+([A-Za-z]{2,8})\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static bool TryParseCurrencyFromSumLineText(string raw, out string currency)
    {
        currency = null!;
        var m = SpreadsheetSumLineTrailingCurrency.Match(raw.TrimEnd());
        if (!m.Success)
            return false;
        currency = m.Groups[2].Value;
        return !string.IsNullOrWhiteSpace(currency);
    }

    private static string? TryReadCurrencyFromSumRowAfterHeader(TextDocument doc, int headerLineNumber)
    {
        for (int lineNum = headerLineNumber + 1; lineNum <= doc.LineCount; lineNum++)
        {
            var ln = doc.GetLineByNumber(lineNum);
            if (IsBareMarkdownCloseFenceLine(doc, ln))
                break;

            var raw = doc.GetText(ln.Offset, ln.Length);
            if (!IsSpreadsheetSumRowLineLoose(raw))
                continue;

            return TryParseCurrencyFromSumLineText(raw, out var c) ? c : null;
        }

        return null;
    }

    private static bool TryParseCurrencyFromSumLineInLineArray(string[] lines, out string currency)
    {
        currency = null!;
        for (int j = lines.Length - 1; j >= 0; j--)
        {
            var raw = lines[j];
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            if (raw.Trim().Equals("```", StringComparison.Ordinal))
                continue;
            if (!IsSpreadsheetSumRowLineLoose(raw))
                continue;
            return TryParseCurrencyFromSumLineText(raw, out currency);
        }

        return false;
    }

    private static string ReadSpreadsheetCurrencyBetweenFenceAndHeader(
        TextDocument doc, DocumentLine openFence, int headerLineNumber)
    {
        var nonEmpty = new List<string>();
        for (int lineNum = openFence.LineNumber + 1; lineNum < headerLineNumber; lineNum++)
        {
            var ln = doc.GetLineByNumber(lineNum);
            var raw = doc.GetText(ln.Offset, ln.Length).Trim();
            if (raw.Length == 0)
                continue;
            nonEmpty.Add(raw);
        }

        if (nonEmpty.Count <= 1)
        {
            string? sumCur = TryReadCurrencyFromSumRowAfterHeader(doc, headerLineNumber);
            return string.IsNullOrWhiteSpace(sumCur)
                ? SpreadsheetAmountHelpers.DefaultCurrency
                : sumCur;
        }

        var cur = nonEmpty[1].Trim();
        if (string.IsNullOrWhiteSpace(cur))
        {
            string? sumCur = TryReadCurrencyFromSumRowAfterHeader(doc, headerLineNumber);
            return string.IsNullOrWhiteSpace(sumCur)
                ? SpreadsheetAmountHelpers.DefaultCurrency
                : sumCur;
        }

        return cur;
    }

    /// <summary>
    /// Second non-empty line after the opening fence and before the header (legacy currency row); hidden in the UI.
    /// </summary>
    private static bool DocumentLineIsSpreadsheetCurrencyLine(TextDocument doc, DocumentLine line)
    {
        if (!DocumentLineShowsSpreadsheetChrome(doc, line))
            return false;
        if (DocumentLineIsSpreadsheetFenceDelimiter(doc, line))
            return false;

        int openLineNum = -1;
        for (int i = line.LineNumber - 1; i >= 1; i--)
        {
            var ln = doc.GetLineByNumber(i);
            if (IsSpreadsheetOpenFenceLine(doc, ln))
            {
                openLineNum = i;
                break;
            }

            if (IsBareMarkdownCloseFenceLine(doc, ln))
                return false;
        }

        if (openLineNum < 0)
            return false;

        DocumentLine? currencyCandidate = null;
        int nonEmptyBeforeHeader = 0;
        for (int lineNum = openLineNum + 1; lineNum <= doc.LineCount; lineNum++)
        {
            var ln = doc.GetLineByNumber(lineNum);
            if (IsBareMarkdownCloseFenceLine(doc, ln))
                break;

            var raw = doc.GetText(ln.Offset, ln.Length).Trim();
            if (raw.Length == 0)
                continue;

            if (IsHeaderRowLine(raw))
                break;

            nonEmptyBeforeHeader++;
            if (nonEmptyBeforeHeader == 2)
            {
                currencyCandidate = ln;
                break;
            }
        }

        return currencyCandidate != null && currencyCandidate.LineNumber == line.LineNumber;
    }

    private static string BuildDefaultSpreadsheetSectionText()
    {
        var rows = new List<SpreadsheetEditRowModel> { SpreadsheetEditRowModel.CreateDefault() };
        return BuildSpreadsheetSectionTextFromEdit(
            "Spreadsheet", SpreadsheetAmountHelpers.DefaultCurrency, rows);
    }

    private void InsertSpreadsheetSection(TextEditor editor)
    {
        if (editor.Document == null)
            return;

        string marker = BuildDefaultSpreadsheetSectionText();
        int replaceStart = editor.SelectionStart;
        int replaceLength = editor.SelectionLength;
        int anchorOffset = Math.Max(0, Math.Min(replaceStart, editor.Document.TextLength));
        var anchorLine = editor.Document.GetLineByOffset(anchorOffset);

        bool needsLeadingNewline = anchorOffset > anchorLine.Offset;
        bool needsTrailingNewline = anchorOffset < anchorLine.EndOffset;
        string replacement =
            $"{(needsLeadingNewline ? Environment.NewLine : string.Empty)}{marker}{(needsTrailingNewline ? Environment.NewLine : string.Empty)}";

        _spreadsheetSumSyncSuppress = true;
        try
        {
            editor.Document.Replace(replaceStart, replaceLength, replacement);
        }
        finally
        {
            _spreadsheetSumSyncSuppress = false;
        }

        editor.TextArea.Caret.Offset = replaceStart + replacement.Length;
        editor.Select(editor.TextArea.Caret.Offset, 0);
        editor.TextArea.TextView.Redraw();
    }

    private static void RemoveSpreadsheetSection(TextEditor editor)
    {
        if (editor.Document == null)
            return;

        int caret = editor.TextArea.Caret.Offset;
        if (!TryGetSpreadsheetBlockSpanContainingOffset(editor.Document, caret, out int start, out int length))
            return;

        editor.Document.Replace(start, length, string.Empty);
        int newCaret = Math.Max(0, Math.Min(start, editor.Document.TextLength));
        editor.TextArea.Caret.Offset = newCaret;
        editor.Select(newCaret, 0);
        editor.TextArea.TextView.Redraw();
    }

    private void TrySyncSpreadsheetSums(TextEditor editor)
    {
        if (_spreadsheetSumSyncSuppress)
            return;
        if (FindDocByEditor(editor) == null || editor.Document == null)
            return;

        _spreadsheetSumSyncSuppress = true;
        try
        {
            bool any = false;
            if (TryMutateSpreadsheetDataRowTotals(editor.Document))
                any = true;
            if (TryMutateSpreadsheetSumLines(editor.Document))
                any = true;
            if (any)
                editor.TextArea.TextView.Redraw();
        }
        finally
        {
            _spreadsheetSumSyncSuppress = false;
        }
    }

    private static bool TryMutateSpreadsheetDataRowTotals(TextDocument doc)
    {
        var blocks = new List<(int openLine, int headerLine, int sumLine)>();
        bool inside = false;
        int curOpen = -1;
        int curHeader = -1;
        int curSum = -1;

        for (int lineNum = 1; lineNum <= doc.LineCount; lineNum++)
        {
            var line = doc.GetLineByNumber(lineNum);
            if (!inside)
            {
                if (IsSpreadsheetOpenFenceLine(doc, line))
                {
                    inside = true;
                    curOpen = lineNum;
                    curHeader = -1;
                    curSum = -1;
                }
                continue;
            }

            if (IsBareMarkdownCloseFenceLine(doc, line))
            {
                blocks.Add((curOpen, curHeader, curSum));
                inside = false;
                continue;
            }

            var raw = doc.GetText(line.Offset, line.Length);
            if (curHeader < 0 && IsHeaderRowLine(raw))
                curHeader = lineNum;
            else if (curHeader >= 0)
            {
                var hLn = doc.GetLineByNumber(curHeader);
                var hRaw = doc.GetText(hLn.Offset, hLn.Length);
                if (TryGetSpreadsheetColumnBoundsFromHeader(hRaw, out var hb)
                    && IsSpreadsheetSumRowLine(raw, hb))
                    curSum = lineNum;
            }
            else if (IsSpreadsheetSumRowLineLoose(raw))
                curSum = lineNum;
        }

        bool any = false;
        foreach (var b in blocks)
        {
            if (b.openLine < 0 || b.headerLine < 0 || b.sumLine < 0)
                continue;

            var openFence = doc.GetLineByNumber(b.openLine);
            var headerLineDoc = doc.GetLineByNumber(b.headerLine);
            var headerRaw = doc.GetText(headerLineDoc.Offset, headerLineDoc.Length);
            if (!TryGetSpreadsheetColumnBoundsFromHeader(headerRaw, out var bounds))
                continue;

            string currency = ReadSpreadsheetCurrencyBetweenFenceAndHeader(doc, openFence, b.headerLine);

            decimal grandTotal = 0;
            for (int lineNum = b.headerLine + 1; lineNum < b.sumLine; lineNum++)
            {
                var ln = doc.GetLineByNumber(lineNum);
                var raw = doc.GetText(ln.Offset, ln.Length);
                var trimmed = raw.Trim();
                if (trimmed.Length == 0)
                    continue;
                if (!RowLooksLikeSpreadsheetDataRow(raw, bounds))
                    continue;
                if (IsSpreadsheetSumRowLine(raw, bounds))
                    continue;
                if (IsHeaderRowLine(raw))
                    continue;

                var contrib = ParseSpreadsheetDataRowContribution(raw, bounds);
                if (!SpreadsheetAmountHelpers.TrySafeAdd(grandTotal, contrib, out grandTotal))
                    continue;
            }

            ComputeSpreadsheetBlockLayoutWidths(
                doc, b.headerLine, b.sumLine, bounds, headerRaw, grandTotal, currency,
                out int layoutW0, out int layoutW1, out int layoutW2,
                out int layoutW3Num, out int layoutW3Full);

            // Rewrite data rows before the header so SplitSpreadsheetCells still uses the prior header geometry.
            for (int lineNum = b.sumLine - 1; lineNum > b.headerLine; lineNum--)
            {
                if (lineNum < 1 || lineNum > doc.LineCount)
                    continue;
                var line = doc.GetLineByNumber(lineNum);
                var raw = doc.GetText(line.Offset, line.Length);
                if (TryRewriteSpreadsheetDataRowFull(
                        doc, line, raw, bounds, headerRaw, currency,
                        layoutW0, layoutW1, layoutW2, layoutW3Num, layoutW3Full))
                    any = true;
            }

            headerLineDoc = doc.GetLineByNumber(b.headerLine);
            if (TryRewriteSpreadsheetHeaderRow(
                    doc, headerLineDoc, currency,
                    layoutW0, layoutW1, layoutW2, layoutW3Num, layoutW3Full))
                any = true;
        }

        return any;
    }

    private static void GetSpreadsheetColumnWidthsFromBounds(SpreadsheetColumnBounds bounds, out int w0, out int w1, out int w2)
    {
        if (bounds.IsPipeDelimited)
        {
            w0 = Math.Max(1, bounds.Pipe1 - bounds.Pipe0 - 1);
            w1 = Math.Max(1, bounds.Pipe2 - bounds.Pipe1 - 1);
            w2 = Math.Max(1, bounds.Pipe3 - bounds.Pipe2 - 1);
            return;
        }

        int[] seps = bounds.InterColumnSepLength != 0
            ? [bounds.InterColumnSepLength]
            :
            [
                SpreadsheetColumnGap.Length + SpreadsheetNumericLeadingMargin.Length,
                SpreadsheetColumnGap.Length
            ];

        foreach (int sep in seps)
        {
            w0 = bounds.QtyStart - bounds.DescStart - sep;
            w1 = bounds.UnitStart - bounds.QtyStart - sep;
            w2 = bounds.TotalStart - bounds.UnitStart - sep;
            if (w0 >= 1 && w1 >= 1 && w2 >= 1)
                return;
        }

        int fb = SpreadsheetColumnGap.Length + SpreadsheetNumericLeadingMargin.Length;
        w0 = Math.Max(1, bounds.QtyStart - bounds.DescStart - fb);
        w1 = Math.Max(1, bounds.UnitStart - bounds.QtyStart - fb);
        w2 = Math.Max(1, bounds.TotalStart - bounds.UnitStart - fb);
    }

    private static bool TryRewriteSpreadsheetHeaderRow(
        TextDocument doc, DocumentLine headerLineDoc, string currency,
        int w0, int w1, int w2, int totalNumericWidth, int totalColumnWidth)
    {
        string old = doc.GetText(headerLineDoc.Offset, headerLineDoc.Length);
        if (!TryGetSpreadsheetColumnBoundsFromHeader(old, out var ob))
            return false;

        string prefix = SpreadsheetTableLeadingPrefix(old, ob);
        string tableMargin = ob.IsPipeDelimited ? "" : SpreadsheetPresentationLeftMargin;
        string newHeader = prefix + tableMargin + FormatSpreadsheetPipeRow(
            "Description", "Quantity", "Unit price",
            SpreadsheetFormatTotalHeaderCell(totalNumericWidth, totalColumnWidth, currency),
            w0, w1, w2, totalColumnWidth);
        if (string.Equals(old, newHeader, StringComparison.Ordinal))
            return false;

        doc.Replace(headerLineDoc.Offset, headerLineDoc.Length, newHeader);
        return true;
    }

    private static bool TryRewriteSpreadsheetDataRowFull(
        TextDocument doc, DocumentLine line, string raw,
        SpreadsheetColumnBounds bounds, string headerLineRaw, string currency,
        int w0, int w1, int w2, int totalNumericWidth, int totalColumnWidth)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
            return false;
        if (IsSpreadsheetSumRowLine(raw, bounds))
            return false;
        if (IsHeaderRowLine(raw))
            return false;

        var parts = SplitSpreadsheetCells(raw, bounds);
        if (parts.Count < 4)
            return false;

        string desc = string.IsNullOrWhiteSpace(parts[0]) ? "Item" : parts[0].Trim();
        if (!SpreadsheetAmountHelpers.TryParseDecimal(parts[1], out var qty)
            || !SpreadsheetAmountHelpers.TryParseDecimal(parts[2], out var unit)
            || !SpreadsheetAmountHelpers.TrySafeMultiply(qty, unit, out var computed))
            return false;

        string qtyDisp = SpreadsheetAmountHelpers.FormatNumber(qty);
        string unitDisp = SpreadsheetAmountHelpers.FormatNumber(unit);
        string totDisp = SpreadsheetAmountHelpers.FormatNumber(computed);

        if (!TryGetSpreadsheetColumnBoundsFromHeader(headerLineRaw, out var hb))
            return false;

        string prefix = SpreadsheetTableLeadingPrefix(headerLineRaw, hb);
        string tableMargin = hb.IsPipeDelimited ? "" : SpreadsheetPresentationLeftMargin;
        string col3 = SpreadsheetFormatTotalDataCell(totDisp, totalNumericWidth, totalColumnWidth, currency);
        string newLine = prefix + tableMargin + FormatSpreadsheetPipeRow(desc, qtyDisp, unitDisp, col3, w0, w1, w2, totalColumnWidth);
        if (string.Equals(raw, newLine, StringComparison.Ordinal))
            return false;

        doc.Replace(line.Offset, line.Length, newLine);
        return true;
    }

    private static void ComputeSpreadsheetBlockLayoutWidths(
        TextDocument doc, int headerLineNum, int sumLineNum, SpreadsheetColumnBounds bounds,
        string headerRaw, decimal grandTotal, string currency,
        out int w0, out int w1, out int w2, out int w3Num, out int w3Full)
    {
        GetSpreadsheetColumnWidthsFromBounds(bounds, out w0, out w1, out w2);

        w0 = Math.Max(w0, "Description".Length);
        w1 = Math.Max(w1, "Quantity".Length);
        w2 = Math.Max(w2, "Unit price".Length);

        int headerTailLen = bounds.IsPipeDelimited
            ? Math.Max(0, bounds.Pipe4 - bounds.Pipe3 - 1)
            : headerRaw.Length >= bounds.TotalStart
                ? headerRaw.Length - bounds.TotalStart
                : 0;

        int sfx = SpreadsheetTotalCurrencySuffixWidth(currency);
        w3Num = Math.Max(
            Math.Max("Total".Length, SpreadsheetAmountHelpers.FormatNumber(grandTotal).Length),
            SpreadsheetAmountHelpers.FormatNumber(0).Length);

        for (int lineNum = headerLineNum + 1; lineNum < sumLineNum; lineNum++)
        {
            var ln = doc.GetLineByNumber(lineNum);
            var raw = doc.GetText(ln.Offset, ln.Length);
            var trimmed = raw.Trim();
            if (trimmed.Length == 0)
                continue;
            if (!RowLooksLikeSpreadsheetDataRow(raw, bounds))
                continue;
            if (IsSpreadsheetSumRowLine(raw, bounds))
                continue;
            if (IsHeaderRowLine(raw))
                continue;

            var parts = SplitSpreadsheetCells(raw, bounds);
            if (parts.Count < 4)
                continue;

            if (!SpreadsheetAmountHelpers.TryParseDecimal(parts[1], out var qty)
                || !SpreadsheetAmountHelpers.TryParseDecimal(parts[2], out var unit)
                || !SpreadsheetAmountHelpers.TrySafeMultiply(qty, unit, out var computed))
                continue;

            string desc = string.IsNullOrWhiteSpace(parts[0]) ? "Item" : parts[0].Trim();
            w0 = Math.Max(w0, desc.Length);
            w1 = Math.Max(w1, SpreadsheetAmountHelpers.FormatNumber(qty).Length);
            w2 = Math.Max(w2, SpreadsheetAmountHelpers.FormatNumber(unit).Length);
            w3Num = Math.Max(w3Num, SpreadsheetAmountHelpers.FormatNumber(computed).Length);
        }

        w0 = Math.Max(w0, "Sum".Length);
        w3Num = Math.Max(w3Num, 1);
        w3Full = Math.Max(w3Num + sfx, headerTailLen);
    }

    private static bool TryMutateSpreadsheetSumLines(TextDocument doc)
    {
        bool any = false;
        bool inside = false;
        DocumentLine? openLine = null;

        for (int lineNum = 1; lineNum <= doc.LineCount; lineNum++)
        {
            var line = doc.GetLineByNumber(lineNum);
            if (!inside)
            {
                if (IsSpreadsheetOpenFenceLine(doc, line))
                {
                    inside = true;
                    openLine = line;
                }

                continue;
            }

            if (IsBareMarkdownCloseFenceLine(doc, line))
            {
                if (openLine != null && TryUpdateSumForClosedSpreadsheetBlock(doc, openLine, line))
                    any = true;
                inside = false;
                openLine = null;
            }
        }

        return any;
    }

    private static bool TryUpdateSumForClosedSpreadsheetBlock(
        TextDocument doc, DocumentLine openFence, DocumentLine closeFence)
    {
        int headerLineNum = -1;
        DocumentLine? sumLine = null;

        for (int i = closeFence.LineNumber - 1; i > openFence.LineNumber; i--)
        {
            var ln = doc.GetLineByNumber(i);
            var raw = doc.GetText(ln.Offset, ln.Length);
            var trimmed = raw.Trim();
            if (trimmed.Length == 0)
                continue;
            if (IsSpreadsheetSumRowLineLoose(raw))
            {
                sumLine = ln;
                break;
            }
        }

        if (sumLine == null)
            return false;

        for (int i = openFence.LineNumber + 1; i < sumLine.LineNumber; i++)
        {
            var ln = doc.GetLineByNumber(i);
            var raw = doc.GetText(ln.Offset, ln.Length);
            if (!IsHeaderRowLine(raw))
                continue;
            headerLineNum = i;
            break;
        }

        if (headerLineNum < 0)
            return false;

        string currency = ReadSpreadsheetCurrencyBetweenFenceAndHeader(doc, openFence, headerLineNum);

        var headerLn = doc.GetLineByNumber(headerLineNum);
        var headerRaw = doc.GetText(headerLn.Offset, headerLn.Length);
        if (!TryGetSpreadsheetColumnBoundsFromHeader(headerRaw, out var bounds))
            return false;

        decimal sum = 0;
        for (int i = headerLineNum + 1; i < sumLine.LineNumber; i++)
        {
            var ln = doc.GetLineByNumber(i);
            var raw = doc.GetText(ln.Offset, ln.Length);
            var trimmed = raw.Trim();
            if (trimmed.Length == 0)
                continue;
            if (!RowLooksLikeSpreadsheetDataRow(raw, bounds))
                continue;
            if (IsSpreadsheetSumRowLine(raw, bounds))
                continue;
            if (IsHeaderRowLine(raw))
                continue;

            var contrib = ParseSpreadsheetDataRowContribution(raw, bounds);
            if (!SpreadsheetAmountHelpers.TrySafeAdd(sum, contrib, out sum))
                return false;
        }

        string sumNumeric = SpreadsheetAmountHelpers.FormatNumber(sum);
        string sumLineText = doc.GetText(sumLine.Offset, sumLine.Length);

        ComputeSpreadsheetBlockLayoutWidths(
            doc, headerLineNum, sumLine.LineNumber, bounds, headerRaw, sum, currency,
            out int layoutW0, out int layoutW1, out int layoutW2,
            out int layoutW3Num, out int layoutW3Full);

        string newLine = BuildSumLinePreservingIndent(
            sumLineText, sumNumeric, headerRaw, currency,
            layoutW0, layoutW1, layoutW2, layoutW3Num, layoutW3Full);
        if (newLine == sumLineText)
            return false;

        // Use Length, not TotalLength — TotalLength includes the line delimiter; replacing it merges the next line (```).
        doc.Replace(sumLine.Offset, sumLine.Length, newLine);
        return true;
    }

    private static bool RowLooksLikeSpreadsheetDataRow(string raw, SpreadsheetColumnBounds bounds)
    {
        if (IsSpreadsheetSumRowLine(raw, bounds))
            return false;
        var cells = SplitSpreadsheetCells(raw, bounds);
        if (cells.Count < 4)
            return false;
        return cells.Any(c => c.Length > 0);
    }

    private static decimal ParseSpreadsheetDataRowContribution(string raw, SpreadsheetColumnBounds bounds)
    {
        var parts = SplitSpreadsheetCells(raw, bounds);
        if (parts.Count < 4)
            return 0;

        bool hasTotal = SpreadsheetAmountHelpers.TryParseDecimal(parts[3], out var totalCol);
        bool hasQty = SpreadsheetAmountHelpers.TryParseDecimal(parts[1], out var qty);
        bool hasUnit = SpreadsheetAmountHelpers.TryParseDecimal(parts[2], out var unit);

        if (hasTotal)
            return totalCol;
        if (hasQty && hasUnit && SpreadsheetAmountHelpers.TrySafeMultiply(qty, unit, out var prod))
            return prod;
        return 0;
    }

    private static string BuildSumLinePreservingIndent(
        string originalLine,
        string sumNumericText,
        string headerLine,
        string currency,
        int w0,
        int w1,
        int w2,
        int totalNumericWidth,
        int totalColumnWidth)
    {
        int ws = 0;
        while (ws < originalLine.Length && char.IsWhiteSpace(originalLine[ws]))
            ws++;

        string indent = originalLine[..ws];
        if (!TryGetSpreadsheetColumnBoundsFromHeader(headerLine, out var hb))
        {
            SpreadsheetAmountHelpers.TryParseDecimal(sumNumericText, out var sumDec);
            string fallback = SpreadsheetAmountHelpers.FormatSumWithCurrency(sumDec, currency);
            int wc = Math.Max(totalColumnWidth, Math.Max(fallback.Length, 1));
            string fbCore = SpreadsheetPresentationLeftMargin + FormatSpreadsheetPipelessAlignedRow(
                "Sum",
                "",
                "",
                PadSpreadsheetNumericCell(fallback, wc),
                Math.Max(w0, "Sum".Length),
                Math.Max(w1, 1),
                Math.Max(w2, 1),
                wc);
            return indent + fbCore;
        }

        string tablePrefix = SpreadsheetTableLeadingPrefix(headerLine, hb);
        string tableMargin = hb.IsPipeDelimited ? "" : SpreadsheetPresentationLeftMargin;
        string totalCell = SpreadsheetFormatTotalSumCell(
            sumNumericText, totalNumericWidth, totalColumnWidth, currency);
        string core = hb.IsPipeDelimited
            ? FormatSpreadsheetPipelessAlignedRow("Sum", "", "", totalCell, w0, w1, w2, totalColumnWidth)
            : FormatSpreadsheetLegacyGapRow("Sum", "", "", totalCell, w0, w1, w2, totalColumnWidth);
        return indent + tablePrefix + tableMargin + core;
    }

    private void ShowSpreadsheetEditDialog(TextEditor editor)
    {
        if (editor.Document == null)
            return;
        int caret = editor.TextArea.Caret.Offset;
        if (!TryGetSpreadsheetBlockSpanContainingOffset(editor.Document, caret, out int start, out int len))
            return;
        if (!TryParseSpreadsheetBlockForEdit(
                editor.Document, start, len, out var title, out var currency, out var rows))
            return;

        var dlg = new SpreadsheetEditWindow(title, currency, rows) { Owner = this };
        if (dlg.ShowDialog() != true)
            return;

        string newBlock;
        try
        {
            newBlock = BuildSpreadsheetSectionTextFromEdit(
                dlg.EditedTitle, dlg.EditedCurrency, dlg.EditedRows);
        }
        catch (OverflowException ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Spreadsheet",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _spreadsheetSumSyncSuppress = true;
        try
        {
            editor.Document.Replace(start, len, newBlock);
        }
        finally
        {
            _spreadsheetSumSyncSuppress = false;
        }

        editor.TextArea.TextView.Redraw();
        TrySyncSpreadsheetSums(editor);
    }

    private static bool TryParseSpreadsheetBlockForEdit(
        TextDocument doc, int spanStart, int spanLength,
        out string title, out string currency, out List<SpreadsheetEditRowModel> rows)
    {
        title = "Spreadsheet";
        currency = SpreadsheetAmountHelpers.DefaultCurrency;
        rows = [];
        bool currencyFromDedicatedMetaLine = false;
        var blockText = doc.GetText(spanStart, spanLength);
        var lines = blockText.Split(NewLineSplits, StringSplitOptions.None);
        int i = 0;
        if (i < lines.Length && lines[i].Trim().Equals("```spreadsheet", StringComparison.OrdinalIgnoreCase))
            i++;

        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i]))
            i++;

        if (i >= lines.Length)
        {
            rows.Add(SpreadsheetEditRowModel.CreateDefault());
            return true;
        }

        SpreadsheetColumnBounds? columnBounds = null;

        if (IsHeaderRowLine(lines[i], out var headerBoundsDirect))
        {
            columnBounds = headerBoundsDirect;
            i++;
        }
        else
        {
            title = string.IsNullOrWhiteSpace(lines[i].Trim()) ? "Spreadsheet" : lines[i].Trim();
            i++;
            while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i]))
                i++;

            if (i >= lines.Length)
            {
                rows.Add(SpreadsheetEditRowModel.CreateDefault());
                return true;
            }

            if (IsHeaderRowLine(lines[i], out var headerAfterTitle))
            {
                columnBounds = headerAfterTitle;
                i++;
            }
            else
            {
                currencyFromDedicatedMetaLine = true;
                currency = string.IsNullOrWhiteSpace(lines[i].Trim())
                    ? SpreadsheetAmountHelpers.DefaultCurrency
                    : lines[i].Trim();
                i++;
                while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i]))
                    i++;

                if (i < lines.Length && IsHeaderRowLine(lines[i], out var headerAfterCurrency))
                {
                    columnBounds = headerAfterCurrency;
                    i++;
                }
            }
        }

        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i]))
            i++;

        while (i < lines.Length)
        {
            var ln = lines[i].TrimEnd();
            i++;
            if (string.IsNullOrWhiteSpace(ln))
                continue;
            if (IsSpreadsheetSumRowLineLoose(ln))
                break;
            if (IsHeaderRowLine(ln))
                continue;

            var cells = columnBounds.HasValue
                ? SplitSpreadsheetCells(ln, columnBounds.Value)
                : SplitSpreadsheetCells(ln);
            if (cells.Count < 4)
                continue;

            if (cells[0].Trim().Equals("Sum", StringComparison.Ordinal))
                continue;

            var model = new SpreadsheetEditRowModel
            {
                Description = cells[0],
                Quantity = SpreadsheetAmountHelpers.TrimTrailingCurrencyToken(cells[1]).Trim(),
                UnitPrice = SpreadsheetAmountHelpers.TrimTrailingCurrencyToken(cells[2]).Trim()
            };
            rows.Add(model);
        }

        if (!currencyFromDedicatedMetaLine
            && TryParseCurrencyFromSumLineInLineArray(lines, out var sumCurrency)
            && !string.IsNullOrWhiteSpace(sumCurrency))
            currency = sumCurrency;

        if (rows.Count == 0)
            rows.Add(SpreadsheetEditRowModel.CreateDefault());

        return true;
    }

    private static readonly string[] NewLineSplits = ["\r\n", "\r", "\n"];

    private static string BuildSpreadsheetSectionTextFromEdit(
        string title, string currency, IReadOnlyList<SpreadsheetEditRowModel> rows)
    {
        string nl = Environment.NewLine;
        currency = string.IsNullOrWhiteSpace(currency)
            ? SpreadsheetAmountHelpers.DefaultCurrency
            : currency.Trim();

        var active = rows.Where(r =>
            !(string.IsNullOrWhiteSpace(r.Description)
              && string.IsNullOrWhiteSpace(r.Quantity)
              && string.IsNullOrWhiteSpace(r.UnitPrice))).ToList();
        if (active.Count == 0)
            active = [SpreadsheetEditRowModel.CreateDefault()];

        decimal sum = 0;
        foreach (var row in active)
        {
            if (!SpreadsheetAmountHelpers.TrySafeAdd(sum, row.LineTotalValue, out sum))
                throw new OverflowException("Spreadsheet total exceeds the supported range.");
        }

        ComputeSpreadsheetColumnWidths(active, out int w0, out int w1, out int w2, out int w3Num);
        w3Num = Math.Max(w3Num, SpreadsheetAmountHelpers.FormatNumber(sum).Length);
        int sfx = SpreadsheetTotalCurrencySuffixWidth(currency);
        int w3 = w3Num + sfx;
        w0 = Math.Max(w0, "Sum".Length);

        var sb = new StringBuilder();
        sb.Append("```spreadsheet");
        sb.Append(nl);
        sb.Append(string.IsNullOrWhiteSpace(title) ? "Spreadsheet" : title.Trim());
        sb.Append(nl);
        sb.Append(nl);
        sb.Append(SpreadsheetPresentationLeftMargin);
        sb.Append(FormatSpreadsheetPipeRow(
            "Description", "Quantity", "Unit price",
            SpreadsheetFormatTotalHeaderCell(w3Num, w3, currency),
            w0, w1, w2, w3));
        sb.Append(nl);
        sb.Append(nl);

        foreach (var row in active)
        {
            decimal lineTotal = row.LineTotalValue;
            string desc = string.IsNullOrWhiteSpace(row.Description) ? "Item" : row.Description.Trim();
            string qty = string.IsNullOrWhiteSpace(row.Quantity) ? "0" : row.Quantity.Trim();
            string unitRaw = string.IsNullOrWhiteSpace(row.UnitPrice) ? "0" : row.UnitPrice.Trim();
            SpreadsheetAmountHelpers.TryParseDecimal(unitRaw, out var unitDec);
            string unitMoney = SpreadsheetAmountHelpers.FormatNumber(unitDec);
            string lineMoney = SpreadsheetAmountHelpers.FormatNumber(lineTotal);
            sb.Append(SpreadsheetPresentationLeftMargin);
            sb.Append(FormatSpreadsheetPipeRow(
                desc,
                qty,
                unitMoney,
                SpreadsheetFormatTotalDataCell(lineMoney, w3Num, w3, currency),
                w0, w1, w2, w3));
            sb.Append(nl);
        }

        sb.Append(nl);
        sb.Append(SpreadsheetPresentationLeftMargin);
        sb.Append(FormatSpreadsheetPipelessAlignedRow(
            "Sum",
            "",
            "",
            SpreadsheetFormatTotalSumCell(SpreadsheetAmountHelpers.FormatNumber(sum), w3Num, w3, currency),
            w0, w1, w2, w3));
        sb.Append(nl);
        sb.Append("```");
        sb.Append(nl);
        return sb.ToString();
    }

    private sealed class SpreadsheetFenceLineHiddenGenerator : VisualLineElementGenerator
    {
        private readonly Func<TextView, bool> _shouldHide;

        public SpreadsheetFenceLineHiddenGenerator(Func<TextView, bool> shouldHide)
            => _shouldHide = shouldHide;

        public override int GetFirstInterestedOffset(int startOffset)
        {
            if (!_shouldHide(CurrentContext.TextView))
                return -1;
            if (CurrentContext?.Document == null)
                return -1;

            var docLine = CurrentContext.VisualLine.FirstDocumentLine;
            var doc = CurrentContext.Document;
            if (!DocumentLineIsSpreadsheetFenceDelimiter(doc, docLine))
                return -1;

            int abs = docLine.Offset;
            if (startOffset > abs)
                return -1;

            return abs;
        }

        public override VisualLineElement? ConstructElement(int offset)
        {
            if (!_shouldHide(CurrentContext.TextView))
                return null;
            if (CurrentContext?.Document == null)
                return null;

            var docLine = CurrentContext.VisualLine.FirstDocumentLine;
            var doc = CurrentContext.Document;
            if (!DocumentLineIsSpreadsheetFenceDelimiter(doc, docLine))
                return null;

            if (offset != docLine.Offset || docLine.Length <= 0)
                return null;

            return new SpreadsheetHiddenDocumentSpanElement(docLine.Length);
        }
    }

    private sealed class SpreadsheetCurrencyLineHiddenGenerator : VisualLineElementGenerator
    {
        private readonly Func<TextView, bool> _shouldHide;

        public SpreadsheetCurrencyLineHiddenGenerator(Func<TextView, bool> shouldHide)
            => _shouldHide = shouldHide;

        public override int GetFirstInterestedOffset(int startOffset)
        {
            if (!_shouldHide(CurrentContext.TextView))
                return -1;
            if (CurrentContext?.Document == null)
                return -1;

            var docLine = CurrentContext.VisualLine.FirstDocumentLine;
            var doc = CurrentContext.Document;
            if (!DocumentLineIsSpreadsheetCurrencyLine(doc, docLine))
                return -1;

            int abs = docLine.Offset;
            if (startOffset > abs)
                return -1;

            return abs;
        }

        public override VisualLineElement? ConstructElement(int offset)
        {
            if (!_shouldHide(CurrentContext.TextView))
                return null;
            if (CurrentContext?.Document == null)
                return null;

            var docLine = CurrentContext.VisualLine.FirstDocumentLine;
            var doc = CurrentContext.Document;
            if (!DocumentLineIsSpreadsheetCurrencyLine(doc, docLine))
                return null;

            if (offset != docLine.Offset || docLine.Length <= 0)
                return null;

            return new SpreadsheetHiddenDocumentSpanElement(docLine.Length);
        }
    }

    private sealed class SpreadsheetHiddenDocumentSpanElement : VisualLineElement
    {
        public SpreadsheetHiddenDocumentSpanElement(int documentLength)
            : base(1, documentLength)
        {
        }

        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
            => new TextHidden(VisualLength);
    }

    private sealed class SpreadsheetChromeBackgroundRenderer : IBackgroundRenderer
    {
        private readonly Func<TextView, bool> _chromeActive;
        private static readonly Brush Fill = ChromeBrush(Color.FromRgb(0xF5, 0xF7, 0xFA));
        private static readonly Brush LeftAccent = ChromeBrush(Color.FromRgb(0xC5, 0xD0, 0xE6));

        public SpreadsheetChromeBackgroundRenderer(Func<TextView, bool> chromeActive)
            => _chromeActive = chromeActive;

        private static SolidColorBrush ChromeBrush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (!_chromeActive(textView))
                return;
            if (textView.Document == null || !textView.VisualLinesValid)
                return;

            double drawWidth = textView.ActualWidth;
            if (textView is IScrollInfo si)
                drawWidth = Math.Max(drawWidth, si.HorizontalOffset + si.ViewportWidth);

            var doc = textView.Document;

            foreach (var vl in textView.VisualLines)
            {
                if (vl.IsDisposed)
                    continue;

                var docLine = vl.FirstDocumentLine;
                if (!DocumentLineShowsSpreadsheetChrome(doc, docLine))
                    continue;

                var rowRects = BackgroundGeometryBuilder.GetRectsFromVisualSegment(
                    textView, vl, 0, vl.VisualLength).ToList();
                if (rowRects.Count == 0)
                    continue;

                double top = rowRects.Min(r => r.Top);
                double bottom = rowRects.Max(r => r.Bottom);
                var band = new Rect(0, top, drawWidth, bottom - top);

                drawingContext.DrawRectangle(Fill, null, band);
                const double accentW = 3.5;
                drawingContext.DrawRectangle(LeftAccent, null, new Rect(0, top, accentW, bottom - top));
            }
        }
    }

    private sealed class SpreadsheetMonospaceTransformer : DocumentColorizingTransformer
    {
        private readonly Func<TextView, bool> _chromeActive;
        private static readonly FontFamily CodeFont = new("Consolas, Courier New");

        public SpreadsheetMonospaceTransformer(Func<TextView, bool> chromeActive)
            => _chromeActive = chromeActive;

        protected override void ColorizeLine(DocumentLine line)
        {
            if (!_chromeActive(CurrentContext.TextView))
                return;
            if (CurrentContext?.Document == null)
                return;

            var doc = CurrentContext.Document;
            if (!DocumentLineShowsSpreadsheetChrome(doc, line))
                return;
            if (IsSpreadsheetNameLine(doc, line))
                return;
            if (DocumentLineIsSpreadsheetCurrencyLine(doc, line))
                return;

            ChangeLinePart(line.Offset, line.EndOffset, ve =>
            {
                var t = ve.TextRunProperties.Typeface;
                ve.TextRunProperties.SetTypeface(new Typeface(CodeFont, t.Style, t.Weight, t.Stretch));
            });
        }
    }

    private sealed class SpreadsheetNameLineTransformer : DocumentColorizingTransformer
    {
        private readonly Func<TextView, bool> _chromeActive;

        public SpreadsheetNameLineTransformer(Func<TextView, bool> chromeActive)
            => _chromeActive = chromeActive;

        protected override void ColorizeLine(DocumentLine line)
        {
            if (!_chromeActive(CurrentContext.TextView))
                return;
            if (CurrentContext?.Document == null || line.Length <= 0)
                return;

            var doc = CurrentContext.Document;
            if (!IsSpreadsheetNameLine(doc, line))
                return;

            ChangeLinePart(line.Offset, line.EndOffset, ve =>
            {
                var tf = ve.TextRunProperties.Typeface;
                ve.TextRunProperties.SetTypeface(new Typeface(
                    tf.FontFamily, tf.Style, FontWeights.Bold, tf.Stretch));
                ve.TextRunProperties.SetFontRenderingEmSize(
                    ve.TextRunProperties.FontRenderingEmSize * 1.4);
            });
        }
    }

    private sealed class SpreadsheetHeaderRowTransformer : DocumentColorizingTransformer
    {
        private readonly Func<TextView, bool> _chromeActive;

        public SpreadsheetHeaderRowTransformer(Func<TextView, bool> chromeActive)
            => _chromeActive = chromeActive;

        protected override void ColorizeLine(DocumentLine line)
        {
            if (!_chromeActive(CurrentContext.TextView))
                return;
            if (CurrentContext?.Document == null || line.Length <= 0)
                return;

            var doc = CurrentContext.Document;
            if (!DocumentLineShowsSpreadsheetChrome(doc, line))
                return;
            if (DocumentLineIsSpreadsheetFenceDelimiter(doc, line))
                return;

            var raw = doc.GetText(line.Offset, line.Length);
            if (!IsHeaderRowLine(raw, out var bounds))
                return;

            void BoldSpan(int relStart, int length)
            {
                if (length <= 0)
                    return;
                relStart = Math.Clamp(relStart, 0, raw.Length);
                int end = Math.Clamp(relStart + length, relStart, raw.Length);
                if (end <= relStart)
                    return;
                int abs = line.Offset + relStart;
                ChangeLinePart(abs, abs + (end - relStart), ve =>
                {
                    var tf = ve.TextRunProperties.Typeface;
                    ve.TextRunProperties.SetTypeface(new Typeface(
                        tf.FontFamily, tf.Style, FontWeights.Bold, tf.Stretch));
                });
            }

            void BoldLabel(int cellStart, int cellEndExclusive, string label)
            {
                cellStart = Math.Clamp(cellStart, 0, raw.Length);
                cellEndExclusive = Math.Clamp(cellEndExclusive, cellStart, raw.Length);
                int s = cellStart;
                while (s < cellEndExclusive && char.IsWhiteSpace(raw[s]))
                    s++;
                if (s + label.Length > cellEndExclusive)
                    return;
                if (!raw.AsSpan(s, label.Length).Equals(label.AsSpan(), StringComparison.OrdinalIgnoreCase))
                    return;
                BoldSpan(s, label.Length);
            }

            if (bounds.IsPipeDelimited)
            {
                BoldLabel(bounds.DescStart, bounds.Pipe1, "Description");
                BoldLabel(bounds.QtyStart, bounds.Pipe2, "Quantity");
                BoldLabel(bounds.UnitStart, bounds.Pipe3, "Unit price");
                BoldLabel(bounds.TotalStart, bounds.Pipe4, "Total");
            }
            else
            {
                BoldLabel(bounds.DescStart, bounds.QtyStart, "Description");
                BoldLabel(bounds.QtyStart, bounds.UnitStart, "Quantity");
                BoldLabel(bounds.UnitStart, bounds.TotalStart, "Unit price");
                BoldLabel(bounds.TotalStart, raw.Length, "Total");
            }
        }
    }

    private static bool IsSpreadsheetNameLine(TextDocument doc, DocumentLine line)
    {
        var prevNum = line.LineNumber - 1;
        if (prevNum < 1)
            return false;
        var prev = doc.GetLineByNumber(prevNum);
        return IsSpreadsheetOpenFenceLine(doc, prev);
    }

    private enum SpreadsheetLineKind
    {
        OutsideBlock,
        OpenFence,
        CloseFence,
        NameLine,
        HeaderRow,
        SumRow,
        DataOrBlankRow,
    }

    private static SpreadsheetLineKind ClassifySpreadsheetLine(TextDocument doc, DocumentLine line)
    {
        bool inside = false;
        DocumentLine? openFence = null;
        for (int i = 1; i < line.LineNumber; i++)
        {
            var prior = doc.GetLineByNumber(i);
            if (!inside)
            {
                if (IsSpreadsheetOpenFenceLine(doc, prior))
                {
                    inside = true;
                    openFence = prior;
                }
            }
            else if (IsBareMarkdownCloseFenceLine(doc, prior))
            {
                inside = false;
                openFence = null;
            }
        }

        if (IsSpreadsheetOpenFenceLine(doc, line))
            return SpreadsheetLineKind.OpenFence;

        if (!inside)
            return SpreadsheetLineKind.OutsideBlock;

        if (IsBareMarkdownCloseFenceLine(doc, line))
            return SpreadsheetLineKind.CloseFence;

        if (openFence != null && line.LineNumber == openFence.LineNumber + 1)
            return SpreadsheetLineKind.NameLine;

        var raw = doc.GetText(line.Offset, line.Length);
        if (IsHeaderRowLine(raw))
            return SpreadsheetLineKind.HeaderRow;
        if (TryFindSpreadsheetHeaderBoundsForDocumentLine(doc, line, out var sumBounds)
            && IsSpreadsheetSumRowLine(raw, sumBounds))
            return SpreadsheetLineKind.SumRow;
        if (IsSpreadsheetSumRowLineLoose(raw))
            return SpreadsheetLineKind.SumRow;

        return SpreadsheetLineKind.DataOrBlankRow;
    }

    private sealed class WritableSegment : ISegment
    {
        public WritableSegment(int offset, int length)
        {
            Offset = offset;
            Length = length;
        }

        public int Offset { get; }
        public int Length { get; }
        public int EndOffset => Offset + Length;
    }

    private readonly struct LineWritableMap
    {
        public LineWritableMap(List<(int start, int end)> ranges, bool newlineWritable)
        {
            Ranges = ranges;
            NewlineWritable = newlineWritable;
        }

        public List<(int start, int end)> Ranges { get; }
        public bool NewlineWritable { get; }
    }

    private static LineWritableMap BuildSpreadsheetReadOnlyLineMap(
        TextDocument doc, DocumentLine line, bool chromeLocksSpreadsheetEditing)
    {
        if (!chromeLocksSpreadsheetEditing)
        {
            return new LineWritableMap(
                new List<(int, int)> { (line.Offset, line.Offset + line.Length) },
                newlineWritable: true);
        }

        if (!DocumentLineShowsSpreadsheetChrome(doc, line))
        {
            return new LineWritableMap(
                new List<(int, int)> { (line.Offset, line.Offset + line.Length) },
                newlineWritable: true);
        }

        return new LineWritableMap(new List<(int, int)>(), newlineWritable: false);
    }

    private sealed class SpreadsheetReadOnlySectionProvider : IReadOnlySectionProvider
    {
        private readonly Func<TextDocument?> _getDoc;
        private readonly Func<bool> _chromeLocksSpreadsheetEditing;

        public SpreadsheetReadOnlySectionProvider(Func<TextDocument?> getDoc, Func<bool> chromeLocksSpreadsheetEditing)
        {
            _getDoc = getDoc;
            _chromeLocksSpreadsheetEditing = chromeLocksSpreadsheetEditing;
        }

        public bool CanInsert(int offset)
        {
            if (!_chromeLocksSpreadsheetEditing())
                return true;
            var doc = _getDoc();
            if (doc == null || doc.TextLength == 0)
                return true;

            offset = Math.Clamp(offset, 0, doc.TextLength);
            var line = doc.GetLineByOffset(offset == doc.TextLength ? Math.Max(0, doc.TextLength - 1) : offset);
            if (!DocumentLineShowsSpreadsheetChrome(doc, line))
                return true;
            return false;
        }

        public IEnumerable<ISegment> GetDeletableSegments(ISegment segment)
        {
            if (segment == null)
                yield break;

            var doc = _getDoc();
            bool chrome = _chromeLocksSpreadsheetEditing();
            if (!chrome || doc == null || doc.LineCount == 0)
            {
                yield return segment;
                yield break;
            }

            int segStart = segment.Offset;
            int segEnd = segment.EndOffset;
            if (segEnd <= segStart)
            {
                yield return segment;
                yield break;
            }

            int cursor = segStart;
            while (cursor < segEnd)
            {
                var line = doc.GetLineByOffset(cursor);
                int lineTotalEnd = line.Offset + line.TotalLength;
                int chunkEnd = Math.Min(lineTotalEnd, segEnd);

                var map = BuildSpreadsheetReadOnlyLineMap(doc, line, chrome);
                foreach (var r in map.Ranges)
                {
                    int subStart = Math.Max(r.start, cursor);
                    int subEnd = Math.Min(r.end, chunkEnd);
                    if (subEnd > subStart)
                        yield return new WritableSegment(subStart, subEnd - subStart);
                }

                if (map.NewlineWritable)
                {
                    int nlStart = line.Offset + line.Length;
                    int nlEnd = lineTotalEnd;
                    int subStart = Math.Max(nlStart, cursor);
                    int subEnd = Math.Min(nlEnd, chunkEnd);
                    if (subEnd > subStart)
                        yield return new WritableSegment(subStart, subEnd - subStart);
                }

                cursor = chunkEnd;
                if (cursor <= line.Offset)
                    cursor = line.Offset + Math.Max(1, line.TotalLength);
            }
        }
    }

    private sealed class SpreadsheetGridLineRenderer : IBackgroundRenderer
    {
        private readonly Func<TextView, bool> _chromeActive;
        private static readonly Pen GridPen = MakePen(Color.FromRgb(0xB6, 0xC2, 0xD8), 0.7);

        public SpreadsheetGridLineRenderer(Func<TextView, bool> chromeActive)
            => _chromeActive = chromeActive;

        private static Pen MakePen(Color c, double thickness)
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            var pen = new Pen(brush, thickness);
            pen.Freeze();
            return pen;
        }

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (!_chromeActive(textView))
                return;
            if (textView.Document == null || !textView.VisualLinesValid)
                return;

            double drawWidth = textView.ActualWidth;
            if (textView is IScrollInfo si)
                drawWidth = Math.Max(drawWidth, si.HorizontalOffset + si.ViewportWidth);

            var doc = textView.Document;

            foreach (var vl in textView.VisualLines)
            {
                if (vl.IsDisposed)
                    continue;
                var docLine = vl.FirstDocumentLine;
                if (!DocumentLineShowsSpreadsheetChrome(doc, docLine))
                    continue;
                if (DocumentLineIsSpreadsheetFenceDelimiter(doc, docLine))
                    continue;

                var rects = BackgroundGeometryBuilder.GetRectsFromVisualSegment(
                    textView, vl, 0, vl.VisualLength).ToList();
                if (rects.Count == 0)
                    continue;
                double bottom = rects.Max(r => r.Bottom);

                drawingContext.DrawLine(
                    GridPen,
                    new Point(0, bottom + 0.5),
                    new Point(drawWidth, bottom + 0.5));

                var raw = doc.GetText(docLine.Offset, docLine.Length);
                List<int> verticalAt;
                var lineKind = ClassifySpreadsheetLine(doc, docLine);
                // Header pipe indices must not be applied to title / sum / currency rows — e.g. Pipe0==0
                // draws a vertical through column 0 on "Spreadsheet", looking like a leading "|".
                if (lineKind == SpreadsheetLineKind.SumRow
                    || lineKind == SpreadsheetLineKind.NameLine
                    || DocumentLineIsSpreadsheetCurrencyLine(doc, docLine))
                    verticalAt = [];
                else if (TryFindSpreadsheetHeaderBoundsForDocumentLine(doc, docLine, out var colBounds))
                    verticalAt = FindSpreadsheetColumnBoundaryOffsetsInLine(string.Empty, colBounds);
                else
                    verticalAt = FindLegacyTabOrPipeSeparatorIndices(raw);

                foreach (var idx in verticalAt)
                {
                    if (idx < 0 || idx >= raw.Length)
                        continue;
                    var pipeRects = BackgroundGeometryBuilder.GetRectsFromVisualSegment(
                        textView, vl, idx, idx + 1).ToList();
                    if (pipeRects.Count == 0)
                        continue;
                    var r = pipeRects[0];
                    double cx = r.Left + r.Width / 2.0;
                    drawingContext.DrawLine(GridPen, new Point(cx, r.Top), new Point(cx, r.Bottom));
                }
            }
        }
    }

    private sealed class SpreadsheetSeparatorColorTransformer : DocumentColorizingTransformer
    {
        private readonly Func<TextView, bool> _chromeActive;
        private static readonly Brush SepBrush = MakeBrush(Color.FromRgb(0xB6, 0xC2, 0xD8));

        public SpreadsheetSeparatorColorTransformer(Func<TextView, bool> chromeActive)
            => _chromeActive = chromeActive;

        private static Brush MakeBrush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            if (!_chromeActive(CurrentContext.TextView))
                return;
            if (CurrentContext?.Document == null || line.Length <= 0)
                return;

            var doc = CurrentContext.Document;
            if (!DocumentLineShowsSpreadsheetChrome(doc, line))
                return;
            if (DocumentLineIsSpreadsheetFenceDelimiter(doc, line))
                return;

            var raw = doc.GetText(line.Offset, line.Length);
            if (ClassifySpreadsheetLine(doc, line) == SpreadsheetLineKind.SumRow)
                return;

            if (TryFindSpreadsheetHeaderBoundsForDocumentLine(doc, line, out var b))
            {
                if (b.IsPipeDelimited)
                {
                    foreach (int pi in new[] { b.Pipe0, b.Pipe1, b.Pipe2, b.Pipe3, b.Pipe4 })
                    {
                        if (pi < 0 || pi >= raw.Length || raw[pi] != '|')
                            continue;
                        int absStart = line.Offset + pi;
                        ChangeLinePart(absStart, absStart + 1, ve =>
                            ve.TextRunProperties.SetForegroundBrush(SepBrush));
                    }

                    return;
                }

                int gap = SpreadsheetColumnGap.Length;
                void PaintGap(int boundaryStart)
                {
                    int start = boundaryStart - gap;
                    if (start < 0)
                        return;
                    for (int i = start; i < boundaryStart && i < raw.Length; i++)
                    {
                        if (raw[i] != ' ')
                            continue;
                        int abs = line.Offset + i;
                        ChangeLinePart(abs, abs + 1, ve =>
                            ve.TextRunProperties.SetForegroundBrush(SepBrush));
                    }
                }

                PaintGap(b.QtyStart);
                PaintGap(b.UnitStart);
                PaintGap(b.TotalStart);
                return;
            }

            char sep = raw.Contains('\t') ? '\t' : '|';
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] != sep)
                    continue;
                int absStart = line.Offset + i;
                ChangeLinePart(absStart, absStart + 1, ve =>
                    ve.TextRunProperties.SetForegroundBrush(SepBrush));
            }
        }
    }

    private sealed class SpreadsheetSumRowTransformer : DocumentColorizingTransformer
    {
        private readonly Func<TextView, bool> _chromeActive;

        public SpreadsheetSumRowTransformer(Func<TextView, bool> chromeActive)
            => _chromeActive = chromeActive;

        protected override void ColorizeLine(DocumentLine line)
        {
            if (!_chromeActive(CurrentContext.TextView))
                return;
            if (CurrentContext?.Document == null || line.Length <= 0)
                return;

            var doc = CurrentContext.Document;
            if (!DocumentLineShowsSpreadsheetChrome(doc, line))
                return;

            var text = doc.GetText(line.Offset, line.Length);

            void Bold(int start, int end)
            {
                if (end <= start)
                    return;
                ChangeLinePart(start, end, ve =>
                {
                    var tf = ve.TextRunProperties.Typeface;
                    ve.TextRunProperties.SetTypeface(new Typeface(
                        tf.FontFamily, tf.Style, FontWeights.Bold, tf.Stretch));
                });
            }

            void Transparent(int start, int end)
            {
                if (end <= start)
                    return;
                ChangeLinePart(start, end, ve =>
                    ve.TextRunProperties.SetForegroundBrush(Brushes.Transparent));
            }

            var trimmed = text.TrimStart();
            int leading = text.Length - trimmed.Length;
            int baseOffset = line.Offset + leading;

            // Legacy markdown-style row (still bold "Sum", hide asterisks).
            if (trimmed.StartsWith("**Sum**", StringComparison.Ordinal))
            {
                const string token = "**Sum**";
                Transparent(baseOffset, baseOffset + 2);
                Bold(baseOffset + 2, baseOffset + 5);
                Transparent(baseOffset + 5, baseOffset + token.Length);
                return;
            }

            if (!TryFindSpreadsheetHeaderBoundsForDocumentLine(doc, line, out var b))
                return;
            if (!IsSpreadsheetSumRowLine(text, b))
                return;

            // Pipe-less sum row: "Sum" starts at the table column where '|' sits on pipe rows (Pipe0).
            if (text.IndexOf('|') < 0 && b.IsPipeDelimited)
            {
                int rs = Math.Clamp(b.Pipe0, 0, text.Length);
                while (rs < text.Length && char.IsWhiteSpace(text[rs]))
                    rs++;
                if (rs + 3 <= text.Length && text.AsSpan(rs, 3).SequenceEqual("Sum".AsSpan()))
                    Bold(line.Offset + rs, line.Offset + rs + 3);
                return;
            }

            int cellEnd = b.IsPipeDelimited ? b.Pipe1 : b.QtyStart;
            int rsPipe = Math.Clamp(b.DescStart, 0, text.Length);
            while (rsPipe < cellEnd && rsPipe < text.Length && char.IsWhiteSpace(text[rsPipe]))
                rsPipe++;
            if (rsPipe + 3 <= text.Length && rsPipe + 3 <= cellEnd
                && text.AsSpan(rsPipe, 3).SequenceEqual("Sum".AsSpan()))
                Bold(line.Offset + rsPipe, line.Offset + rsPipe + 3);
        }
    }

    private sealed class SpreadsheetDataNumberTransformer : DocumentColorizingTransformer
    {
        private readonly Func<TextView, bool> _chromeActive;
        private static readonly Brush NumberBrush = MakeBrush(Color.FromRgb(0x0D, 0x73, 0x77));

        public SpreadsheetDataNumberTransformer(Func<TextView, bool> chromeActive)
            => _chromeActive = chromeActive;

        private static Brush MakeBrush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            if (!_chromeActive(CurrentContext.TextView))
                return;
            if (CurrentContext?.Document == null || line.Length <= 0)
                return;

            var doc = CurrentContext.Document;
            if (!DocumentLineShowsSpreadsheetChrome(doc, line))
                return;
            if (DocumentLineIsSpreadsheetFenceDelimiter(doc, line))
                return;
            if (ClassifySpreadsheetLine(doc, line) != SpreadsheetLineKind.DataOrBlankRow)
                return;

            var raw = doc.GetText(line.Offset, line.Length);
            if (IsHeaderRowLine(raw))
                return;

            void PaintNumericCell(int relStart, int relEndExclusive)
            {
                int a = Math.Clamp(relStart, 0, raw.Length);
                int b = Math.Clamp(relEndExclusive, a, raw.Length);
                while (a < b && char.IsWhiteSpace(raw[a]))
                    a++;
                while (b > a && char.IsWhiteSpace(raw[b - 1]))
                    b--;
                if (b <= a)
                    return;
                string inner = raw.Substring(a, b - a);
                if (!SpreadsheetAmountHelpers.TryParseDecimal(inner, out _))
                    return;
                string core = SpreadsheetAmountHelpers.TrimTrailingCurrencyToken(inner.Trim());
                if (core.Length == 0)
                    return;
                int idx = inner.IndexOf(core, StringComparison.Ordinal);
                if (idx < 0)
                    return;
                int paintStart = line.Offset + a + idx;
                ChangeLinePart(paintStart, paintStart + core.Length, ve =>
                    ve.TextRunProperties.SetForegroundBrush(NumberBrush));
            }

            if (TryFindSpreadsheetHeaderBoundsForDocumentLine(doc, line, out var bounds))
            {
                int totalEnd = bounds.IsPipeDelimited ? bounds.Pipe4 : raw.Length;
                PaintNumericCell(bounds.QtyStart, bounds.UnitStart);
                PaintNumericCell(bounds.UnitStart, bounds.TotalStart);
                PaintNumericCell(bounds.TotalStart, totalEnd);
                return;
            }

            var seps = FindLegacyTabOrPipeSeparatorIndices(raw);
            if (seps.Count < 3)
                return;
            PaintNumericCell(seps[0] + 1, seps[1]);
            PaintNumericCell(seps[1] + 1, seps[2]);
            PaintNumericCell(seps[2] + 1, raw.Length);
        }
    }

    private void AttachSpreadsheetEditorChrome(TextEditor editor)
    {
        bool Chrome(TextView tv) => SpreadsheetChromeActiveForTextView(tv);

        editor.TextArea.TextView.ElementGenerators.Add(new SpreadsheetFenceLineHiddenGenerator(Chrome));
        editor.TextArea.TextView.ElementGenerators.Add(new SpreadsheetCurrencyLineHiddenGenerator(Chrome));
        editor.TextArea.TextView.BackgroundRenderers.Add(new SpreadsheetChromeBackgroundRenderer(Chrome));
        editor.TextArea.TextView.BackgroundRenderers.Add(new SpreadsheetGridLineRenderer(Chrome));
        editor.TextArea.TextView.LineTransformers.Add(new SpreadsheetMonospaceTransformer(Chrome));
        editor.TextArea.TextView.LineTransformers.Add(new SpreadsheetNameLineTransformer(Chrome));
        editor.TextArea.TextView.LineTransformers.Add(new SpreadsheetHeaderRowTransformer(Chrome));
        editor.TextArea.TextView.LineTransformers.Add(new SpreadsheetSeparatorColorTransformer(Chrome));
        editor.TextArea.TextView.LineTransformers.Add(new SpreadsheetDataNumberTransformer(Chrome));
        editor.TextArea.TextView.LineTransformers.Add(new SpreadsheetSumRowTransformer(Chrome));

        editor.TextArea.ReadOnlySectionProvider = new SpreadsheetReadOnlySectionProvider(
            () => editor.Document,
            () => _renderSpreadsheet && FindDocByEditor(editor) != null);
    }
}
