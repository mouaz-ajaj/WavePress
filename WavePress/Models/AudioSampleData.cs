namespace WavePress.Models
{
    /// <summary>
    /// يحتوي على بيانات العينات الخام (PCM samples) المقروءة من ملف صوتي.
    /// Holds raw PCM sample data extracted from an audio file.
    /// </summary>
    public class AudioSampleData
    {
        /// <summary>مصفوفة العينات الصوتية (16-bit PCM)</summary>
        public short[] Samples { get; set; } = Array.Empty<short>();

        /// <summary>معدل العينات بالهرتز</summary>
        public int SampleRate { get; set; }

        /// <summary>عدد القنوات</summary>
        public int Channels { get; set; }

        /// <summary>عدد البتات لكل عينة</summary>
        public int BitsPerSample { get; set; } = 16;
    }
}
