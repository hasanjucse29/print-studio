using System;
using System.Collections.Generic;
using System.IO;
using System.Printing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintStudio.Models;

namespace PrintStudio.Services
{
    public class PrintEngineOptions
    {
        public string PrinterName { get; set; } = string.Empty;
        public string ColorMode { get; set; } = "bw"; // "bw" or "color"
        public string PaperSize { get; set; } = "A4"; // A4, Letter, Legal
        public string Duplex { get; set; } = "simplex"; // simplex, vertical, horizontal
        public int Nup { get; set; } = 1; // 1, 2, 4, 6
        public int Dpi { get; set; } = 600;
        public int Copies { get; set; } = 1;
        public bool DrawBorders { get; set; } = true;
        public string Orientation { get; set; } = "auto"; // auto, portrait, landscape
    }

    public static class NativePrintEngine
    {
        public static async Task PrintBatchAsync(
            List<PrintItem> items,
            PrintEngineOptions options,
            Action<int, int, string> onProgress,
            CancellationToken ct)
        {
            await Task.Run(async () =>
            {
                using var server = new LocalPrintServer();
                PrintQueue queue;
                try
                {
                    queue = server.GetPrintQueue(options.PrinterName);
                }
                catch
                {
                    queue = server.DefaultPrintQueue;
                }

                var ticket = queue.DefaultPrintTicket.Clone();

                // 1. Color mode
                ticket.OutputColor = options.ColorMode == "color" ? OutputColor.Color : OutputColor.Monochrome;

                // 2. Duplex
                if (options.Duplex == "vertical")
                    ticket.Duplexing = Duplexing.TwoSidedLongEdge;
                else if (options.Duplex == "horizontal")
                    ticket.Duplexing = Duplexing.TwoSidedShortEdge;
                else
                    ticket.Duplexing = Duplexing.OneSided;

                // 3. Page media size
                if (options.PaperSize == "Letter")
                    ticket.PageMediaSize = new PageMediaSize(PageMediaSizeName.NorthAmericaLetter);
                else if (options.PaperSize == "Legal")
                    ticket.PageMediaSize = new PageMediaSize(PageMediaSizeName.NorthAmericaLegal);
                else
                    ticket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);

                // 4. Copies
                ticket.CopyCount = Math.Max(1, options.Copies);

                // Process each document
                for (int itemIdx = 0; itemIdx < items.Count; itemIdx++)
                {
                    if (ct.IsCancellationRequested) break;
                    var item = items[itemIdx];
                    onProgress(itemIdx + 1, items.Count, item.FileName);

                    var pagesToPrint = ParsePageRange(item.PageRange, item.PageCount);
                    int nup = options.Nup;

                    var docTicket = ticket.Clone();
                    bool docLandscape = options.Orientation == "landscape" || (options.Orientation == "auto" && item.DetectedOrientation == "landscape");
                    if (nup == 2) docLandscape = !docLandscape; // 2-up flips best
                    else if (nup == 6) docLandscape = true;

                    docTicket.PageOrientation = docLandscape ? PageOrientation.Landscape : PageOrientation.Portrait;

                    var printDialog = new PrintDialog();
                    printDialog.PrintQueue = queue;
                    printDialog.PrintTicket = docTicket;

                    // Standard sheet dimensions in 96 DPI units
                    double sheetW = docLandscape ? 1122 : 793;
                    double sheetH = docLandscape ? 793 : 1122;

                    // Group pages into N-up chunks
                    for (int i = 0; i < pagesToPrint.Count; i += nup)
                    {
                        if (ct.IsCancellationRequested) break;
                        int take = Math.Min(nup, pagesToPrint.Count - i);
                        var chunkIndices = pagesToPrint.GetRange(i, take);

                        var renderedImages = new List<BitmapSource>();
                        foreach (uint pageIdx in chunkIndices)
                        {
                            byte[] pngBytes = await WindowsPdfRenderer.RenderPageToPngBytesAsync(item.FilePath, pageIdx, 1200, 1600);
                            using var ms = new MemoryStream(pngBytes);
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.StreamSource = ms;
                            bmp.EndInit();
                            bmp.Freeze();
                            renderedImages.Add(bmp);
                        }

                        // Compose N-up visually using WPF Visual/DrawingContext
                        var visual = ComposeSheetVisual(renderedImages, sheetW, sheetH, nup, options.DrawBorders, options.ColorMode == "bw");
                        printDialog.PrintVisual(visual, $"Print Studio - {item.FileName} (Sheet {i / nup + 1})");
                    }
                }
            }, ct);
        }

        private static Visual ComposeSheetVisual(List<BitmapSource> images, double width, double height, int nup, bool drawBorders, bool isGrayscale)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Background
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));

                int cols = 1, rows = 1;
                bool isLandscape = width > height;
                if (nup == 2) { cols = isLandscape ? 2 : 1; rows = isLandscape ? 1 : 2; }
                else if (nup == 4) { cols = 2; rows = 2; }
                else if (nup == 6) { cols = isLandscape ? 3 : 2; rows = isLandscape ? 2 : 3; }

                double cellW = width / cols;
                double cellH = height / rows;
                double padX = cellW * 0.03;
                double padY = cellH * 0.03;
                double availW = cellW - (padX * 2);
                double availH = cellH - (padY * 2);

                var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(200, 205, 215)), 1.0);

                for (int idx = 0; idx < images.Count; idx++)
                {
                    if (idx >= cols * rows) break;
                    int col = idx % cols;
                    int row = idx / cols;

                    double x0 = col * cellW + padX;
                    double y0 = row * cellH + padY;

                    var img = images[idx];
                    double scale = Math.Min(availW / img.PixelWidth, availH / img.PixelHeight);
                    double drawW = img.PixelWidth * scale;
                    double drawH = img.PixelHeight * scale;

                    double pasteX = x0 + (availW - drawW) / 2.0;
                    double pasteY = y0 + (availH - drawH) / 2.0;

                    BitmapSource finalImg = img;
                    if (isGrayscale && img.Format != PixelFormats.Gray8)
                    {
                        finalImg = new FormatConvertedBitmap(img, PixelFormats.Gray8, null, 0);
                    }

                    var rect = new Rect(pasteX, pasteY, drawW, drawH);
                    dc.DrawImage(finalImg, rect);

                    if (drawBorders && nup > 1)
                    {
                        dc.DrawRectangle(null, borderPen, rect);
                    }
                }
            }
            return dv;
        }

        public static List<int> ParsePageRange(string rangeStr, int totalPages)
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(rangeStr) || rangeStr.Trim().ToLower() == "all")
            {
                for (int i = 0; i < totalPages; i++) result.Add(i);
                return result;
            }

            string clean = rangeStr.Trim().ToLower();
            if (clean == "odd")
            {
                for (int i = 0; i < totalPages; i++) if ((i + 1) % 2 != 0) result.Add(i);
                return result;
            }
            if (clean == "even")
            {
                for (int i = 0; i < totalPages; i++) if ((i + 1) % 2 == 0) result.Add(i);
                return result;
            }

            var parts = clean.Split(',');
            foreach (var p in parts)
            {
                string s = p.Trim();
                if (s.Contains('-'))
                {
                    var seg = s.Split('-');
                    if (seg.Length == 2 && int.TryParse(seg[0], out int start) && int.TryParse(seg[1], out int end))
                    {
                        for (int i = Math.Max(1, start); i <= Math.Min(totalPages, end); i++)
                        {
                            int zeroIdx = i - 1;
                            if (!result.Contains(zeroIdx)) result.Add(zeroIdx);
                        }
                    }
                }
                else if (int.TryParse(s, out int single))
                {
                    if (single >= 1 && single <= totalPages)
                    {
                        int zeroIdx = single - 1;
                        if (!result.Contains(zeroIdx)) result.Add(zeroIdx);
                    }
                }
            }
            return result.Count > 0 ? result : new List<int> { 0 };
        }
    }
}
