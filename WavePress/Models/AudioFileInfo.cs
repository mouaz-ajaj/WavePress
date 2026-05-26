namespace WavePress.Models
{
    /// <summary>
    /// يحتوي على معلومات الملف الصوتي المُحمَّل (metadata).
    /// Stores metadata about a loaded audio file.
    /// </summary>
    public class AudioFileInfo
    {
        /// <summary>اسم الملف بدون المسار</summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>المسار الكامل للملف</summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>حجم الملف بالبايت</summary>
        public long FileSize { get; set; }

        /// <summary>مدة الملف الصوتي</summary>
        public TimeSpan Duration { get; set; }

        /// <summary>معدل العينات بالهرتز (مثل 44100)</summary>
        public int SampleRate { get; set; }

        /// <summary>عدد القنوات (1 = Mono, 2 = Stereo)</summary>
        public int Channels { get; set; }

        /// <summary>عدد البتات لكل عينة (مثل 16)</summary>
        public int BitsPerSample { get; set; }

        /// <summary>معدل البت بالكيلوبت/ثانية</summary>
        public int BitRate { get; set; }

        /// <summary>نوع الترميز (مثل PCM, MP3)</summary>
        public string Codec { get; set; } = "PCM";
    }
}
