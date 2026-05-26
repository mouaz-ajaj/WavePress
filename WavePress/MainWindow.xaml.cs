using System.Windows;
using System.Windows.Controls;
using WavePress.ViewModels;

namespace WavePress
{
    /// <summary>
    /// الكود الخلفي للنافذة الرئيسية — يتعامل فقط مع أحداث لا يمكن تنفيذها في ViewModel.
    /// Code-behind for MainWindow — handles only Drag & Drop events and sidebar scroll navigation.
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// التعامل مع سحب الملف فوق النافذة — يقبل فقط الملفات.
        /// Accepts only file drops.
        /// </summary>
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

        /// <summary>
        /// التعامل مع إسقاط الملف على النافذة.
        /// Handles file drop on the window.
        /// </summary>
        private void Window_Drop(object sender, DragEventArgs e)
        {
            HandleFileDrop(e);
        }

        /// <summary>
        /// التعامل مع إسقاط الملف على منطقة الإسقاط المخصصة.
        /// </summary>
        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            HandleFileDrop(e);
        }

        /// <summary>
        /// يستخرج مسار الملف من بيانات السحب ويمرره إلى ViewModel.
        /// </summary>
        private void HandleFileDrop(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;

            string filePath = files[0]; // نأخذ أول ملف فقط
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

        /// <summary>
        /// التمرير إلى قسم معين عند الضغط على زر في Sidebar.
        /// Scrolls to a named section when a sidebar button is clicked.
        /// </summary>
        private void ScrollToSection(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string sectionName)
            {
                // البحث عن العنصر المسمى
                var element = this.FindName(sectionName) as FrameworkElement;
                if (element != null)
                {
                    element.BringIntoView();
                }
            }
        }

        /// <summary>
        /// تنظيف الموارد عند إغلاق النافذة.
        /// </summary>
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
