using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PrintStudio.Models;
using PrintStudio.Services;

namespace PrintStudio
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<PrintItem> Queue { get; } = new();
        private PrinterDeviceInfo? _selectedPrinter;
        private int _selectedNup = 1;
        private bool _isInitialized = false;
        private readonly DispatcherTimer _queueAutoRefreshTimer;

        public MainWindow()
        {
            InitializeComponent();
            _isInitialized = true;
            FileList.ItemsSource = Queue;
            CopiesText.TextChanged += (s, e) => { if (_isInitialized) UpdateSummary(); };

            // Setup auto-refresh timer for Live Print Queue
            _queueAutoRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _queueAutoRefreshTimer.Tick += async (s, e) =>
            {
                if (QueueView.Visibility == Visibility.Visible && _selectedPrinter != null)
                {
                    await LoadPrintJobsAsync(isBackgroundPoll: true);
                }
            };
            _queueAutoRefreshTimer.Start();

            Loaded += MainWindow_Loaded;
            Closing += (s, e) => _queueAutoRefreshTimer.Stop();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshPrintersAsync();
            UpdateSummary();
        }

        private async Task RefreshPrintersAsync()
        {
            ActivePrinterNameText.Text = "প্রিন্টার খোঁজা হচ্ছে...";
            var printers = await Task.Run(() => PrinterService.GetPrinters());

            PrinterCombo.ItemsSource = printers;
            if (printers.Count > 0)
            {
                var preferred = printers.FirstOrDefault(p => !p.IsOffline && p.IsDefault)
                             ?? printers.FirstOrDefault(p => !p.IsOffline)
                             ?? printers[0];
                PrinterCombo.SelectedItem = preferred;
            }
        }

        private void PrinterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PrinterCombo.SelectedItem is PrinterDeviceInfo printer)
            {
                _selectedPrinter = printer;
                ActivePrinterNameText.Text = printer.Name;
                ActivePrinterStatusTag.Text = printer.StatusLabel;

                if (printer.IsOffline)
                {
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                    ActivePrinterStatusTag.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                }
                else if (printer.IsBusy)
                {
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber
                    ActivePrinterStatusTag.Foreground = new SolidColorBrush(Color.FromRgb(217, 119, 6));
                }
                else
                {
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green
                    ActivePrinterStatusTag.Foreground = new SolidColorBrush(Color.FromRgb(5, 150, 105));
                }

                // Update Quality options with max pre-selected
                QualityCombo.ItemsSource = printer.QualityOptions;
                var maxOpt = printer.QualityOptions.FirstOrDefault(q => q.IsDefault) ?? printer.QualityOptions.FirstOrDefault();
                QualityCombo.SelectedItem = maxOpt;

                UpdateHardwareAlert(printer);
                UpdateSummary();

                if (QueueView.Visibility == Visibility.Visible)
                {
                    _ = LoadPrintJobsAsync();
                }
            }
        }

        private void UpdateHardwareAlert(PrinterDeviceInfo printer)
        {
            if (HardwareAlertBanner == null) return;

            var alert = printer.HardwareAlert;
            if (alert.HasAlert)
            {
                HardwareAlertBanner.Visibility = Visibility.Visible;
                if (AlertIconText != null) AlertIconText.Text = alert.Icon;
                if (AlertMessageText != null) AlertMessageText.Text = alert.Message;

                if (alert.Severity == "danger")
                {
                    HardwareAlertBanner.Background = new SolidColorBrush(Color.FromRgb(254, 242, 242));
                    HardwareAlertBanner.BorderBrush = new SolidColorBrush(Color.FromRgb(254, 202, 202));
                    if (AlertMessageText != null) AlertMessageText.Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28));
                }
                else
                {
                    HardwareAlertBanner.Background = new SolidColorBrush(Color.FromRgb(254, 249, 195));
                    HardwareAlertBanner.BorderBrush = new SolidColorBrush(Color.FromRgb(253, 224, 71));
                    if (AlertMessageText != null) AlertMessageText.Foreground = new SolidColorBrush(Color.FromRgb(161, 98, 7));
                }
            }
            else
            {
                HardwareAlertBanner.Visibility = Visibility.Collapsed;
            }
        }

        private void SettingChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitialized)
            {
                UpdateSummary();
            }
        }

        private void Nup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int nup))
            {
                _selectedNup = nup;
                ResetNupButtons();
                btn.Background = new SolidColorBrush(Color.FromRgb(239, 246, 255));
                btn.Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                btn.BorderBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                if (_isInitialized) UpdateSummary();
            }
        }

        private void ResetNupButtons()
        {
            var defBg = Brushes.White;
            var defFg = new SolidColorBrush(Color.FromRgb(71, 85, 105));
            var defBorder = new SolidColorBrush(Color.FromRgb(203, 213, 225));

            BtnNup1.Background = defBg; BtnNup1.Foreground = defFg; BtnNup1.BorderBrush = defBorder;
            BtnNup2.Background = defBg; BtnNup2.Foreground = defFg; BtnNup2.BorderBrush = defBorder;
            BtnNup4.Background = defBg; BtnNup4.Foreground = defFg; BtnNup4.BorderBrush = defBorder;
            BtnNup6.Background = defBg; BtnNup6.Foreground = defFg; BtnNup6.BorderBrush = defBorder;
        }

        private void BtnCopyMinus_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(CopiesText.Text, out int c))
            {
                CopiesText.Text = Math.Max(1, c - 1).ToString();
                if (_isInitialized) UpdateSummary();
            }
        }

        private void BtnCopyPlus_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(CopiesText.Text, out int c))
            {
                CopiesText.Text = Math.Min(99, c + 1).ToString();
                if (_isInitialized) UpdateSummary();
            }
        }

        private void UpdateSummary()
        {
            if (!_isInitialized) return;

            if (_selectedPrinter != null && SummaryPrinterName != null && SummaryStatusTag != null)
            {
                SummaryPrinterName.Text = _selectedPrinter.Name;
                SummaryStatusTag.Text = _selectedPrinter.StatusLabel;
            }

            string color = (ColorCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "color" ? "কালার" : "B&W";
            string paper = (PaperCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "A4";
            int dpi = (QualityCombo?.SelectedItem as QualityOption)?.Dpi ?? 600;
            if (SummarySpecs != null)
            {
                SummarySpecs.Text = $"{color} • {paper} • {dpi} DPI";
            }

            // Calculate sheet estimate
            int totalSheets = 0;
            int nup = _selectedNup;
            int copies = (CopiesText != null && int.TryParse(CopiesText.Text, out int cp)) ? cp : 1;

            foreach (var item in Queue)
            {
                var pList = NativePrintEngine.ParsePageRange(item.PageRange, item.PageCount);
                int itemCopies = item.UseCustomSettings ? Math.Max(1, item.Copies) : copies;
                int sheets = (int)Math.Ceiling((double)pList.Count / nup) * itemCopies;
                totalSheets += sheets;
            }

            string duplex = (DuplexCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "simplex";
            int physicalSheets = duplex == "simplex" ? totalSheets : (int)Math.Ceiling((double)totalSheets / 2);
            if (SummarySheets != null)
            {
                SummarySheets.Text = $"{physicalSheets} টি কাগজ{(duplex != "simplex" ? " (ডুপ্লেক্স)" : "")}";
            }

            if (QueueStatsText != null)
            {
                QueueStatsText.Text = $"প্রিন্ট কিউ ({Queue.Count}টি ফাইল • মোট {Queue.Sum(q => q.PageCount)} পেজ)";
            }
            if (BtnPrintAll != null)
            {
                BtnPrintAll.IsEnabled = Queue.Count > 0;
            }
        }

        private async void DropZone_Drop(object sender, DragEventArgs e)
        {
            DropZone.Background = Brushes.White;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                await AddPdfFilesAsync(files);
            }
        }

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                DropZone.Background = new SolidColorBrush(Color.FromRgb(239, 246, 255));
            }
            e.Handled = true;
        }

        private void DropZone_DragLeave(object sender, DragEventArgs e)
        {
            DropZone.Background = Brushes.White;
        }

        private async void DropZone_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            OpenFileDialog dlg = new()
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                Multiselect = true
            };
            if (dlg.ShowDialog() == true)
            {
                await AddPdfFilesAsync(dlg.FileNames);
            }
        }

        private async void BtnAddMore_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new()
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                Multiselect = true
            };
            if (dlg.ShowDialog() == true)
            {
                await AddPdfFilesAsync(dlg.FileNames);
            }
        }

        private async Task AddPdfFilesAsync(string[] paths)
        {
            foreach (string p in paths)
            {
                if (!p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
                if (!File.Exists(p)) continue;

                try
                {
                    var info = await WindowsPdfRenderer.GetInfoAsync(p);
                    var fi = new FileInfo(p);
                    string sizeStr = fi.Length < 1024 * 1024
                        ? $"{fi.Length / 1024.0:F1} KB"
                        : $"{fi.Length / (1024.0 * 1024.0):F1} MB";

                    // Generate thumbnail
                    byte[] pngBytes = await WindowsPdfRenderer.RenderPageToPngBytesAsync(p, 0, 100, 130);
                    using var ms = new MemoryStream(pngBytes);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();

                    var item = new PrintItem
                    {
                        FilePath = p,
                        PageCount = info.PageCount,
                        FileSizeFormatted = sizeStr,
                        DetectedOrientation = info.IsPortrait ? "portrait" : "landscape",
                        Thumbnail = bmp
                    };
                    item.PropertyChanged += (s, e) => { if (_isInitialized) UpdateSummary(); };

                    Queue.Add(item);
                }
                catch { }
            }
            UpdateSummary();
        }

        private void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            Queue.Clear();
            UpdateSummary();
        }

        private void BtnRemoveItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                var item = Queue.FirstOrDefault(q => q.Id == id);
                if (item != null) Queue.Remove(item);
                UpdateSummary();
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshPrintersAsync();
            if (QueueView.Visibility == Visibility.Visible)
            {
                await LoadPrintJobsAsync();
            }
        }

        private void BtnAbout_Click(object sender, RoutedEventArgs e)
        {
            var about = new AboutWindow { Owner = this };
            about.ShowDialog();
        }

        // ==========================================
        // TOP NAVIGATION TABS
        // ==========================================
        private void TabWorkspace_Click(object sender, RoutedEventArgs e)
        {
            WorkspaceView.Visibility = Visibility.Visible;
            QueueView.Visibility = Visibility.Collapsed;

            TabWorkspaceBtn.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));
            TabWorkspaceBtn.Foreground = Brushes.White;

            TabQueueBtn.Background = Brushes.Transparent;
            TabQueueBtn.Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105));
        }

        private async void TabQueue_Click(object sender, RoutedEventArgs e)
        {
            WorkspaceView.Visibility = Visibility.Collapsed;
            QueueView.Visibility = Visibility.Visible;

            TabQueueBtn.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));
            TabQueueBtn.Foreground = Brushes.White;

            TabWorkspaceBtn.Background = Brushes.Transparent;
            TabWorkspaceBtn.Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105));

            await LoadPrintJobsAsync();
        }

        // ==========================================
        // LIVE PRINT QUEUE ACTIONS
        // ==========================================
        private async Task LoadPrintJobsAsync(bool isBackgroundPoll = false)
        {
            if (_selectedPrinter == null)
            {
                if (!isBackgroundPoll)
                {
                    QueuePrinterTitle.Text = "কোনো প্রিন্টার নির্বাচিত নেই";
                    PrintJobsGrid.ItemsSource = null;
                    QueueCountSummaryText.Text = "মোট ০টি জব লাইনে আছে";
                }
                return;
            }

            QueuePrinterTitle.Text = $"{_selectedPrinter.Name} ({_selectedPrinter.StatusLabel})";

            try
            {
                var jobs = await Task.Run(() => PrinterService.GetPrintJobs(_selectedPrinter.Name));
                PrintJobsGrid.ItemsSource = jobs;
                QueueCountSummaryText.Text = $"মোট {jobs.Count}টি জব লাইনে আছে";
            }
            catch (Exception ex)
            {
                if (!isBackgroundPoll)
                {
                    QueueCountSummaryText.Text = $"কিউ লোড করা যায়নি: {ex.Message}";
                }
            }
        }

        private async void BtnRefreshQueueTab_Click(object sender, RoutedEventArgs e)
        {
            await LoadPrintJobsAsync();
        }

        private void BtnOpenWindowsQueue_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPrinter == null) return;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "rundll32.exe",
                    Arguments = $"printui.dll,PrintUIEntry /o /n \"{_selectedPrinter.Name}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"উইন্ডোজ কিউ উইন্ডো খুলতে সমস্যা হয়েছে:\n{ex.Message}", "ত্রুটি", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void BtnPauseJob_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int jobId && _selectedPrinter != null)
            {
                bool ok = await Task.Run(() => PrinterService.PauseJob(_selectedPrinter.Name, jobId));
                if (!ok)
                {
                    // If pausing didn't work, maybe it was already paused, try resume
                    await Task.Run(() => PrinterService.ResumeJob(_selectedPrinter.Name, jobId));
                }
                await LoadPrintJobsAsync();
            }
        }

        private async void BtnCancelJob_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int jobId && _selectedPrinter != null)
            {
                var res = MessageBox.Show($"আপনি কি নিশ্চিত যে জব #{jobId} বাতিল করতে চান?", "প্রিন্ট জব বাতিল", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                {
                    await Task.Run(() => PrinterService.CancelJob(_selectedPrinter.Name, jobId));
                    await Task.Delay(400); // Give spooler time to update state
                    await LoadPrintJobsAsync();
                }
            }
        }

        // ==========================================
        // COMPOSITE SHEET PREVIEW
        // ==========================================
        private void BtnPreview_Click(object sender, RoutedEventArgs e)
        {
            if (Queue.Count == 0)
            {
                MessageBox.Show("প্রিভিউ দেখতে প্রথমে অন্তত একটি পিডিএফ ফাইল যোগ করুন।", "Print Studio", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Pick currently selected item in listbox or first item in queue
            var targetItem = FileList.SelectedItem as PrintItem ?? Queue[0];

            var opts = new PrintEngineOptions
            {
                PrinterName = _selectedPrinter?.Name ?? "",
                ColorMode = (ColorCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "bw",
                PaperSize = (PaperCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "A4",
                Duplex = (DuplexCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "simplex",
                Nup = _selectedNup,
                DrawBorders = ChkDrawBorders?.IsChecked ?? true,
                Orientation = (OrientCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto",
                Copies = (CopiesText != null && int.TryParse(CopiesText.Text, out int cp)) ? cp : 1,
                Dpi = (QualityCombo?.SelectedItem as QualityOption)?.Dpi ?? 600
            };

            var previewWin = new PreviewWindow(targetItem, opts)
            {
                Owner = this
            };
            previewWin.ShowDialog();
        }

        // ==========================================
        // BATCH PRINT EXECUTION
        // ==========================================
        private async void BtnPrintAll_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPrinter == null)
            {
                MessageBox.Show("দয়া করে একটি প্রিন্টার সিলেক্ট করুন।", "Print Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (Queue.Count == 0) return;

            if (_selectedPrinter.IsOffline)
            {
                var res = MessageBox.Show("নির্বাচিত প্রিন্টারটি বর্তমানে অফলাইনে আছে! প্রিন্ট জব উইন্ডোজ স্পুলার কিউতে জমা থাকবে। আপনি কি প্রিন্ট পাঠাতে চান?", "সতর্কতা", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res != MessageBoxResult.Yes) return;
            }

            BtnPrintAll.IsEnabled = false;
            var opts = new PrintEngineOptions
            {
                PrinterName = _selectedPrinter.Name,
                ColorMode = (ColorCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "bw",
                PaperSize = (PaperCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "A4",
                Duplex = (DuplexCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "simplex",
                Nup = _selectedNup,
                DrawBorders = ChkDrawBorders?.IsChecked ?? true,
                Orientation = (OrientCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto",
                Copies = int.TryParse(CopiesText.Text, out int cp) ? cp : 1,
                Dpi = (QualityCombo.SelectedItem as QualityOption)?.Dpi ?? 600
            };

            using var cts = new CancellationTokenSource();
            try
            {
                await NativePrintEngine.PrintBatchAsync(
                    Queue.ToList(),
                    opts,
                    (done, total, name) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            Title = $"Print Studio — প্রিন্ট হচ্ছে ({done}/{total}): {name}";
                        });
                    },
                    cts.Token
                );

                MessageBox.Show("সমস্ত প্রিন্ট জব সফলভাবে প্রিন্ট স্পুলারে পাঠানো হয়েছে!", "সফল", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"প্রিন্ট করার সময় সমস্যা হয়েছে:\n{ex.Message}", "ত্রুটি", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Title = "Print Studio — স্মার্ট ব্যাচ প্রিন্টিং";
                BtnPrintAll.IsEnabled = true;
            }
        }
    }
}
