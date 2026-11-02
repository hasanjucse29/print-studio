using System;
using System.Threading.Tasks;
using System.Windows;
using PrintStudio.Models;
using PrintStudio.Services;

namespace PrintStudio
{
    public partial class PreviewWindow : Window
    {
        private readonly PrintItem _item;
        private readonly PrintEngineOptions _options;
        private int _currentSheetIndex = 0;
        private int _totalSheets = 1;

        public PreviewWindow(PrintItem item, PrintEngineOptions options)
        {
            InitializeComponent();
            _item = item;
            _options = options;

            DocTitleText.Text = item.FileName;
            string color = (item.UseCustomSettings && item.CustomColor != "default") ? item.CustomColor : options.ColorMode;
            SpecsText.Text = $"N-Up: {options.Nup} • {options.PaperSize} • {(color == "color" ? "কালার" : "B&W")}";

            var pages = NativePrintEngine.ParsePageRange(item.PageRange, item.PageCount);
            _totalSheets = Math.Max(1, (int)Math.Ceiling((double)pages.Count / options.Nup));

            Loaded += async (s, e) => await LoadCurrentSheetAsync();
        }

        private async Task LoadCurrentSheetAsync()
        {
            SheetCounterText.Text = $"শিট {_currentSheetIndex + 1} / {_totalSheets}";
            BtnPrevSheet.IsEnabled = _currentSheetIndex > 0;
            BtnNextSheet.IsEnabled = _currentSheetIndex < _totalSheets - 1;

            try
            {
                var bmp = await NativePrintEngine.RenderPreviewSheetAsync(_item, _currentSheetIndex, _options);
                PreviewImage.Source = bmp;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"প্রিভিউ তৈরি করতে সমস্যা হয়েছে:\n{ex.Message}", "প্রিভিউ এরর", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnPrevSheet_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSheetIndex > 0)
            {
                _currentSheetIndex--;
                await LoadCurrentSheetAsync();
            }
        }

        private async void BtnNextSheet_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSheetIndex < _totalSheets - 1)
            {
                _currentSheetIndex++;
                await LoadCurrentSheetAsync();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
