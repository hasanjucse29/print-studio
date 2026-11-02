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

        // Per-File Custom Settings Override
        private bool _useCustomSettings = false;
        public bool UseCustomSettings
        {
            get => _useCustomSettings;
            set { _useCustomSettings = value; OnPropertyChanged(nameof(UseCustomSettings)); }
        }

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

        private string _customColor = "default"; // "default", "bw", "color"
        public string CustomColor
        {
            get => _customColor;
            set { _customColor = value; OnPropertyChanged(nameof(CustomColor)); }
        }

        private string _customOrientation = "default"; // "default", "portrait", "landscape"
        public string CustomOrientation
        {
            get => _customOrientation;
            set { _customOrientation = value; OnPropertyChanged(nameof(CustomOrientation)); }
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

    public class PrinterHardwareAlert
    {
        public bool HasAlert { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Severity { get; set; } = "none"; // "warning", "danger", "info", "none"
        public string Icon { get; set; } = "fa-circle-check";
    }

    public class PrinterDeviceInfo
    {
        public string Name { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public bool IsOffline { get; set; }
        public bool IsBusy { get; set; }
        public bool IsOutOfPaper { get; set; }
        public bool IsPaperJammed { get; set; }
        public bool IsDoorOpened { get; set; }
        public bool IsTonerLow { get; set; }
        public int QueueCount { get; set; }
        public int MaxDpi { get; set; } = 600;
        public List<QualityOption> QualityOptions { get; set; } = new();

        public PrinterHardwareAlert HardwareAlert
        {
            get
            {
                if (IsPaperJammed)
                    return new PrinterHardwareAlert { HasAlert = true, Severity = "danger", Message = "কাগজ আটকে আছে (Paper Jam) — প্রিন্টারের রোলার ও ট্রে চেক করুন", Icon = "⚠️" };
                if (IsOutOfPaper)
                    return new PrinterHardwareAlert { HasAlert = true, Severity = "warning", Message = "কাগজ নেই (Paper Out) — প্রিন্টারের ট্রেতে কাগজ দিন", Icon = "⚠️" };
                if (IsDoorOpened)
                    return new PrinterHardwareAlert { HasAlert = true, Severity = "warning", Message = "প্রিন্টারের ঢাকনা খোলা (Door Open) — কভারটি বন্ধ করুন", Icon = "⚠️" };
                if (IsTonerLow)
                    return new PrinterHardwareAlert { HasAlert = true, Severity = "warning", Message = "কালি / টোনার শেষ পর্যায়ে (Toner Low)", Icon = "⚠️" };
                if (IsOffline)
                    return new PrinterHardwareAlert { HasAlert = true, Severity = "danger", Message = "প্রিন্টারটি বর্তমানে অফলাইনে আছে (সংযোগ বিচ্ছিন্ন)", Icon = "🔴" };
                
                return new PrinterHardwareAlert { HasAlert = false, Severity = "none", Message = "প্রিন্টার রেডি (অনলাইন)", Icon = "🟢" };
            }
        }

        public string StatusState => IsOffline ? "offline" : (IsBusy ? "busy" : "ready");
        public string StatusLabel
        {
            get
            {
                if (IsPaperJammed) return "কাগজ জ্যাম";
                if (IsOutOfPaper) return "কাগজ নেই";
                if (IsDoorOpened) return "কভার খোলা";
                if (IsOffline) return "অফলাইন";
                if (IsBusy) return $"ব্যস্ত ({QueueCount})";
                return "রেডি";
            }
        }

        public string DisplayText => $"{(IsOffline ? "🔴" : (IsOutOfPaper || IsPaperJammed ? "⚠️" : "🟢"))} {Name} [{StatusLabel}] {(IsDefault ? "★" : "")}";
        public override string ToString() => DisplayText;
    }

    public class PrintJobItem
    {
        public int JobId { get; set; }
        public string DocumentName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string PagesProgress { get; set; } = string.Empty;
        public string TimeSubmitted { get; set; } = string.Empty;
        public bool IsPaused { get; set; }
        public bool CanCancel { get; set; } = true;
    }
}
