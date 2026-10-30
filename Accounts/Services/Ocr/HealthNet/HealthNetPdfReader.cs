using Accounts.Services.Ocr.HealthNet.Models;
using Accounts.Services.Ocr.HealthNet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Accounts.Services.Ocr.HealthNet
{
    public class HealthNetPdfReader
        : IHealthNetPdfReader
    {
        private readonly IHealthNetPageClassifier
            _pageClassifier;

        private readonly IHealthNetTextNormalizer
            _normalizer;

        /*
         * PDF words appearing approximately
         * at the same Y position belong to
         * the same visual row.
         */
        private const double LineYTolerance = 3.0;

        public HealthNetPdfReader(
            IHealthNetPageClassifier pageClassifier,
            IHealthNetTextNormalizer normalizer)
        {
            _pageClassifier = pageClassifier;
            _normalizer = normalizer;
        }

        public Task<List<HealthNetPageData>>
            ReadPagesAsync(
                Stream pdfStream,
                CancellationToken cancellationToken =
                    default)
        {
            if (pdfStream == null)
            {
                throw new ArgumentNullException(
                    nameof(pdfStream));
            }

            if (!pdfStream.CanRead)
            {
                throw new InvalidOperationException(
                    "The supplied PDF stream cannot be read.");
            }

            var result =
                new List<HealthNetPageData>();

            using var document =
                PdfDocument.Open(pdfStream);

            int physicalPageNumber = 0;

            foreach (
                var page in document.GetPages())
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                physicalPageNumber++;

                var pageData =
                    BuildPageData(
                        page,
                        physicalPageNumber);

                pageData.PageType =
                    _pageClassifier
                        .Classify(pageData);

                result.Add(pageData);
            }

            return Task.FromResult(result);
        }

        private HealthNetPageData BuildPageData(
            Page page,
            int physicalPageNumber)
        {
            var result =
                new HealthNetPageData
                {
                    /*
                     * Physical PDF page number.
                     *
                     * We do NOT use the printed
                     * "Page X of Y" value here.
                     */
                    PageNumber =
                        physicalPageNumber,

                    PageWidth =
                        Convert.ToDouble(
                            page.Width),

                    PageHeight =
                        Convert.ToDouble(
                            page.Height),

                    RawText =
                        page.Text ??
                        string.Empty
                };

            var pdfWords = page
                .GetWords()
                .Where(x =>
                    !string.IsNullOrWhiteSpace(
                        x.Text))
                .ToList();

            foreach (var word in pdfWords)
            {
                var box =
                    word.BoundingBox;

                result.Words.Add(
                    new HealthNetWordModel
                    {
                        Text =
                            word.Text.Trim(),

                        Left =
                            Convert.ToDouble(
                                box.Left),

                        Right =
                            Convert.ToDouble(
                                box.Right),

                        Top =
                            Convert.ToDouble(
                                box.Top),

                        Bottom =
                            Convert.ToDouble(
                                box.Bottom)
                    });
            }

            /*
             * Detect whether PdfPig found
             * meaningful machine-readable text.
             */
            result.IsScannedPage =
                IsProbablyScannedPage(result);

            /*
             * OCR fallback has not been used.
             * It will be connected later.
             */
            result.UsedOcrFallback =
                false;

            result.Lines =
                BuildLogicalLines(
                    result.Words);

            var lineBasedText =
                string.Join(
                    Environment.NewLine,
                    result.Lines.Select(
                        x => x.Text));

            /*
             * Prefer text rebuilt from
             * coordinate-aware rows.
             */
            var parserText =
                !string.IsNullOrWhiteSpace(
                    lineBasedText)
                    ? lineBasedText
                    : result.RawText;

            result.CleanText =
                _normalizer
                    .NormalizePageText(
                        parserText);

            return result;
        }

        private static bool
            IsProbablyScannedPage(
                HealthNetPageData page)
        {
            if (page.Words == null ||
                page.Words.Count == 0)
            {
                return true;
            }

            var characterCount =
                page.Words.Sum(
                    x =>
                        x.Text?.Length ?? 0);

            /*
             * Health Net EOP normally contains
             * substantial text.
             *
             * Very small extraction indicates
             * scanned/image content or broken
             * text layer.
             */
            return
                page.Words.Count < 10 ||
                characterCount < 50;
        }

        private static
            List<HealthNetTextLineModel>
            BuildLogicalLines(
                List<HealthNetWordModel> words)
        {
            var result =
                new List<
                    HealthNetTextLineModel>();

            if (words == null ||
                words.Count == 0)
            {
                return result;
            }

            /*
             * PDF coordinates normally start
             * bottom-left.
             *
             * Higher Top value means visually
             * higher position.
             */
            var orderedWords = words
                .OrderByDescending(
                    x => x.Top)
                .ThenBy(
                    x => x.Left)
                .ToList();

            var groups =
                new List<
                    List<HealthNetWordModel>>();

            foreach (var word in orderedWords)
            {
                List<HealthNetWordModel>?
                    selectedGroup = null;

                foreach (
                    var group in groups)
                {
                    var averageTop =
                        group.Average(
                            x => x.Top);

                    if (Math.Abs(
                            averageTop -
                            word.Top)
                        <= LineYTolerance)
                    {
                        selectedGroup =
                            group;

                        break;
                    }
                }

                if (selectedGroup == null)
                {
                    selectedGroup =
                        new List<
                            HealthNetWordModel>();

                    groups.Add(
                        selectedGroup);
                }

                selectedGroup.Add(word);
            }

            var orderedGroups =
                groups
                    .OrderByDescending(
                        x =>
                            x.Average(
                                w => w.Top))
                    .ToList();

            int lineNo = 1;

            foreach (
                var group in orderedGroups)
            {
                var lineWords = group
                    .OrderBy(
                        x => x.Left)
                    .ToList();

                var lineText =
                    string.Join(
                        " ",
                        lineWords
                            .Select(
                                x => x.Text));

                if (string.IsNullOrWhiteSpace(
                    lineText))
                {
                    continue;
                }

                result.Add(
                    new HealthNetTextLineModel
                    {
                        LineNo =
                            lineNo++,

                        Text =
                            lineText,

                        Left =
                            lineWords.Min(
                                x => x.Left),

                        Right =
                            lineWords.Max(
                                x => x.Right),

                        Top =
                            lineWords.Max(
                                x => x.Top),

                        Bottom =
                            lineWords.Min(
                                x => x.Bottom),

                        Words =
                            lineWords
                    });
            }

            return result;
        }
    }
}