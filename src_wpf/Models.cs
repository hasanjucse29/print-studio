using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows.Media.Imaging;

namespace PrintStudio.Models
{
    public class PrintItem : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string FilePath { get; set; } = string.Empty;
        public string FileName => Path.GetFileName(FilePath);
        public string FileSizeFormatted { get; set; } = string.Empty;
        public int PageCount { get; set; }
        public string DetectedOrientation { get; set; } = "portrait";
        public BitmapSource? Thumbnail { get; set; }

        private string _pageRange = "all";
        public string PageRange
        {
            get => _pageRange;
            set { _pageRange = value; OnPropertyChanged(nameof(PageRange)); }
        }

        private int _copies = 1;
        public int Copies
        {
            get => _copies;
            set { _copies = value; OnPropertyChanged(nameof(Copies)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }

    public class QualityOption
    {
        public int Dpi { get; set; }
        public string Label { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public override string ToString() => Label;
    }

    public class PrinterDeviceInfo
    {
        public string Name { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public bool IsOffline { get; set; }
        public bool IsBusy { get; set; }
        public int QueueCount { get; set; }
        public int MaxDpi { get; set; } = 600;
        public List<QualityOption> QualityOptions { get; set; } = new();

        public string StatusState => IsOffline ? "offline" : (IsBusy ? "busy" : "ready");
        public string StatusLabel => IsOffline ? "অফলাইন" : (IsBusy ? $"ব্যস্ত ({QueueCount})" : "রেডি");
        public string DisplayText => $"{(IsOffline ? "🔴" : "🟢")} {Name} [{(IsOffline ? "অফলাইন" : "রেডি")}] {(IsDefault ? "★" : "")}";
        public override string ToString() => DisplayText;
    }
}
