namespace WavePress.Models
{
    /// <summary>
    /// نتيجة عملية الضغط — تحتوي على البيانات المضغوطة ومعلومات وصفية لفك الضغط.
    /// Result of a compression operation, containing compressed bytes and metadata needed for decompression.
    /// </summary>
    public class CompressionResult
    {
        /// <summary>البيانات المضغوطة</summary>
        public byte[] CompressedData { get; set; } = Array.Empty<byte>();

        /// <summary>حجم الملف الأصلي بالبايت</summary>
        public long OriginalSize { get; set; }

        /// <summary>حجم البيانات المضغوطة بالبايت</summary>
        public long CompressedSize { get; set; }

        /// <summary>اسم الخوارزمية المستخدمة</summary>
        public string AlgorithmName { get; set; } = string.Empty;

        /// <summary>الزمن المستغرق في الضغط</summary>
        public TimeSpan TimeElapsed { get; set; }

        // ── Metadata المطلوبة لفك الضغط ──

        /// <summary>معدل العينات الأصلي</summary>
        public int OriginalSampleRate { get; set; }

        /// <summary>عدد القنوات الأصلي</summary>
        public int OriginalChannels { get; set; }

        /// <summary>عدد البتات الأصلي</summary>
        public int OriginalBitsPerSample { get; set; }

        /// <summary>عدد العينات الأصلي</summary>
        public int OriginalSampleCount { get; set; }

        /// <summary>الإعدادات المستخدمة أثناء الضغط</summary>
        public CompressionSettings? Settings { get; set; }
    }
}
