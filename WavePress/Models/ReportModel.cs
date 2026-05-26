namespace WavePress.Models
{
    /// <summary>
    /// نموذج التقرير النهائي بعد اكتمال الضغط — يعرض ملخصاً شاملاً للمستخدم.
    /// Final report model displayed after compression completes.
    /// </summary>
    public class ReportModel
    {
        /// <summary>حجم الملف الأصلي بالبايت</summary>
        public long OriginalSize { get; set; }

        /// <summary>حجم الملف المضغوط بالبايت</summary>
        public long CompressedSize { get; set; }

        /// <summary>الحجم الموفَّر بالبايت</summary>
        public long SavedSize => OriginalSize - CompressedSize;

        /// <summary>نسبة التوفير المئوية</summary>
        public double SavingPercentage =>
            OriginalSize > 0 ? (double)SavedSize / OriginalSize * 100.0 : 0;

        /// <summary>نسبة الضغط (Original / Compressed)</summary>
        public double CompressionRatio =>
            CompressedSize > 0 ? (double)OriginalSize / CompressedSize : 0;

        /// <summary>الزمن المستغرق</summary>
        public TimeSpan TimeElapsed { get; set; }

        /// <summary>اسم الخوارزمية المستخدمة</summary>
        public string AlgorithmName { get; set; } = string.Empty;

        /// <summary>معدل العينات المستخدم</summary>
        public int SampleRate { get; set; }

        /// <summary>مستويات التكميم</summary>
        public int QuantizationLevels { get; set; }

        /// <summary>عدد البتات لكل عينة</summary>
        public int BitsPerSample { get; set; }

        /// <summary>حجم خطوة الدلتا</summary>
        public int StepSize { get; set; }

        /// <summary>مسار ملف الإخراج</summary>
        public string OutputPath { get; set; } = string.Empty;
    }
}
