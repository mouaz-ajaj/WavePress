using System.Diagnostics;
using System.IO;
using WavePress.Helpers;
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
    /// تخزين البتات يتم بدقة حقيقية باستخدام BitWriter:
    ///   targetBits=4  → كل عينة بـ 4 بتات حقيقية → نسبة 4:1
    ///   targetBits=8  → كل عينة بـ 8 بتات         → نسبة 2:1
    ///   targetBits=12 → كل عينة بـ 12 بتاً         → نسبة 1.33:1
    ///   targetBits=16 → كل عينة بـ 16 بتاً         → نسبة 1:1
    /// 
    /// ── هيكل الملف المضغوط ──
    /// [4 bytes: عدد العينات] [1 byte: targetBits] [بيانات مضغوطة بـ BitWriter]
    /// 
    /// ── How it works ──
    /// Applies µ-law companding (µ=255) then quantizes each sample to exactly
    /// targetBitsPerSample bits using BitWriter for true bit-level packing.
    /// Formula: F(x) = sgn(x) * ln(1 + µ|x|) / ln(1 + µ)
    /// </summary>
    public class NonlinearQuantizationAlgorithm : IAudioCompressionAlgorithm
    {
        public string Name        => "Nonlinear Quantization (µ-law)";
        public string Description => "Compresses dynamic range using µ-law companding with true bit-level packing.";

        private const double Mu = 255.0; // معامل µ القياسي

        public Task<CompressionResult> CompressAsync(
            AudioSampleData input,
            CompressionSettings settings,
            IProgress<CompressionProgress> progress,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var stopwatch   = Stopwatch.StartNew();
                int totalSamples = input.Samples.Length;
                int targetBits  = settings.TargetBitsPerSample;

                // عدد المستويات مشتق مباشرة من عدد البتات (لا حاجة لـ QuantizationLevels)
                // quantLevels is always 2^targetBits — derived automatically from bits, not from
                // the QuantizationLevels UI field which is now read-only/ignored for this algorithm.
                int quantLevels = 1 << targetBits; // 2^targetBits

                // ── هيكل البيانات المضغوطة ──
                // Header: [4 bytes: totalSamples] [1 byte: targetBits]
                // Body:   BitWriter stream (targetBits per sample)
                using var headerStream = new MemoryStream();
                using var headerWriter = new BinaryWriter(headerStream);
                headerWriter.Write(totalSamples); // 4 bytes
                headerWriter.Write((byte)targetBits); // 1 byte
                byte[] header = headerStream.ToArray();

                // ── تشفير العينات بـ BitWriter ──
                var bitWriter = new BitWriter();

                for (int i = 0; i < totalSamples; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // تطبيع العينة إلى [-1, 1]
                    double normalized = input.Samples[i] / 32768.0;
                    normalized = Math.Clamp(normalized, -1.0, 1.0);

                    // تطبيق µ-law companding
                    double sign      = Math.Sign(normalized);
                    double magnitude = Math.Abs(normalized);
                    double companded = sign * Math.Log(1.0 + Mu * magnitude) / Math.Log(1.0 + Mu);

                    // تكميم: [-1,1] → [0, quantLevels-1] (unsigned)
                    int quantized = (int)Math.Round((companded + 1.0) * 0.5 * (quantLevels - 1));
                    quantized = Math.Clamp(quantized, 0, quantLevels - 1);

                    // كتابة القيمة بعدد البتات المطلوب بدقة
                    bitWriter.WriteBits(quantized, targetBits);

                    if (i % 10000 == 0 && i > 0)
                    {
                        double percent   = (double)i / totalSamples * 100.0;
                        double elapsed   = stopwatch.Elapsed.TotalSeconds;
                        double speedMBps = elapsed > 0 ? (i * 2.0 / 1_048_576.0) / elapsed : 0;
                        // النسبة التقريبية: 16 bits أصلية ÷ targetBits
                        double approxRatio = 16.0 / targetBits;

                        progress.Report(new CompressionProgress
                        {
                            PercentComplete    = percent,
                            CompressionRatio   = approxRatio,
                            ProcessingSpeedMBps = speedMBps,
                            CurrentPhase       = "Applying µ-law companding..."
                        });
                    }
                }

                bitWriter.Flush();
                byte[] bitData     = bitWriter.ToArray();
                byte[] compressed  = new byte[header.Length + bitData.Length];
                Buffer.BlockCopy(header,  0, compressed, 0,             header.Length);
                Buffer.BlockCopy(bitData, 0, compressed, header.Length, bitData.Length);

                stopwatch.Stop();

                double finalRatio = (totalSamples * 2.0) / compressed.Length;

                progress.Report(new CompressionProgress
                {
                    PercentComplete    = 100,
                    CompressionRatio   = finalRatio,
                    ProcessingSpeedMBps = 0,
                    CurrentPhase       = "Complete"
                });

                return new CompressionResult
                {
                    CompressedData        = compressed,
                    OriginalSize          = totalSamples * 2L, // 16-bit PCM = 2 bytes/sample
                    CompressedSize        = compressed.Length,
                    AlgorithmName         = Name,
                    TimeElapsed           = stopwatch.Elapsed,
                    OriginalSampleRate    = input.SampleRate,
                    OriginalChannels      = input.Channels,
                    OriginalBitsPerSample = input.BitsPerSample,
                    OriginalSampleCount   = totalSamples,
                    Settings              = settings
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
                byte[] data   = compressedData.CompressedData;

                // ── قراءة الهيدر ──
                int totalSamples = BitConverter.ToInt32(data, 0);   // 4 bytes
                int targetBits   = data[4];                          // 1 byte
                int quantLevels  = 1 << targetBits;
                int headerSize   = 5;

                short[] samples = new short[totalSamples];
                var bitReader   = new BitReader(data, headerSize);

                for (int i = 0; i < totalSamples; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // قراءة القيمة المكممة (unsigned)
                    int quantized    = bitReader.ReadBits(targetBits);

                    // عكس التكميم: [0, quantLevels-1] → [-1, 1]
                    double companded = (quantized / (double)(quantLevels - 1)) * 2.0 - 1.0;

                    // عكس µ-law: x = sgn(y) * (1/µ) * ((1+µ)^|y| - 1)
                    double sign      = Math.Sign(companded);
                    double magnitude = Math.Abs(companded);
                    double expanded  = sign * (1.0 / Mu) * (Math.Pow(1.0 + Mu, magnitude) - 1.0);

                    // إعادة إلى نطاق 16-bit
                    samples[i] = (short)Math.Clamp(expanded * 32768.0, short.MinValue, short.MaxValue);

                    if (i % 10000 == 0 && i > 0)
                    {
                        progress.Report(new CompressionProgress
                        {
                            PercentComplete = (double)i / totalSamples * 100.0,
                            CurrentPhase    = "Expanding µ-law..."
                        });
                    }
                }

                stopwatch.Stop();
                progress.Report(new CompressionProgress { PercentComplete = 100, CurrentPhase = "Complete" });

                return new AudioSampleData
                {
                    Samples       = samples,
                    SampleRate    = compressedData.OriginalSampleRate,
                    Channels      = compressedData.OriginalChannels,
                    BitsPerSample = compressedData.OriginalBitsPerSample
                };
            }, cancellationToken);
        }
    }
}
