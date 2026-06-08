using System.Diagnostics;
using System.IO;
using WavePress.Helpers;
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
    /// تخزين البتات يتم بدقة حقيقية باستخدام BitWriter:
    ///   targetBits=4  → كل فرق بـ 4 بتات حقيقية → نسبة ~4:1 (تتفاوت حسب الإشارة)
    ///   targetBits=8  → كل فرق بـ 8 بتات         → نسبة ~2:1
    ///   targetBits=16 → كل فرق بـ 16 بتاً         → نسبة ~1:1
    /// 
    /// ── هيكل الملف المضغوط ──
    /// [4 bytes: totalSamples] [1 byte: targetBits] [2 bytes: firstSample] [2 bytes: stepSize]
    /// [بيانات مضغوطة بـ BitWriter — targetBits per diff, unsigned offset encoding]
    /// 
    /// Encoding: quantizedDiff stored as (quantizedDiff - minDiff) where minDiff = -2^(n-1)
    /// This maps the signed range [-2^(n-1), 2^(n-1)-1] to unsigned [0, 2^n - 1].
    /// 
    /// ── How it works ──
    /// Stores quantized differences between consecutive samples using exactly
    /// targetBitsPerSample bits per difference via BitWriter (true bit-level packing).
    /// </summary>
    public class DpcmAlgorithm : IAudioCompressionAlgorithm
    {
        public string Name        => "DPCM (Differential PCM)";
        public string Description => "Encodes quantized differences between samples using true bit-level packing.";

        public Task<CompressionResult> CompressAsync(
            AudioSampleData input,
            CompressionSettings settings,
            IProgress<CompressionProgress> progress,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var stopwatch    = Stopwatch.StartNew();
                int totalSamples = input.Samples.Length;
                int diffBits     = settings.TargetBitsPerSample; // عدد البتات للفرق

                // نطاق الفروقات المكممة (signed two's complement range for diffBits)
                int maxDiff = (1 << (diffBits - 1)) - 1;  // مثلاً: 4 bits → +7
                int minDiff = -(1 << (diffBits - 1));      // مثلاً: 4 bits → -8
                // العدد الكلي للمستويات = 2^diffBits (مثلاً 4 bits → 16 مستوى)

                // ── حساب عامل التحجيم (scale factor) ──
                // نأخذ عينة من الفروقات الفعلية لضبط step size بحيث لا يفيض
                int maxActualDiff = 1;
                int scanLimit = Math.Min(totalSamples, 50_000);
                for (int i = 1; i < scanLimit; i++)
                {
                    int d = Math.Abs(input.Samples[i] - input.Samples[i - 1]);
                    if (d > maxActualDiff) maxActualDiff = d;
                }
                // stepSize: يحول الفروقات الكبيرة إلى النطاق المتاح
                // نضيف 1 لضمان أن maxActualDiff / stepSize ≤ maxDiff
                int stepSize = Math.Max(1, (maxActualDiff + maxDiff - 1) / maxDiff);

                // ── بناء الهيدر ──
                // [4 bytes: totalSamples] [1 byte: diffBits] [2 bytes: firstSample] [2 bytes: stepSize]
                using var headerStream = new MemoryStream();
                using var headerWriter = new BinaryWriter(headerStream);
                headerWriter.Write(totalSamples);          // 4 bytes
                headerWriter.Write((byte)diffBits);        // 1 byte
                headerWriter.Write(input.Samples[0]);      // 2 bytes (Int16)
                headerWriter.Write((short)stepSize);       // 2 bytes
                byte[] header = headerStream.ToArray();    // 9 bytes total

                // ── تشفير الفروقات بـ BitWriter ──
                var bitWriter = new BitWriter();
                short predicted = input.Samples[0];

                for (int i = 1; i < totalSamples; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // حساب الفرق الفعلي وتكميمه
                    int actualDiff    = input.Samples[i] - predicted;
                    int quantizedDiff = (int)Math.Round((double)actualDiff / stepSize);
                    quantizedDiff     = Math.Clamp(quantizedDiff, minDiff, maxDiff);

                    // تحديث predicted بالفرق المكمم (لضمان تطابق encoder/decoder)
                    predicted = (short)Math.Clamp(
                        predicted + quantizedDiff * stepSize,
                        short.MinValue, short.MaxValue);

                    // تحويل signed → unsigned: نضيف (2^(n-1)) لجعل القيمة غير سالبة
                    // مثال (4 bits): range [-8..+7] → [0..15]
                    int encoded = quantizedDiff - minDiff; // يعادل: quantizedDiff + 2^(n-1)

                    // كتابة بعدد البتات المطلوب بدقة
                    bitWriter.WriteBits(encoded, diffBits);

                    if (i % 10000 == 0 && i > 0)
                    {
                        double percent      = (double)i / totalSamples * 100.0;
                        double elapsed      = stopwatch.Elapsed.TotalSeconds;
                        double speedMBps    = elapsed > 0 ? (i * 2.0 / 1_048_576.0) / elapsed : 0;
                        double approxRatio  = 16.0 / diffBits;

                        progress.Report(new CompressionProgress
                        {
                            PercentComplete    = percent,
                            CompressionRatio   = approxRatio,
                            ProcessingSpeedMBps = speedMBps,
                            CurrentPhase       = "Computing differences..."
                        });
                    }
                }

                bitWriter.Flush();
                byte[] bitData    = bitWriter.ToArray();
                byte[] compressed = new byte[header.Length + bitData.Length];
                Buffer.BlockCopy(header,  0, compressed, 0,             header.Length);
                Buffer.BlockCopy(bitData, 0, compressed, header.Length, bitData.Length);

                stopwatch.Stop();

                double finalRatio = (totalSamples * 2.0) / compressed.Length;

                progress.Report(new CompressionProgress
                {
                    PercentComplete = 100,
                    CompressionRatio = finalRatio,
                    CurrentPhase = "Complete"
                });

                return new CompressionResult
                {
                    CompressedData        = compressed,
                    OriginalSize          = totalSamples * 2L,
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
                // [4 bytes: totalSamples] [1 byte: diffBits] [2 bytes: firstSample] [2 bytes: stepSize]
                int  totalSamples  = BitConverter.ToInt32(data, 0);
                int  diffBits      = data[4];
                short firstSample  = BitConverter.ToInt16(data, 5);
                short stepSize     = BitConverter.ToInt16(data, 7);
                int  headerSize    = 9;

                int minDiff = -(1 << (diffBits - 1)); // مثلاً 4 bits → -8

                short[] samples = new short[totalSamples];
                samples[0]      = firstSample;
                short predicted = firstSample;

                var bitReader = new BitReader(data, headerSize);

                for (int i = 1; i < totalSamples; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // قراءة القيمة المكممة (unsigned)
                    int encoded       = bitReader.ReadBits(diffBits);
                    // عكس الإزاحة: unsigned → signed
                    int quantizedDiff = encoded + minDiff;

                    // إعادة بناء العينة
                    predicted = (short)Math.Clamp(
                        predicted + quantizedDiff * stepSize,
                        short.MinValue, short.MaxValue);
                    samples[i] = predicted;

                    if (i % 10000 == 0 && i > 0)
                    {
                        progress.Report(new CompressionProgress
                        {
                            PercentComplete = (double)i / totalSamples * 100.0,
                            CurrentPhase    = "Reconstructing samples..."
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
