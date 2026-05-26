using System.Diagnostics;
using System.IO;
using WavePress.Models;

namespace WavePress.Algorithms
{
    /// <summary>
    /// خوارزمية ترميز النبض التفاضلي (Differential Pulse Code Modulation - DPCM).
    /// 
    /// ── كيف تعمل ──
    /// بدلاً من تخزين كل عينة بالكامل (16-bit)، نخزن الفرق (difference)
    /// بين العينة الحالية والعينة السابقة فقط.
    /// 
    /// لأن الإشارات الصوتية عادة تتغير تدريجياً بين العينات المتجاورة،
    /// فإن الفروقات تكون صغيرة ويمكن تمثيلها بعدد أقل من البتات.
    /// 
    /// مثال: إذا كانت العينات [100, 105, 103, 108]
    /// الفروقات تكون: [100, +5, -2, +5] — أصغر بكثير!
    /// 
    /// ── How it works ──
    /// Stores differences between consecutive samples instead of absolute values.
    /// Adjacent audio samples are highly correlated, so differences are small
    /// and can be encoded with fewer bits, achieving compression.
    /// </summary>
    public class DpcmAlgorithm : IAudioCompressionAlgorithm
    {
        public string Name => "DPCM (Differential PCM)";
        public string Description => "Encodes differences between consecutive samples using fewer bits.";

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
                int diffBits = settings.TargetBitsPerSample; // عدد البتات للفرق
                int maxDiff = (1 << (diffBits - 1)) - 1;    // أقصى قيمة فرق موجبة
                int minDiff = -(1 << (diffBits - 1));        // أقل قيمة فرق سالبة
                int levels = 1 << diffBits;                   // عدد المستويات الكلي

                // ── بناء البيانات المضغوطة ──
                // الهيكل: [العينة الأولى كاملة (2 bytes)] + [الفروقات المكممة]

                using var ms = new MemoryStream();
                using var writer = new BinaryWriter(ms);

                // تخزين العينة الأولى كاملة (نقطة البداية)
                writer.Write(input.Samples[0]);

                // حساب عامل التحجيم: نحدد أقصى فرق فعلي لتحديد step size
                // Calculate scale factor based on actual difference range
                int maxActualDiff = 0;
                for (int i = 1; i < Math.Min(totalSamples, 50000); i++)
                {
                    int diff = Math.Abs(input.Samples[i] - input.Samples[i - 1]);
                    if (diff > maxActualDiff) maxActualDiff = diff;
                }
                // عامل التحجيم: يحول الفروقات الكبيرة إلى نطاق البتات المتاح
                int stepSize = Math.Max(1, maxActualDiff / maxDiff);
                writer.Write((short)stepSize);

                short predicted = input.Samples[0];

                for (int i = 1; i < totalSamples; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // حساب الفرق الفعلي
                    int actualDiff = input.Samples[i] - predicted;

                    // تكميم الفرق
                    int quantizedDiff = (int)Math.Round((double)actualDiff / stepSize);
                    quantizedDiff = Math.Clamp(quantizedDiff, minDiff, maxDiff);

                    // تحديث القيمة المتوقعة باستخدام الفرق المكمم (وليس الفعلي)
                    // لضمان تطابق encoder و decoder
                    predicted = (short)Math.Clamp(predicted + quantizedDiff * stepSize, short.MinValue, short.MaxValue);

                    // تخزين الفرق المكمم كـ byte واحد (مع إزاحة لتجنب القيم السالبة)
                    if (diffBits <= 8)
                    {
                        byte encoded = (byte)(quantizedDiff - minDiff); // shift to unsigned
                        writer.Write(encoded);
                    }
                    else
                    {
                        short encoded = (short)(quantizedDiff - minDiff);
                        writer.Write(encoded);
                    }

                    // الإبلاغ عن التقدم
                    if (i % 10000 == 0)
                    {
                        double percent = (double)i / totalSamples * 100.0;
                        double elapsed = stopwatch.Elapsed.TotalSeconds;
                        double speedMBps = elapsed > 0 ? (i * 2.0 / 1024.0 / 1024.0) / elapsed : 0;

                        progress.Report(new CompressionProgress
                        {
                            PercentComplete = percent,
                            CompressionRatio = (totalSamples * 2.0) / Math.Max(ms.Position, 1),
                            ProcessingSpeedMBps = speedMBps,
                            CurrentPhase = "Computing differences..."
                        });
                    }
                }

                stopwatch.Stop();
                byte[] compressed = ms.ToArray();

                progress.Report(new CompressionProgress
                {
                    PercentComplete = 100,
                    CompressionRatio = (totalSamples * 2.0) / compressed.Length,
                    CurrentPhase = "Complete"
                });

                return new CompressionResult
                {
                    CompressedData = compressed,
                    OriginalSize = totalSamples * 2,
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
                int diffBits = settings.TargetBitsPerSample;
                int minDiff = -(1 << (diffBits - 1));
                int totalSamples = compressedData.OriginalSampleCount;

                using var ms = new MemoryStream(compressedData.CompressedData);
                using var reader = new BinaryReader(ms);

                short[] samples = new short[totalSamples];

                // قراءة العينة الأولى وعامل التحجيم
                samples[0] = reader.ReadInt16();
                short stepSize = reader.ReadInt16();

                short predicted = samples[0];

                for (int i = 1; i < totalSamples; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // قراءة الفرق المكمم
                    int quantizedDiff;
                    if (diffBits <= 8)
                    {
                        byte encoded = reader.ReadByte();
                        quantizedDiff = encoded + minDiff; // reverse shift
                    }
                    else
                    {
                        short encoded = reader.ReadInt16();
                        quantizedDiff = encoded + minDiff;
                    }

                    // إعادة بناء العينة
                    predicted = (short)Math.Clamp(predicted + quantizedDiff * stepSize, short.MinValue, short.MaxValue);
                    samples[i] = predicted;

                    if (i % 10000 == 0)
                    {
                        progress.Report(new CompressionProgress
                        {
                            PercentComplete = (double)i / totalSamples * 100.0,
                            CurrentPhase = "Reconstructing samples..."
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
