using System;
using System.Collections.Generic;
using System.Printing;
using System.Runtime.InteropServices;
using PrintStudio.Models;

namespace PrintStudio.Services
{
    public static class PrinterService
    {
        [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int DeviceCapabilities(string pDevice, string pPort, short fwCapability, IntPtr pOutput, IntPtr pDevMode);

        private const short DC_ENUMRESOLUTIONS = 13;

        public static List<PrinterDeviceInfo> GetPrinters()
        {
            var list = new List<PrinterDeviceInfo>();
            string defaultPrinterName = string.Empty;

            try
            {
                using var server = new LocalPrintServer();
                defaultPrinterName = server.DefaultPrintQueue?.Name ?? string.Empty;
                var queues = server.GetPrintQueues();

                foreach (var q in queues)
                {
                    try
                    {
                        q.Refresh();
                        var info = new PrinterDeviceInfo
                        {
                            Name = q.Name,
                            IsDefault = (q.Name == defaultPrinterName),
                            IsOffline = q.IsOffline,
                            IsBusy = q.IsBusy,
                            IsOutOfPaper = q.IsOutOfPaper || q.QueueStatus.HasFlag(PrintQueueStatus.PaperOut),
                            IsPaperJammed = q.IsPaperJammed || q.QueueStatus.HasFlag(PrintQueueStatus.PaperJam),
                            IsDoorOpened = q.IsDoorOpened || q.QueueStatus.HasFlag(PrintQueueStatus.DoorOpen),
                            IsTonerLow = q.IsTonerLow || q.QueueStatus.HasFlag(PrintQueueStatus.TonerLow),
                            QueueCount = q.NumberOfJobs
                        };

                        // Determine hardware resolution
                        int maxDpi = 600;
                        var hwDpis = GetHardwareResolutions(q.Name);
                        if (hwDpis.Count > 0)
                        {
                            hwDpis.Sort();
                            maxDpi = hwDpis[^1];
                        }
                        info.MaxDpi = maxDpi;
                        info.QualityOptions = BuildQualityOptions(maxDpi, hwDpis);

                        list.Add(info);
                    }
                    catch { }
                }
            }
            catch
            {
                // Fallback gracefully
            }

            // Sort: Online printers first, then default
            list.Sort((a, b) =>
            {
                if (a.IsOffline != b.IsOffline) return a.IsOffline.CompareTo(b.IsOffline);
                if (a.IsDefault != b.IsDefault) return b.IsDefault.CompareTo(a.IsDefault);
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            return list;
        }

        public static List<PrintJobItem> GetPrintJobs(string printerName)
        {
            var jobsList = new List<PrintJobItem>();
            if (string.IsNullOrWhiteSpace(printerName)) return jobsList;

            try
            {
                using var server = new LocalPrintServer();
                using var queue = server.GetPrintQueue(printerName);
                queue.Refresh();
                var jobCollection = queue.GetPrintJobInfoCollection();

                foreach (PrintSystemJobInfo job in jobCollection)
                {
                    try
                    {
                        job.Refresh();
                        string status = "লাইনে অপেক্ষা করছে";
                        if (job.JobStatus.HasFlag(PrintJobStatus.Printing)) status = "প্রিন্ট হচ্ছে...";
                        else if (job.JobStatus.HasFlag(PrintJobStatus.Spooling)) status = "স্পুল হচ্ছে...";
                        else if (job.JobStatus.HasFlag(PrintJobStatus.Paused)) status = "পজ করা আছে";
                        else if (job.JobStatus.HasFlag(PrintJobStatus.Error) || job.JobStatus.HasFlag(PrintJobStatus.UserIntervention)) status = "ত্রুটি / ইউজার ইন্টারভেনশন";
                        else if (job.JobStatus.HasFlag(PrintJobStatus.Completed)) status = "সম্পন্ন";

                        string progress = job.NumberOfPages > 0
                            ? $"{job.NumberOfPagesPrinted} / {job.NumberOfPages} পেজ"
                            : "প্রস্তুত হচ্ছে";

                        jobsList.Add(new PrintJobItem
                        {
                            JobId = job.JobIdentifier,
                            DocumentName = string.IsNullOrWhiteSpace(job.Name) ? "অজ্ঞাত ডকুমেন্ট" : job.Name,
                            Status = status,
                            PagesProgress = progress,
                            TimeSubmitted = job.TimeJobSubmitted.ToLocalTime().ToString("hh:mm:ss tt"),
                            IsPaused = job.JobStatus.HasFlag(PrintJobStatus.Paused),
                            CanCancel = true
                        });
                    }
                    catch { }
                }
            }
            catch { }

            return jobsList;
        }

        public static bool CancelJob(string printerName, int jobId)
        {
            try
            {
                using var server = new LocalPrintServer();
                using var queue = server.GetPrintQueue(printerName);
                queue.Refresh();
                var job = queue.GetJob(jobId);
                job?.Cancel();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool PauseJob(string printerName, int jobId)
        {
            try
            {
                using var server = new LocalPrintServer();
                using var queue = server.GetPrintQueue(printerName);
                queue.Refresh();
                var job = queue.GetJob(jobId);
                job?.Pause();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool ResumeJob(string printerName, int jobId)
        {
            try
            {
                using var server = new LocalPrintServer();
                using var queue = server.GetPrintQueue(printerName);
                queue.Refresh();
                var job = queue.GetJob(jobId);
                job?.Resume();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static List<int> GetHardwareResolutions(string printerName)
        {
            var resList = new List<int>();
            try
            {
                int count = DeviceCapabilities(printerName, "", DC_ENUMRESOLUTIONS, IntPtr.Zero, IntPtr.Zero);
                if (count > 0)
                {
                    int structSize = 8; // 2 x LONG (x, y)
                    IntPtr pBuf = Marshal.AllocHGlobal(count * structSize);
                    if (DeviceCapabilities(printerName, "", DC_ENUMRESOLUTIONS, pBuf, IntPtr.Zero) > 0)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            int x = Marshal.ReadInt32(pBuf, i * structSize);
                            if (x > 0 && !resList.Contains(x)) resList.Add(x);
                        }
                    }
                    Marshal.FreeHGlobal(pBuf);
                }
            }
            catch { }
            return resList;
        }

        private static List<QualityOption> BuildQualityOptions(int maxDpi, List<int> hwDpis)
        {
            var candidates = new HashSet<int>(hwDpis) { maxDpi };
            if (maxDpi >= 2400) { candidates.Add(2400); candidates.Add(1200); candidates.Add(600); candidates.Add(300); candidates.Add(150); }
            else if (maxDpi >= 1200) { candidates.Add(1200); candidates.Add(600); candidates.Add(300); candidates.Add(150); }
            else if (maxDpi >= 600) { candidates.Add(600); candidates.Add(300); candidates.Add(150); }
            else if (maxDpi >= 300) { candidates.Add(300); candidates.Add(150); }
            else { candidates.Add(150); }

            var sorted = new List<int>();
            foreach (int d in candidates) if (d <= maxDpi && d >= 100) sorted.Add(d);
            sorted.Sort((a, b) => b.CompareTo(a));

            var options = new List<QualityOption>();
            foreach (int d in sorted)
            {
                bool isMax = (d == maxDpi);
                string lbl = $"{d} DPI";
                if (isMax)
                {
                    string desc = d >= 1200 ? "আল্ট্রা কোয়ালিটি" : (d >= 600 ? "হাই কোয়ালিটি" : "স্ট্যান্ডার্ড");
                    lbl += $" (এই প্রিন্টারের সর্বোচ্চ / {desc}) ★";
                }
                else if (d >= 1200) lbl += " (আল্ট্রা কোয়ালিটি / ফাইন)";
                else if (d >= 600) lbl += " (হাই কোয়ালিটি / স্পষ্ট)";
                else if (d >= 300) lbl += " (স্ট্যান্ডার্ড কোয়ালিটি)";
                else lbl += " (ড্রাফট / দ্রুত ও সাশ্রয়ী)";

                options.Add(new QualityOption
                {
                    Dpi = d,
                    Label = lbl,
                    IsDefault = isMax
                });
            }
            return options;
        }
    }
}
