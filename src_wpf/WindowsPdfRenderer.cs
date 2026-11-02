using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PrintStudio.Services
{
    public class PdfDocumentInfo
    {
        public int PageCount { get; set; }
        public double FirstPageWidth { get; set; }
        public double FirstPageHeight { get; set; }
        public bool IsPortrait => FirstPageHeight >= FirstPageWidth;
    }

    public static class WindowsPdfRenderer
    {
        public static async Task<PdfDocumentInfo> GetInfoAsync(string filePath)
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(filePath));
            PdfDocument doc = await PdfDocument.LoadFromFileAsync(file);
            int count = (int)doc.PageCount;
            double w = 595, h = 842;
            if (count > 0)
            {
                using var page = doc.GetPage(0);
                w = page.Size.Width;
                h = page.Size.Height;
            }
            return new PdfDocumentInfo
            {
                PageCount = count,
                FirstPageWidth = w,
                FirstPageHeight = h
            };
        }

        public static async Task<byte[]> RenderPageToPngBytesAsync(string filePath, uint pageIndex, uint targetWidth = 0, uint targetHeight = 0)
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(filePath));
            PdfDocument doc = await PdfDocument.LoadFromFileAsync(file);
            if (pageIndex >= doc.PageCount) pageIndex = 0;

            using var page = doc.GetPage(pageIndex);
            using var stream = new InMemoryRandomAccessStream();

            var renderOptions = new PdfPageRenderOptions();
            if (targetWidth > 0 && targetHeight > 0)
            {
                renderOptions.DestinationWidth = targetWidth;
                renderOptions.DestinationHeight = targetHeight;
            }

            await page.RenderToStreamAsync(stream, renderOptions);

            byte[] buffer = new byte[stream.Size];
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(buffer);

            return buffer;
        }
    }
}
