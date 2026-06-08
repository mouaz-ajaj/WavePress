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
    /// بين العينة الحالية والعينة السابقة في نفس القناة فقط.
    /// 
    /// الصوت الاستيريو (2 channels) يُعالَج بشكل مستقل لكل قناة:
    ///   فروق القناة اليسرى  [L0, ΔL1, ΔL2, ...] تُحسب بين L-samples فقط.
    ///   فروق القناة اليمنى  [R0, ΔR1, ΔR2, ...] تُحسب بين R-samples فقط.
    /// لا يتم خلط بيانات القناتين، مما يعطي جودة أفضل من حساب الفروق بين
    /// عينات متتالية في الـ interleaved stream.
    /// 
    /// حجم الملف المضغوط يعتمد أساساً على TargetBitsPerSample:
    ///   targetBits=4  → كل فرق بـ 4 بتات حقيقية → نسبة ≈ 4:1
    ///   targetBits=8  → كل فرق بـ 8 بتات         → نسبة ≈ 2:1
    ///   targetBits=12 → كل فرق بـ 12 بتاً         → نسبة ≈ 1.33:1
    ///   targetBits=16 → كل فرق بـ 16 بتاً         → نسبة ≈ 1:1
    /// 
    /// ملاحظة: TargetBitsPerSample يجب أن يكون بين 2 و 16.
    ///   - 1 bit ممنوع لأن maxDiff يصبح 0 مما يسبب خطأ في القسمة.
    ///   - للضغط بمعدل 1 bit/sample، استخدم خوارزمية Delta Modulation.
    /// 
    /// DeltaStepSize يؤثر على جودة الصوت لا حجم الملف:
    ///   خطوة صغيرة → دقة أعلى للإشارات الهادئة لكن احتمال slope overload.
    ///   خطوة كبيرة → تتبع الإشارات السريعة لكن ضوضاء أعلى.
    /// 
    /// ── هيكل الملف المضغوط ──
    /// [4 bytes: totalSamples] [1 byte: diffBits] [1 byte: channels]
    /// ثم لكل قناة: [2 bytes: firstSample] [2 bytes: stepSize]
    /// ثم: بيانات مضغوطة بـ BitWriter (جميع القنوات مخللة / interleaved)
    /// 
    /// Encoding: unsigned offset → (quantizedDiff - minDiff)
    ///   maps signed [-2^(n-1), 2^(n-1)-1] to unsigned [0, 2^n - 1].
    /// </summary>
    public class DpcmAlgorithm : IAudioCompressionAlgorithm
    {
        public string Name        => "DPCM (Differential PCM)";
        public string Description => "Encodes per-channel differences between consecutive samples using true bit-level packing.";

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
                int channels     = Math.Max(1, input.Channels);
                int diffBits     = settings.TargetBitsPerSample;

                // ── Validation ──
                // 1-bit غير مدعوم في DPCM: يسبب maxDiff = 0 → قسمة على صفر.
                // للضغط بـ 1 bit/sample استخدم Delta Modulation.
                if (diffBits < 2 || diffBits > 16)
                    throw new ArgumentOutOfRangeException(nameof(settings),
                        $"DPCM requires TargetBitsPerSample between 2 and 16 (got {diffBits}). " +
                        "For 1-bit encoding, use Delta Modulation instead.");

                // نطاق الفروقات المكممة للـ diffBits المحدد
                int maxDiff = (1 << (diffBits - 1)) - 1;  // e.g. 4 bits → +7
                int minDiff = -(1 << (diffBits - 1));      // e.g. 4 bits → -8

                // ── حساب عدد العينات per-channel ──
                // المصفوفة Interleaved: [L0, R0, L1, R1, ...]
                int samplesPerChannel = totalSamples / channels;

                // ── حساب stepSize لكل قناة بشكل مستقل ──
                int[] stepSizes = new int[channels];
                for (int ch = 0; ch < channels; ch++)
                {
                    int maxActualDiff = 1;
                    int scanLimit     = Math.Min(samplesPerChannel, 50_000);

                    for (int i = 1; i < scanLimit; i++)
                    {
                        // موضع العينة في الـ interleaved array
                        int idx  = i * channels + ch;
                        int prev = (i - 1) * channels + ch;
                        if (idx < totalSamples && prev < totalSamples)
                        {
                            int d = Math.Abs(input.Samples[idx] - input.Samples[prev]);
                            if (d > maxActualDiff) maxActualDiff = d;
                        }
                    }
                    // نضيف 1 لضمان أن maxActualDiff / stepSize ≤ maxDiff
                    stepSizes[ch] = Math.Max(1, (maxActualDiff + maxDiff - 1) / maxDiff);
                }

                // ── بناء الهيدر ──
                // [4: totalSamples] [1: diffBits] [1: channels]
                // ثم لكل قناة: [2: firstSample] [2: stepSize]
                using var headerStream = new MemoryStream();
                using var headerWriter = new BinaryWriter(headerStream);
                headerWriter.Write(totalSamples);       // 4 bytes
                headerWriter.Write((byte)diffBits);     // 1 byte
                headerWriter.Write((byte)channels);     // 1 byte

                for (int ch = 0; ch < channels; ch++)
                {
                    short firstSample = (totalSamples > ch) ? input.Samples[ch] : (short)0;
                    headerWriter.Write(firstSample);            // 2 bytes (Int16)
                    headerWriter.Write((short)stepSizes[ch]);   // 2 bytes
                }
                byte[] header = headerStream.ToArray(); // 6 + 4*channels bytes

                // ── تشفير الفروقات بـ BitWriter (per-channel) ──
                var bitWriter  = new BitWriter();
                short[] predicted = new short[channels];
                for (int ch = 0; ch < channels; ch++)
                    predicted[ch] = (totalSamples > ch) ? input.Samples[ch] : (short)0;

                // نمر على العينات بترتيب interleaved لكن نتتبع predicted بشكل مستقل لكل قناة
                for (int i = channels; i < totalSamples; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int ch        = i % channels;
                    int stepSize  = stepSizes[ch];

                    int actualDiff    = input.Samples[i] - predicted[ch];
                    int quantizedDiff = (int)Math.Round((double)actualDiff / stepSize);
                    quantizedDiff     = Math.Clamp(quantizedDiff, minDiff, maxDiff);

                    // تحديث predicted بالفرق المكمم لضمان تطابق encoder/decoder
                    predicted[ch] = (short)Math.Clamp(
                        predicted[ch] + quantizedDiff * stepSize,
                        short.MinValue, short.MaxValue);

                    // تحويل signed → unsigned: نضيف 2^(n-1) لجعل القيمة غير سالبة
                    int encoded = quantizedDiff - minDiff; // e.g. 4 bits: [-8..+7] → [0..15]
                    bitWriter.WriteBits(encoded, diffBits);

                    if (i % 10000 == 0 && i > 0)
                    {
                        double percent     = (double)i / totalSamples * 100.0;
                        double elapsed     = stopwatch.Elapsed.TotalSeconds;
                        double speedMBps   = elapsed > 0 ? (i * 2.0 / 1_048_576.0) / elapsed : 0;
                        // النسبة تعتمد على عدد البتات: 16 ÷ diffBits
                        double approxRatio = 16.0 / diffBits;

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
                    OriginalChannels      = channels,
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
                // [4: totalSamples] [1: diffBits] [1: channels]
                // ثم لكل قناة: [2: firstSample] [2: stepSize]
                int   totalSamples = BitConverter.ToInt32(data, 0);
                int   diffBits     = data[4];
                int   channels     = data[5];
                int   minDiff      = -(1 << (diffBits - 1));
                int   headerBase   = 6; // موضع بداية بيانات القنوات

                short[] firstSamples = new short[channels];
                short[] stepSizes    = new short[channels];
                for (int ch = 0; ch < channels; ch++)
                {
                    firstSamples[ch] = BitConverter.ToInt16(data, headerBase + ch * 4);
                    stepSizes[ch]    = BitConverter.ToInt16(data, headerBase + ch * 4 + 2);
                }

                int headerSize   = headerBase + channels * 4; // الحجم الكلي للهيدر
                short[] samples  = new short[totalSamples];
                short[] predicted = new short[channels];

                // تهيئة العينات الأولى لكل قناة
                for (int ch = 0; ch < channels; ch++)
                {
                    samples[ch]   = firstSamples[ch];
                    predicted[ch] = firstSamples[ch];
                }

                var bitReader = new BitReader(data, headerSize);

                // نعيد بناء العينات بنفس ترتيب الـ interleaved
                for (int i = channels; i < totalSamples; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int ch       = i % channels;
                    int stepSize = stepSizes[ch];

                    int encoded       = bitReader.ReadBits(diffBits);
                    int quantizedDiff = encoded + minDiff; // عكس الإزاحة

                    predicted[ch] = (short)Math.Clamp(
                        predicted[ch] + quantizedDiff * stepSize,
                        short.MinValue, short.MaxValue);
                    samples[i] = predicted[ch];

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
                    Channels      = channels,
                    BitsPerSample = compressedData.OriginalBitsPerSample
                };
            }, cancellationToken);
        }
    }
}
