using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace PrintStudio
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Email_MouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("mailto:jucse29.399@gmail.com") { UseShellExecute = true });
            }
            catch { }
        }

        private void GitHub_MouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://github.com/hasanjucse29/print-studio") { UseShellExecute = true });
            }
            catch { }
        }
    }
}
