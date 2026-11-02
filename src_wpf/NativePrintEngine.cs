using System;
using System.Collections.Generic;
using System.IO;
using System.Printing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Xps;
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

                // Safely obtain base PrintTicket without crashing on buggy printer drivers
                PrintTicket? baseTicket = null;
                try
                {
                    baseTicket = queue.UserPrintTicket?.Clone() ?? queue.DefaultPrintTicket?.Clone();
                }
                catch
                {
                    // Fall back to clean in-memory ticket if driver fails DEVMODE-to-PrintTicket conversion
                    try
                    {
                        baseTicket = new PrintTicket();
                    }
                    catch
                    {
                        baseTicket = null;
                    }
                }

                // Process each document
                for (int itemIdx = 0; itemIdx < items.Count; itemIdx++)
                {
                    if (ct.IsCancellationRequested) break;
                    var item = items[itemIdx];
                    onProgress(itemIdx + 1, items.Count, item.FileName);

                    // Granular per-file overrides
                    string effectiveColor = item.UseCustomSettings && item.CustomColor != "default"
                        ? item.CustomColor
                        : options.ColorMode;

                    string effectiveOrient = item.UseCustomSettings && item.CustomOrientation != "default"
                        ? item.CustomOrientation
                        : options.Orientation;

                    int effectiveCopies = item.UseCustomSettings ? item.Copies : options.Copies;
                    int nup = options.Nup;

                    var pagesToPrint = ParsePageRange(item.PageRange, item.PageCount);

                    bool docLandscape = effectiveOrient == "landscape" || (effectiveOrient == "auto" && item.DetectedOrientation == "landscape");
                    if (nup == 2) docLandscape = !docLandscape; // 2-up flips best
                    else if (nup == 6) docLandscape = true;

                    // Configure doc ticket safely
                    PrintTicket? docTicket = null;
                    if (baseTicket != null)
                    {
                        try
                        {
                            docTicket = baseTicket.Clone();
                            try { docTicket.OutputColor = effectiveColor == "color" ? OutputColor.Color : OutputColor.Monochrome; } catch { }
                            try { docTicket.CopyCount = Math.Max(1, effectiveCopies); } catch { }
                            try { docTicket.PageOrientation = docLandscape ? PageOrientation.Landscape : PageOrientation.Portrait; } catch { }

                            // Duplex
                            try
                            {
                                if (options.Duplex == "vertical") docTicket.Duplexing = Duplexing.TwoSidedLongEdge;
                                else if (options.Duplex == "horizontal") docTicket.Duplexing = Duplexing.TwoSidedShortEdge;
                                else docTicket.Duplexing = Duplexing.OneSided;
                            }
                            catch { }

                            // Paper size
                            try
                            {
                                if (options.PaperSize == "Letter") docTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.NorthAmericaLetter);
                                else if (options.PaperSize == "Legal") docTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.NorthAmericaLegal);
                                else docTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
                            }
                            catch { }
                        }
                        catch
                        {
                            docTicket = null;
                        }
                    }

                    // Standard sheet dimensions in 96 DPI units
                    double sheetW = docLandscape ? 1122 : 793;
                    double sheetH = docLandscape ? 793 : 1122;

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

                        var visual = ComposeSheetVisual(renderedImages, sheetW, sheetH, nup, options.DrawBorders, effectiveColor == "bw");

                        // Print sheet with multi-layer fallback
                        PrintSheetSafe(queue, visual, docTicket, $"Print Studio - {item.FileName} (Sheet {i / nup + 1})");
                    }
                }
            }, ct);
        }

        private static void PrintSheetSafe(PrintQueue queue, Visual visual, PrintTicket? docTicket, string description)
        {
            // Layer 1: XpsDocumentWriter with customized PrintTicket
            if (docTicket != null)
            {
                try
                {
                    var xpsWriter = PrintQueue.CreateXpsDocumentWriter(queue);
                    xpsWriter.Write(visual, docTicket);
                    return;
                }
                catch
                {
                    // Fall through to Layer 2 if driver failed DEVMODE conversion
                }
            }

            // Layer 2: XpsDocumentWriter with driver native default (bypasses XML PrintTicket conversion)
            try
            {
                var xpsWriter = PrintQueue.CreateXpsDocumentWriter(queue);
                xpsWriter.Write(visual);
                return;
            }
            catch
            {
                // Fall through to Layer 3
            }

            // Layer 3: PrintDialog.PrintVisual fallback
            try
            {
                var printDialog = new System.Windows.Controls.PrintDialog();
                printDialog.PrintQueue = queue;
                printDialog.PrintVisual(visual, description);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"প্রিন্টারে ডেটা পাঠানো যায়নি ({queue.Name}): {ex.Message}", ex);
            }
        }

        public static async Task<BitmapSource> RenderPreviewSheetAsync(
            PrintItem item,
            int sheetIndex,
            PrintEngineOptions options)
        {
            string effectiveColor = item.UseCustomSettings && item.CustomColor != "default"
                ? item.CustomColor
                : options.ColorMode;

            string effectiveOrient = item.UseCustomSettings && item.CustomOrientation != "default"
                ? item.CustomOrientation
                : options.Orientation;

            int nup = options.Nup;
            var pagesToPrint = ParsePageRange(item.PageRange, item.PageCount);
            int startPageIdx = sheetIndex * nup;

            if (startPageIdx >= pagesToPrint.Count) startPageIdx = 0;
            int take = Math.Min(nup, pagesToPrint.Count - startPageIdx);
            var chunkIndices = pagesToPrint.GetRange(startPageIdx, take);

            bool docLandscape = effectiveOrient == "landscape" || (effectiveOrient == "auto" && item.DetectedOrientation == "landscape");
            if (nup == 2) docLandscape = !docLandscape;
            else if (nup == 6) docLandscape = true;

            double sheetW = docLandscape ? 1122 : 793;
            double sheetH = docLandscape ? 793 : 1122;

            var renderedImages = new List<BitmapSource>();
            foreach (uint pageIdx in chunkIndices)
            {
                byte[] pngBytes = await WindowsPdfRenderer.RenderPageToPngBytesAsync(item.FilePath, pageIdx, 800, 1100);
                using var ms = new MemoryStream(pngBytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                renderedImages.Add(bmp);
            }

            var visual = ComposeSheetVisual(renderedImages, sheetW, sheetH, nup, options.DrawBorders, effectiveColor == "bw");

            var rtb = new RenderTargetBitmap((int)sheetW, (int)sheetH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        private static Visual ComposeSheetVisual(List<BitmapSource> images, double width, double height, int nup, bool drawBorders, bool isGrayscale)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Clean Sheet Background
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

                var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(203, 213, 225)), 1.0);
                borderPen.Freeze();

                for (int i = 0; i < images.Count; i++)
                {
                    int r = i / cols;
                    int c = i % cols;
                    double x0 = c * cellW + padX;
                    double y0 = r * cellH + padY;

                    var img = images[i];
                    double imgW = img.PixelWidth;
                    double imgH = img.PixelHeight;

                    // Fit proportionally
                    double scale = Math.Min(availW / imgW, availH / imgH);
                    double drawW = imgW * scale;
                    double drawH = imgH * scale;
                    double drawX = x0 + (availW - drawW) / 2.0;
                    double drawY = y0 + (availH - drawH) / 2.0;

                    BitmapSource finalImg = img;
                    if (isGrayscale && img.Format != PixelFormats.Gray8)
                    {
                        var grayBmp = new FormatConvertedBitmap();
                        grayBmp.BeginInit();
                        grayBmp.Source = img;
                        grayBmp.DestinationFormat = PixelFormats.Gray8;
                        grayBmp.EndInit();
                        grayBmp.Freeze();
                        finalImg = grayBmp;
                    }

                    dc.DrawImage(finalImg, new Rect(drawX, drawY, drawW, drawH));

                    if (drawBorders)
                    {
                        dc.DrawRectangle(null, borderPen, new Rect(drawX, drawY, drawW, drawH));
                    }
                }
            }
            return dv;
        }

        public static List<uint> ParsePageRange(string rangeStr, int maxPages)
        {
            var result = new List<uint>();
            if (string.IsNullOrWhiteSpace(rangeStr) || rangeStr.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                for (uint i = 0; i < maxPages; i++) result.Add(i);
                return result;
            }

            string clean = rangeStr.Trim().ToLowerInvariant();
            if (clean == "odd")
            {
                for (uint i = 0; i < maxPages; i++) if ((i + 1) % 2 != 0) result.Add(i);
                return result;
            }
            if (clean == "even")
            {
                for (uint i = 0; i < maxPages; i++) if ((i + 1) % 2 == 0) result.Add(i);
                return result;
            }

            var parts = clean.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            var pageSet = new HashSet<uint>();

            foreach (var part in parts)
            {
                var p = part.Trim();
                if (p.Contains('-'))
                {
                    var segs = p.Split('-');
                    if (segs.Length == 2 && uint.TryParse(segs[0].Trim(), out uint start) && uint.TryParse(segs[1].Trim(), out uint end))
                    {
                        for (uint i = Math.Max(1, start); i <= Math.Min((uint)maxPages, end); i++)
                        {
                            pageSet.Add(i - 1);
                        }
                    }
                }
                else if (uint.TryParse(p, out uint singlePage))
                {
                    if (singlePage >= 1 && singlePage <= maxPages)
                    {
                        pageSet.Add(singlePage - 1);
                    }
                }
            }

            result.AddRange(pageSet);
            result.Sort();
            if (result.Count == 0)
            {
                for (uint i = 0; i < maxPages; i++) result.Add(i);
            }
            return result;
        }
    }
}
