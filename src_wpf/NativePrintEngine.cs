using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct DOCINFO
    {
        public int cbSize;
        [MarshalAs(UnmanagedType.LPTStr)] public string lpszDocName;
        [MarshalAs(UnmanagedType.LPTStr)] public string? lpszOutput;
        [MarshalAs(UnmanagedType.LPTStr)] public string? lpszDatatype;
        public int fwType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct DEVMODE
    {
        private const int CCHDEVICENAME = 32;
        private const int CCHFORMNAME = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;

        public short dmOrientation;
        public short dmPaperSize;
        public short dmPaperLength;
        public short dmPaperWidth;
        public short dmScale;
        public short dmCopies;
        public short dmDefaultSource;
        public short dmPrintQuality;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
        public string dmFormName;
        public short dmLogPixels;
        public short dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmNup;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    public static class NativePrintEngine
    {
        // GDI Win32 API
        [DllImport("gdi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CreateDC(string lpszDriver, string lpszDevice, string? lpszOutput, IntPtr lpInitData);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int StartDoc(IntPtr hdc, ref DOCINFO lpdi);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int EndDoc(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int StartPage(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int EndPage(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int SetStretchBltMode(IntPtr hdc, int mode);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int StretchDIBits(
            IntPtr hdc,
            int XDest, int YDest, int nDestWidth, int nDestHeight,
            int XSrc, int YSrc, int nSrcWidth, int nSrcHeight,
            byte[] lpBits,
            ref BITMAPINFO lpBitsInfo,
            uint iUsage,
            uint dwRop);

        // WinSpool Win32 API
        [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

        [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int DocumentProperties(IntPtr hWnd, IntPtr hPrinter, string pDeviceName, IntPtr pDevModeOutput, IntPtr pDevModeInput, int fMode);

        private const int HORZRES = 8;
        private const int VERTRES = 10;
        private const int HALFTONE = 4;
        private const uint DIB_RGB_COLORS = 0;
        private const uint SRCCOPY = 0x00CC0020;

        private const int DM_ORIENTATION = 0x00000001;
        private const int DM_PAPERSIZE = 0x00000002;
        private const int DM_COPIES = 0x00000100;
        private const int DM_COLOR = 0x00000800;
        private const int DM_DUPLEX = 0x00001000;
        private const int DM_PRINTQUALITY = 0x00000400;
        private const int DM_YRESOLUTION = 0x00002000;

        public static async Task PrintBatchAsync(
            List<PrintItem> items,
            PrintEngineOptions options,
            Action<int, int, string> onProgress,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(options.PrinterName))
            {
                throw new InvalidOperationException("কোনো প্রিন্টার নির্বাচিত করা হয়নি।");
            }

            bool isVirtual = options.PrinterName.IndexOf("PDF", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             options.PrinterName.IndexOf("XPS", StringComparison.OrdinalIgnoreCase) >= 0;

            for (int itemIdx = 0; itemIdx < items.Count; itemIdx++)
            {
                if (ct.IsCancellationRequested) break;
                var item = items[itemIdx];
                onProgress(itemIdx + 1, items.Count, item.FileName);

                string effectiveColor = item.UseCustomSettings && item.CustomColor != "default"
                    ? item.CustomColor
                    : options.ColorMode;

                string effectiveOrient = item.UseCustomSettings && item.CustomOrientation != "default"
                    ? item.CustomOrientation
                    : options.Orientation;

                int effectiveCopies = item.UseCustomSettings ? item.Copies : options.Copies;
                int nup = options.Nup;

                var pagesToPrint = ParsePageRange(item.PageRange, item.PageCount);
                if (pagesToPrint.Count == 0) continue;

                bool docLandscape = effectiveOrient == "landscape" || (effectiveOrient == "auto" && item.DetectedOrientation == "landscape");
                if (nup == 2) docLandscape = !docLandscape; // 2-up flips best
                else if (nup == 6) docLandscape = true;

                // Prepare DEVMODE for this document
                IntPtr pDevMode = IntPtr.Zero;
                IntPtr hPrinter;
                if (OpenPrinter(options.PrinterName, out hPrinter, IntPtr.Zero) != 0)
                {
                    try
                    {
                        int size = DocumentProperties(IntPtr.Zero, hPrinter, options.PrinterName, IntPtr.Zero, IntPtr.Zero, 0);
                        if (size > 0)
                        {
                            pDevMode = Marshal.AllocHGlobal(size);
                            if (DocumentProperties(IntPtr.Zero, hPrinter, options.PrinterName, pDevMode, IntPtr.Zero, 2) == 1) // 2 = DM_OUT_BUFFER
                            {
                                var devMode = Marshal.PtrToStructure<DEVMODE>(pDevMode);

                                devMode.dmOrientation = (short)(docLandscape ? 2 : 1);
                                devMode.dmFields |= DM_ORIENTATION;

                                if (effectiveCopies > 1)
                                {
                                    devMode.dmCopies = (short)effectiveCopies;
                                    devMode.dmFields |= DM_COPIES;
                                }

                                if (options.Duplex == "vertical")
                                {
                                    devMode.dmDuplex = 2; // DMDUP_VERTICAL
                                    devMode.dmFields |= DM_DUPLEX;
                                }
                                else if (options.Duplex == "horizontal")
                                {
                                    devMode.dmDuplex = 3; // DMDUP_HORIZONTAL
                                    devMode.dmFields |= DM_DUPLEX;
                                }
                                else
                                {
                                    devMode.dmDuplex = 1; // DMDUP_SIMPLEX
                                    devMode.dmFields |= DM_DUPLEX;
                                }

                                if (options.PaperSize == "Letter") devMode.dmPaperSize = 1;
                                else if (options.PaperSize == "Legal") devMode.dmPaperSize = 5;
                                else devMode.dmPaperSize = 9; // A4
                                devMode.dmFields |= DM_PAPERSIZE;

                                devMode.dmColor = (short)(effectiveColor == "color" ? 2 : 1);
                                devMode.dmFields |= DM_COLOR;

                                if (options.Dpi > 0)
                                {
                                    devMode.dmPrintQuality = (short)options.Dpi;
                                    devMode.dmYResolution = (short)options.Dpi;
                                    devMode.dmFields |= DM_PRINTQUALITY | DM_YRESOLUTION;
                                }

                                Marshal.StructureToPtr(devMode, pDevMode, false);
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        ClosePrinter(hPrinter);
                    }
                }

                // Create native GDI Printer DC
                IntPtr hdc = CreateDC("WINSPOOL", options.PrinterName, null, pDevMode);
                if (pDevMode != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pDevMode);
                }

                if (hdc == IntPtr.Zero)
                {
                    int win32Err = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException($"প্রিন্টারের সাথে সংযোগ স্থাপন করা যায়নি ({options.PrinterName})। Win32 Error: {win32Err}");
                }

                try
                {
                    int dcW = GetDeviceCaps(hdc, HORZRES);
                    int dcH = GetDeviceCaps(hdc, VERTRES);
                    if (dcW <= 0 || dcH <= 0)
                    {
                        dcW = docLandscape ? 3508 : 2480;
                        dcH = docLandscape ? 2480 : 3508;
                    }

                    var di = new DOCINFO();
                    di.cbSize = Marshal.SizeOf(typeof(DOCINFO));
                    di.lpszDocName = $"Print Studio - {item.FileName}";

                    if (isVirtual)
                    {
                        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        string outDir = Path.Combine(desktop, "Print_Studio_Output");
                        Directory.CreateDirectory(outDir);
                        string cleanName = Path.GetFileNameWithoutExtension(item.FileName);
                        di.lpszOutput = Path.Combine(outDir, $"{cleanName}_printed_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
                    }

                    int startDocRes = StartDoc(hdc, ref di);
                    if (startDocRes <= 0)
                    {
                        int err = Marshal.GetLastWin32Error();
                        throw new InvalidOperationException($"প্রিন্ট জব শুরু করা যায়নি (StartDoc Error: {err})");
                    }

                    SetStretchBltMode(hdc, HALFTONE);

                    // Sheet canvas size in pixels (high quality 300 DPI canvas)
                    double compW = docLandscape ? 3508 : 2480;
                    double compH = docLandscape ? 2480 : 3508;

                    for (int i = 0; i < pagesToPrint.Count; i += nup)
                    {
                        if (ct.IsCancellationRequested) break;
                        int take = Math.Min(nup, pagesToPrint.Count - i);
                        var chunkIndices = pagesToPrint.GetRange(i, take);

                        var renderedImages = new List<BitmapSource>();
                        foreach (uint pageIdx in chunkIndices)
                        {
                            byte[] pngBytes = await WindowsPdfRenderer.RenderPageToPngBytesAsync(item.FilePath, pageIdx, 1800, 2400);
                            using var ms = new MemoryStream(pngBytes);
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.StreamSource = ms;
                            bmp.EndInit();
                            bmp.Freeze();
                            renderedImages.Add(bmp);
                        }

                        // Compose visual sheet
                        var visual = ComposeSheetVisual(renderedImages, compW, compH, nup, options.DrawBorders, effectiveColor == "bw");

                        // Render visual to RenderTargetBitmap
                        var rtb = new RenderTargetBitmap((int)compW, (int)compH, 96, 96, PixelFormats.Pbgra32);
                        rtb.Render(visual);
                        rtb.Freeze();

                        int bmpW = rtb.PixelWidth;
                        int bmpH = rtb.PixelHeight;
                        int stride = bmpW * 4;
                        byte[] pixels = new byte[stride * bmpH];
                        rtb.CopyPixels(pixels, stride, 0);

                        var bmi = new BITMAPINFO();
                        bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                        bmi.bmiHeader.biWidth = bmpW;
                        bmi.bmiHeader.biHeight = -bmpH; // Top-down DIB
                        bmi.bmiHeader.biPlanes = 1;
                        bmi.bmiHeader.biBitCount = 32;
                        bmi.bmiHeader.biCompression = 0; // BI_RGB
                        bmi.bmiHeader.biSizeImage = (uint)pixels.Length;

                        StartPage(hdc);
                        StretchDIBits(hdc, 0, 0, dcW, dcH, 0, 0, bmpW, bmpH, pixels, ref bmi, DIB_RGB_COLORS, SRCCOPY);
                        EndPage(hdc);

                        await Task.Delay(1, ct);
                    }

                    EndDoc(hdc);
                }
                finally
                {
                    DeleteDC(hdc);
                }
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

                var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(203, 213, 225)), Math.Max(1.5, width / 800.0));
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
