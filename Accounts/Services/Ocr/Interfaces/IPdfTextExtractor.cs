using Accounts.Models;

namespace Accounts.Services.Ocr.Interfaces;

/// <summary>
/// Extracts text from PDF pages using PdfPig word bounding boxes.
/// Groups words into lines by Y position (tolerance ~3 points), left-to-right,
/// inserts a space when horizontal gap exceeds ~0.75 points.
/// </summary>
public interface IPdfTextExtractor
{
    /// <summary>Extract all pages from a PDF byte array.</summary>
    IReadOnlyList<PdfPageLines> ExtractPages(byte[] pdfBytes, double yTolerance = 3.0, double xGap = 0.75);
}

public sealed class PdfPageLines
{
    public int PageIndex { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public IReadOnlyList<TextLine> Lines { get; init; } = [];
    public string FullText => string.Join("\n", Lines.Select(l => l.Text));
    public int WordCount { get; init; }
}

public sealed class TextLine
{
    public string Text { get; init; } = "";
    public double Top { get; init; }
    public double Bottom { get; init; }
    public double Left { get; init; }
    public double Right { get; init; }
    public IReadOnlyList<WordBox> Words { get; init; } = [];
}

public sealed class WordBox
{
    public string Text { get; init; } = "";
    public double Left { get; init; }
    public double Right { get; init; }
    public double Top { get; init; }
    public double Bottom { get; init; }
}

