using System.Diagnostics;
using WavePress.Models;

namespace WavePress.Algorithms
{
    /// <summary>
    /// خوارزمية تعديل الدلتا التكيفي (Adaptive Delta Modulation - ADM).
    /// 
    /// ── كيف تعمل ──
    /// تطوير لخوارزمية Delta Modulation العادية حيث يتغير حجم الخطوة (δ)
    /// ديناميكياً بناءً على سلوك الإشارة:
    /// 
    /// - إذا تكرر نفس الاتجاه (صعود متتالي أو نزول متتالي):
    ///   → يزداد حجم الخطوة بضربه في AdaptiveFactor (مثل 1.5×)
    ///   → هذا يحل مشكلة Slope Overload
    /// 
    /// - إذا تغير الاتجاه (صعود ثم نزول أو العكس):
    ///   → يقل حجم الخطوة بقسمته على AdaptiveFactor
    ///   → هذا يحل مشكلة Granular Noise
    /// 
    /// النتيجة: جودة أفضل من DM العادي مع نفس معدل الضغط (1 bit/sample).
    /// 
    /// ── How it works ──
    /// Like Delta Modulation but with adaptive step size:
    /// - Same direction → multiply step by factor (handles steep slopes)
    /// - Direction change → divide step by factor (reduces granular noise)
    /// </summary>
    public class AdaptiveDeltaModulationAlgorithm : IAudioCompressionAlgorithm
    {
        public string Name => "Adaptive Delta Modulation";
        public string Description => "1-bit encoding with adaptive step size that grows/shrinks based on signal behavior.";

        private const double MinStep = 16.0;   // حد أدنى لحجم الخطوة
        private const double MaxStep = 16384.0; // حد أقصى لحجم الخطوة

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
                double adaptiveFactor = settings.AdaptiveFactor;
                double stepSize = settings.DeltaStepSize;

                // ── الهيكل المضغوط ──
                // [العينة الأولى: 2 bytes] [حجم الخطوة الأولي: 8 bytes (double)]
                // [العامل التكيفي: 8 bytes (double)] + [بتات مضغوطة]

                int bitDataLength = (totalSamples - 1 + 7) / 8;
                byte[] headerAndBits = new byte[2 + 8 + 8 + bitDataLength];

                // تخزين Header
                headerAndBits[0] = (byte)(input.Samples[0] & 0xFF);
                headerAndBits[1] = (byte)((input.Samples[0] >> 8) & 0xFF);
                Buffer.BlockCopy(BitConverter.GetBytes(stepSize), 0, headerAndBits, 2, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(adaptiveFactor), 0, headerAndBits, 10, 8);

                int headerSize = 18; // 2 + 8 + 8
                double predicted = input.Samples[0];
                bool lastBit = false; // اتجاه الخطوة السابقة
                int bitIndex = 0;

                for (int i = 1; i < totalSamples; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // تحديد الاتجاه
                    bool currentBit = input.Samples[i] >= predicted;

                    // تخزين البت
                    if (currentBit)
                    {
                        int bytePos = headerSize + (bitIndex / 8);
                        int bitPos = bitIndex % 8;
                        headerAndBits[bytePos] |= (byte)(1 << bitPos);
                        predicted += stepSize;
                    }
                    else
                    {
                        predicted -= stepSize;
                    }

                    // ── تكيف حجم الخطوة ──
                    // Adapt step size based on direction consistency
                    if (i > 1) // نبدأ التكيف من العينة الثالثة
                    {
                        if (currentBit == lastBit)
                        {
                            // نفس الاتجاه → زيادة الخطوة (الإشارة تتغير بسرعة)
                            stepSize = Math.Min(stepSize * adaptiveFactor, MaxStep);
                        }
                        else
                        {
                            // تغير الاتجاه → تقليل الخطوة (الإشارة مستقرة)
                            stepSize = Math.Max(stepSize / adaptiveFactor, MinStep);
                        }
                    }

                    predicted = Math.Clamp(predicted, short.MinValue, short.MaxValue);
                    lastBit = currentBit;
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
                            CompressionRatio = (totalSamples * 2.0) / Math.Max(headerAndBits.Length, 1),
                            ProcessingSpeedMBps = speedMBps,
                            CurrentPhase = "Adaptive delta encoding..."
                        });
                    }
                }

                stopwatch.Stop();

                progress.Report(new CompressionProgress
                {
                    PercentComplete = 100,
                    CompressionRatio = (totalSamples * 2.0) / headerAndBits.Length,
                    CurrentPhase = "Complete"
                });

                return new CompressionResult
                {
                    CompressedData = headerAndBits,
                    OriginalSize = totalSamples * 2,
                    CompressedSize = headerAndBits.Length,
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

                // قراءة Header
                short firstSample = (short)(data[0] | (data[1] << 8));
                double stepSize = BitConverter.ToDouble(data, 2);
                double adaptiveFactor = BitConverter.ToDouble(data, 10);

                int headerSize = 18;
                short[] samples = new short[totalSamples];
                samples[0] = firstSample;
                double predicted = firstSample;
                bool lastBit = false;
                int bitIndex = 0;

                for (int i = 1; i < totalSamples; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // قراءة البت
                    int bytePos = headerSize + (bitIndex / 8);
                    int bitPos = bitIndex % 8;
                    bool currentBit = (data[bytePos] & (1 << bitPos)) != 0;

                    if (currentBit)
                        predicted += stepSize;
                    else
                        predicted -= stepSize;

                    // نفس منطق التكيف كما في الضغط
                    if (i > 1)
                    {
                        if (currentBit == lastBit)
                            stepSize = Math.Min(stepSize * adaptiveFactor, MaxStep);
                        else
                            stepSize = Math.Max(stepSize / adaptiveFactor, MinStep);
                    }

                    predicted = Math.Clamp(predicted, short.MinValue, short.MaxValue);
                    samples[i] = (short)predicted;
                    lastBit = currentBit;
                    bitIndex++;

                    if (i % 10000 == 0)
                    {
                        progress.Report(new CompressionProgress
                        {
                            PercentComplete = (double)i / totalSamples * 100.0,
                            CurrentPhase = "Adaptive reconstruction..."
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
