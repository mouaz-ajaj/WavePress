namespace WavePress.Models
{
    /// <summary>
    /// بيانات التقدم المُبلَّغة أثناء عملية الضغط أو فك الضغط.
    /// Progress data reported during compression/decompression operations.
    /// </summary>
    public class CompressionProgress
    {
        /// <summary>نسبة التقدم (0 - 100)</summary>
        public double PercentComplete { get; set; }

        /// <summary>نسبة الضغط الحالية (Original / Compressed)</summary>
        public double CompressionRatio { get; set; }

        /// <summary>سرعة المعالجة بالميغابايت/ثانية</summary>
        public double ProcessingSpeedMBps { get; set; }

        /// <summary>المرحلة الحالية (مثل "Compressing...", "Quantizing...")</summary>
        public string CurrentPhase { get; set; } = string.Empty;
    }
}
