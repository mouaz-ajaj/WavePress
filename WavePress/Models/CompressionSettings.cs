namespace WavePress.Models
{
    /// <summary>
    /// أنواع خوارزميات الضغط المتاحة.
    /// Available compression algorithm types.
    /// </summary>
    public enum AlgorithmType
    {
        NonlinearQuantization,
        DPCM,
        DeltaModulation,
        AdaptiveDeltaModulation
    }

    /// <summary>
    /// إعدادات الضغط التي يختارها المستخدم قبل تنفيذ العملية.
    /// User-configurable compression settings.
    /// </summary>
    public class CompressionSettings
    {
        /// <summary>نوع الخوارزمية المختارة</summary>
        public AlgorithmType AlgorithmType { get; set; } = AlgorithmType.NonlinearQuantization;

        /// <summary>معدل العينات المستهدف (Hz)</summary>
        public int TargetSampleRate { get; set; } = 22050;

        /// <summary>عدد مستويات التكميم (يُستخدم في Nonlinear Quantization)</summary>
        public int QuantizationLevels { get; set; } = 256;

        /// <summary>عدد البتات لكل عينة في الإخراج</summary>
        public int TargetBitsPerSample { get; set; } = 8;

        /// <summary>حجم خطوة الدلتا (يُستخدم في Delta Modulation)</summary>
        public int DeltaStepSize { get; set; } = 256;

        /// <summary>عامل التكيف (يُستخدم في Adaptive Delta Modulation)</summary>
        public double AdaptiveFactor { get; set; } = 1.5;

        /// <summary>اسم ملف الإخراج</summary>
        public string OutputFileName { get; set; } = "output";
    }
}
