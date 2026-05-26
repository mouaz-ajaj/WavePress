using System.Windows;
using System.Windows.Controls;
using WavePress.ViewModels;

namespace WavePress
{
    /// <summary>
    /// الكود الخلفي للنافذة الرئيسية — يتعامل فقط مع أحداث لا يمكن تنفيذها في ViewModel.
    /// Code-behind for MainWindow — handles Drag & Drop, sidebar scroll, and custom title bar buttons.
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        // ══════════════════════════════════════
        //  Custom Title Bar Buttons
        // ══════════════════════════════════════

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                MaxRestoreButton.Content = "\uE922"; // Maximize icon
            }
            else
            {
                WindowState = WindowState.Maximized;
                MaxRestoreButton.Content = "\uE923"; // Restore icon
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ══════════════════════════════════════
        //  Drag & Drop
        // ══════════════════════════════════════

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            HandleFileDrop(e);
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            HandleFileDrop(e);
        }

        private void HandleFileDrop(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;

            string filePath = files[0];
            string ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();

            if (ext is ".wav" or ".mp3")
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.LoadFile(filePath);
                }
            }
            else
            {
                MessageBox.Show("Please drop a WAV or MP3 file.", "Unsupported Format",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            e.Handled = true;
        }

        // ══════════════════════════════════════
        //  Sidebar Scroll Navigation
        // ══════════════════════════════════════

        private void ScrollToSection(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string sectionName)
            {
                var element = this.FindName(sectionName) as FrameworkElement;
                if (element != null)
                {
                    element.BringIntoView();
                }
            }
        }

        // ══════════════════════════════════════
        //  Cleanup
        // ══════════════════════════════════════

        protected override void OnClosed(EventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.Dispose();
            }
            base.OnClosed(e);
        }
    }
}
