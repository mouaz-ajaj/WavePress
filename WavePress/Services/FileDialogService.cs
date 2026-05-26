using Microsoft.Win32;

namespace WavePress.Services
{
    /// <summary>
    /// خدمة حوارات الملفات — تفتح حوار اختيار/حفظ الملف.
    /// Wraps Win32 Open/Save file dialogs for MVVM compatibility.
    /// </summary>
    public class FileDialogService
    {
        /// <summary>
        /// يفتح حوار اختيار ملف صوتي.
        /// Opens a file dialog filtered for audio files.
        /// </summary>
        /// <returns>مسار الملف المختار أو null إذا ألغى المستخدم</returns>
        public string? OpenAudioFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Audio File",
                Filter = "Audio Files|*.wav;*.mp3|WAV Files|*.wav|MP3 Files|*.mp3|All Files|*.*",
                FilterIndex = 1
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        /// <summary>
        /// يفتح حوار اختيار ملف WavePress مضغوط.
        /// Opens a file dialog for .wvp compressed files.
        /// </summary>
        public string? OpenCompressedFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select WavePress File",
                Filter = "WavePress Files|*.wvp|All Files|*.*",
                FilterIndex = 1
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        /// <summary>
        /// يفتح حوار حفظ ملف مضغوط.
        /// Opens a save dialog for compressed output.
        /// </summary>
        public string? SaveCompressedFile(string defaultFileName = "output")
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save Compressed File",
                Filter = "WavePress Files|*.wvp",
                FileName = defaultFileName,
                DefaultExt = ".wvp"
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        /// <summary>
        /// يفتح حوار حفظ ملف WAV (لفك الضغط).
        /// Opens a save dialog for decompressed WAV output.
        /// </summary>
        public string? SaveWavFile(string defaultFileName = "decompressed")
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save Decompressed WAV File",
                Filter = "WAV Files|*.wav",
                FileName = defaultFileName,
                DefaultExt = ".wav"
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}
