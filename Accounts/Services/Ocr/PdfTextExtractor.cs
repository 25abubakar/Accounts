using System.Text;
using Accounts.Services.Ocr.Interfaces;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Accounts.Services.Ocr;

public sealed class PdfTextExtractor : IPdfTextExtractor
{
    public IReadOnlyList<PdfPageLines> ExtractPages(byte[] pdfBytes, double yTolerance = 3.0, double xGap = 0.75)
    {
        var result = new List<PdfPageLines>();
        using var document = PdfDocument.Open(pdfBytes);

        for (int p = 1; p <= document.NumberOfPages; p++)
        {
            var page = document.GetPage(p);
            var words = page.GetWords().ToList();

            var lineClusters = new List<List<Word>>();
            // Sort words by Y (Bottom) descending, then X (Left) ascending
            var sortedWords = words.OrderByDescending(w => w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left).ToList();

            foreach (var word in sortedWords)
            {
                // Find cluster whose average bottom is within yTolerance
                var match = lineClusters.FirstOrDefault(c => Math.Abs(c.Average(w => w.BoundingBox.Bottom) - word.BoundingBox.Bottom) <= yTolerance);
                if (match != null)
                {
                    match.Add(word);
                }
                else
                {
                    lineClusters.Add(new List<Word> { word });
                }
            }

            var textLines = new List<TextLine>();
            // Order line clusters from top to bottom (highest Y to lowest Y)
            foreach (var cluster in lineClusters.OrderByDescending(c => c.Average(w => w.BoundingBox.Bottom)))
            {
                var orderedWords = cluster.OrderBy(w => w.BoundingBox.Left).ToList();
                var wordBoxes = orderedWords.Select(w => new WordBox
                {
                    Text = w.Text,
                    Left = w.BoundingBox.Left,
                    Right = w.BoundingBox.Right,
                    Top = w.BoundingBox.Top,
                    Bottom = w.BoundingBox.Bottom
                }).ToList();

                var sb = new StringBuilder();
                for (int i = 0; i < orderedWords.Count; i++)
                {
                    if (i > 0)
                    {
                        var prev = orderedWords[i - 1];
                        var curr = orderedWords[i];
                        if ((curr.BoundingBox.Left - prev.BoundingBox.Right) > xGap)
                        {
                            sb.Append(' ');
                        }
                    }
                    sb.Append(orderedWords[i].Text);
                }

                textLines.Add(new TextLine
                {
                    Text = sb.ToString(),
                    Top = cluster.Max(w => w.BoundingBox.Top),
                    Bottom = cluster.Min(w => w.BoundingBox.Bottom),
                    Left = cluster.Min(w => w.BoundingBox.Left),
                    Right = cluster.Max(w => w.BoundingBox.Right),
                    Words = wordBoxes
                });
            }

            result.Add(new PdfPageLines
            {
                PageIndex = p - 1,
                Width = page.Width,
                Height = page.Height,
                Lines = textLines,
                WordCount = words.Count
            });
        }

        return result;
    }
}

