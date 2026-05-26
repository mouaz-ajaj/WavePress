using System.Diagnostics;
using WavePress.Models;

namespace WavePress.Algorithms
{
    /// <summary>
    /// خوارزمية تعديل الدلتا (Delta Modulation - DM).
    /// 
    /// ── كيف تعمل ──
    /// حالة خاصة ومبسطة من DPCM حيث يتم تمثيل كل عينة ببت واحد فقط:
    /// - إذا كانت العينة الحالية أكبر من المتوقعة → bit = 1 (صعود بخطوة δ)
    /// - إذا كانت أصغر أو مساوية → bit = 0 (نزول بخطوة δ)
    /// 
    /// حجم الخطوة (δ) ثابت طوال العملية.
    /// 
    /// المزايا: أبسط خوارزمية ممكنة (1 bit/sample).
    /// العيوب: Slope Overload (لا تستطيع تتبع التغيرات السريعة)
    ///         و Granular Noise (تذبذب حول القيم المستقرة).
    /// 
    /// ── How it works ──
    /// Simplest form of differential encoding: 1 bit per sample.
    /// Bit=1 means "step up by δ", bit=0 means "step down by δ".
    /// Fixed step size throughout the entire signal.
    /// </summary>
    public class DeltaModulationAlgorithm : IAudioCompressionAlgorithm
    {
        public string Name => "Delta Modulation";
        public string Description => "1-bit encoding: each sample is represented as step-up or step-down by a fixed delta.";

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
                int stepSize = settings.DeltaStepSize;

                // ── الهيكل المضغوط ──
                // [العينة الأولى: 2 bytes] + [بتات مضغوطة: 1 bit per sample]
                // Header: first sample (2 bytes)
                // Data: packed bits (1 bit per sample, 8 samples per byte)

                int bitDataLength = (totalSamples - 1 + 7) / 8; // عدد البايتات المطلوبة
                byte[] compressed = new byte[2 + bitDataLength]; // 2 bytes header + bits

                // تخزين العينة الأولى
                compressed[0] = (byte)(input.Samples[0] & 0xFF);
                compressed[1] = (byte)((input.Samples[0] >> 8) & 0xFF);

                double predicted = input.Samples[0];
                int bitIndex = 0;

                for (int i = 1; i < totalSamples; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // مقارنة العينة الفعلية مع المتوقعة
                    if (input.Samples[i] >= predicted)
                    {
                        // صعود: bit = 1
                        int bytePos = 2 + (bitIndex / 8);
                        int bitPos = bitIndex % 8;
                        compressed[bytePos] |= (byte)(1 << bitPos);
                        predicted += stepSize;
                    }
                    else
                    {
                        // نزول: bit = 0 (already 0 by default)
                        predicted -= stepSize;
                    }

                    // تحديد حدود القيمة المتوقعة
                    predicted = Math.Clamp(predicted, short.MinValue, short.MaxValue);
                    bitIndex++;

                    // الإبلاغ عن التقدم
                    if (i % 10000 == 0)
                    {
                        double percent = (double)i / totalSamples * 100.0;
                        double elapsed = stopwatch.Elapsed.TotalSeconds;
                        double speedMBps = elapsed > 0 ? (i * 2.0 / 1024.0 / 1024.0) / elapsed : 0;

                        progress.Report(new CompressionProgress
                        {
                            PercentComplete = percent,
                            CompressionRatio = (totalSamples * 2.0) / Math.Max(compressed.Length, 1),
                            ProcessingSpeedMBps = speedMBps,
                            CurrentPhase = "Delta encoding (1-bit)..."
                        });
                    }
                }

                stopwatch.Stop();

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
                byte[] data = compressedData.CompressedData;
                int totalSamples = compressedData.OriginalSampleCount;
                int stepSize = settings.DeltaStepSize;

                short[] samples = new short[totalSamples];

                // قراءة العينة الأولى
                samples[0] = (short)(data[0] | (data[1] << 8));
                double predicted = samples[0];

                int bitIndex = 0;

                for (int i = 1; i < totalSamples; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // قراءة البت
                    int bytePos = 2 + (bitIndex / 8);
                    int bitPos = bitIndex % 8;
                    bool isUp = (data[bytePos] & (1 << bitPos)) != 0;

                    if (isUp)
                        predicted += stepSize;
                    else
                        predicted -= stepSize;

                    predicted = Math.Clamp(predicted, short.MinValue, short.MaxValue);
                    samples[i] = (short)predicted;
                    bitIndex++;

                    if (i % 10000 == 0)
                    {
                        progress.Report(new CompressionProgress
                        {
                            PercentComplete = (double)i / totalSamples * 100.0,
                            CurrentPhase = "Reconstructing from delta bits..."
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
