using System.Diagnostics;
using WavePress.Models;

namespace WavePress.Algorithms
{
    /// <summary>
    /// خوارزمية التكميم غير الخطي (Nonlinear Quantization) باستخدام قانون µ (mu-law).
    /// 
    /// ── كيف تعمل ──
    /// بدلاً من توزيع مستويات التكميم بشكل منتظم (linear)،
    /// تستخدم دالة لوغاريتمية (µ-law) لضغط النطاق الديناميكي:
    /// - القيم الصغيرة (الإشارات الهادئة) تحصل على دقة أعلى
    /// - القيم الكبيرة (الإشارات العالية) تُضغط
    /// 
    /// هذا يحافظ على جودة الصوت الهادئ مع تقليل عدد البتات المطلوبة.
    /// 
    /// المعادلة: F(x) = sgn(x) * ln(1 + µ|x|) / ln(1 + µ)
    /// حيث µ = 255 عادةً في أنظمة الهاتف الأمريكية.
    /// 
    /// ── How it works ──
    /// Uses µ-law companding to compress the dynamic range before quantization.
    /// Small signal values get finer resolution; large values are compressed.
    /// Formula: F(x) = sgn(x) * ln(1 + µ|x|) / ln(1 + µ), where µ = 255.
    /// </summary>
    public class NonlinearQuantizationAlgorithm : IAudioCompressionAlgorithm
    {
        public string Name => "Nonlinear Quantization (µ-law)";
        public string Description => "Compresses dynamic range using µ-law companding, then reduces bits per sample.";

        private const double Mu = 255.0; // معامل µ القياسي

        public Task<CompressionResult> CompressAsync(
            AudioSampleData input,
            CompressionSettings settings,
            IProgress<CompressionProgress> progress,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var stopwatch = Stopwatch.StartNew();
                int totalSamples = input.Samples.Length;
                int targetBits = settings.TargetBitsPerSample;
                int quantLevels = (int)Math.Pow(2, targetBits); // عدد المستويات

                // ── الخطوة 1: تطبيق µ-law companding + التكميم ──
                // Step 1: Apply µ-law companding and quantize to fewer bits

                byte[] compressed = new byte[totalSamples * ((targetBits <= 8) ? 1 : 2)];
                int byteIndex = 0;

                for (int i = 0; i < totalSamples; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // تطبيع العينة إلى [-1, 1]
                    double normalized = input.Samples[i] / 32768.0;

                    // تطبيق µ-law compression
                    double sign = Math.Sign(normalized);
                    double magnitude = Math.Abs(normalized);
                    double compressed_val = sign * Math.Log(1.0 + Mu * magnitude) / Math.Log(1.0 + Mu);

                    // تكميم إلى عدد المستويات المطلوب
                    // Quantize to target levels: map [-1,1] → [0, quantLevels-1]
                    int quantized = (int)Math.Round((compressed_val + 1.0) / 2.0 * (quantLevels - 1));
                    quantized = Math.Clamp(quantized, 0, quantLevels - 1);

                    // تخزين القيمة المكممة
                    if (targetBits <= 8)
                    {
                        compressed[byteIndex++] = (byte)quantized;
                    }
                    else
                    {
                        // تخزين كـ 2 bytes (little-endian)
                        compressed[byteIndex++] = (byte)(quantized & 0xFF);
                        compressed[byteIndex++] = (byte)((quantized >> 8) & 0xFF);
                    }

                    // الإبلاغ عن التقدم كل 10000 عينة
                    if (i % 10000 == 0)
                    {
                        double percent = (double)i / totalSamples * 100.0;
                        double elapsed = stopwatch.Elapsed.TotalSeconds;
                        double speedMBps = elapsed > 0 ? (i * 2.0 / 1024.0 / 1024.0) / elapsed : 0;

                        progress.Report(new CompressionProgress
                        {
                            PercentComplete = percent,
                            CompressionRatio = (totalSamples * 2.0) / Math.Max(byteIndex, 1),
                            ProcessingSpeedMBps = speedMBps,
                            CurrentPhase = "Applying µ-law companding..."
                        });
                    }
                }

                stopwatch.Stop();
                Array.Resize(ref compressed, byteIndex);

                progress.Report(new CompressionProgress
                {
                    PercentComplete = 100,
                    CompressionRatio = (totalSamples * 2.0) / compressed.Length,
                    ProcessingSpeedMBps = 0,
                    CurrentPhase = "Complete"
                });

                return new CompressionResult
                {
                    CompressedData = compressed,
                    OriginalSize = totalSamples * 2, // 16-bit = 2 bytes per sample
                    CompressedSize = compressed.Length,
                    AlgorithmName = Name,
                    TimeElapsed = stopwatch.Elapsed,
                    OriginalSampleRate = input.SampleRate,
                    OriginalChannels = input.Channels,
                    OriginalBitsPerSample = input.BitsPerSample,
                    OriginalSampleCount = totalSamples,
                    Settings = settings
                };
            }, cancellationToken);
        }

        public Task<AudioSampleData> DecompressAsync(
            CompressionResult compressedData,
            CompressionSettings settings,
            IProgress<CompressionProgress> progress,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var stopwatch = Stopwatch.StartNew();
                int targetBits = settings.TargetBitsPerSample;
                int quantLevels = (int)Math.Pow(2, targetBits);
                byte[] data = compressedData.CompressedData;
                int totalSamples = compressedData.OriginalSampleCount;

                short[] samples = new short[totalSamples];
                int byteIndex = 0;

                for (int i = 0; i < totalSamples && byteIndex < data.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // قراءة القيمة المكممة
                    int quantized;
                    if (targetBits <= 8)
                    {
                        quantized = data[byteIndex++];
                    }
                    else
                    {
                        quantized = data[byteIndex] | (data[byteIndex + 1] << 8);
                        byteIndex += 2;
                    }

                    // عكس التكميم: [0, quantLevels-1] → [-1, 1]
                    double compressed_val = (quantized / (double)(quantLevels - 1)) * 2.0 - 1.0;

                    // عكس µ-law: استعادة القيمة الأصلية
                    // Inverse µ-law: x = sgn(y) * (1/µ) * ((1+µ)^|y| - 1)
                    double sign = Math.Sign(compressed_val);
                    double magnitude = Math.Abs(compressed_val);
                    double expanded = sign * (1.0 / Mu) * (Math.Pow(1.0 + Mu, magnitude) - 1.0);

                    // إعادة إلى نطاق 16-bit
                    samples[i] = (short)Math.Clamp(expanded * 32768.0, short.MinValue, short.MaxValue);

                    if (i % 10000 == 0)
                    {
                        progress.Report(new CompressionProgress
                        {
                            PercentComplete = (double)i / totalSamples * 100.0,
                            CurrentPhase = "Expanding µ-law..."
                        });
                    }
                }

                stopwatch.Stop();
                progress.Report(new CompressionProgress { PercentComplete = 100, CurrentPhase = "Complete" });

                return new AudioSampleData
                {
                    Samples = samples,
                    SampleRate = compressedData.OriginalSampleRate,
                    Channels = compressedData.OriginalChannels,
                    BitsPerSample = compressedData.OriginalBitsPerSample
                };
            }, cancellationToken);
        }
    }
}
