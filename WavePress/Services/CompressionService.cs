using System.IO;
using WavePress.Algorithms;
using WavePress.Models;

namespace WavePress.Services
{
    /// <summary>
    /// خدمة الضغط — تنسق عملية الضغط وفك الضغط وحفظ/تحميل الملفات المضغوطة.
    /// Orchestrates compression/decompression and manages .wvp file format.
    /// </summary>
    public class CompressionService
    {
        // قائمة الخوارزميات المتاحة
        private readonly Dictionary<AlgorithmType, IAudioCompressionAlgorithm> _algorithms;

        public CompressionService()
        {
            _algorithms = new Dictionary<AlgorithmType, IAudioCompressionAlgorithm>
            {
                { AlgorithmType.NonlinearQuantization, new NonlinearQuantizationAlgorithm() },
                { AlgorithmType.DPCM, new DpcmAlgorithm() },
                { AlgorithmType.DeltaModulation, new DeltaModulationAlgorithm() },
                { AlgorithmType.AdaptiveDeltaModulation, new AdaptiveDeltaModulationAlgorithm() }
            };
        }

        /// <summary>يعيد قائمة أسماء الخوارزميات المتاحة</summary>
        public IReadOnlyList<string> GetAlgorithmNames() =>
            _algorithms.Values.Select(a => a.Name).ToList();

        /// <summary>يعيد الخوارزمية حسب النوع</summary>
        public IAudioCompressionAlgorithm GetAlgorithm(AlgorithmType type) =>
            _algorithms[type];

        /// <summary>
        /// ينفذ عملية الضغط.
        /// Executes compression using the selected algorithm.
        /// </summary>
        public async Task<CompressionResult> CompressAsync(
            AudioSampleData input,
            CompressionSettings settings,
            IProgress<CompressionProgress> progress,
            CancellationToken cancellationToken)
        {
            var algorithm = _algorithms[settings.AlgorithmType];
            return await algorithm.CompressAsync(input, settings, progress, cancellationToken);
        }

        /// <summary>
        /// ينفذ عملية فك الضغط.
        /// Executes decompression using the selected algorithm.
        /// </summary>
        public async Task<AudioSampleData> DecompressAsync(
            CompressionResult compressedData,
            CompressionSettings settings,
            IProgress<CompressionProgress> progress,
            CancellationToken cancellationToken)
        {
            var algorithm = _algorithms[settings.AlgorithmType];
            return await algorithm.DecompressAsync(compressedData, settings, progress, cancellationToken);
        }

        /// <summary>
        /// يحفظ نتيجة الضغط كملف .wvp (WavePress format).
        /// 
        /// هيكل الملف:
        /// [Magic: 4 bytes "WVPS"]
        /// [Version: 1 byte]
        /// [AlgorithmType: 1 byte]
        /// [OriginalSampleRate: 4 bytes]
        /// [OriginalChannels: 2 bytes]
        /// [OriginalBitsPerSample: 2 bytes]
        /// [OriginalSampleCount: 4 bytes]
        /// [TargetBitsPerSample: 4 bytes]
        /// [DeltaStepSize: 4 bytes]
        /// [AdaptiveFactor: 8 bytes]
        /// [CompressedDataLength: 4 bytes]
        /// [CompressedData: N bytes]
        /// </summary>
        public void SaveCompressedFile(string filePath, CompressionResult result)
        {
            using var fs = new FileStream(filePath, FileMode.Create);
            using var writer = new BinaryWriter(fs);

            // كتابة Header
            writer.Write("WVPS"u8);                              // Magic bytes
            writer.Write((byte)1);                                 // Version
            writer.Write((byte)(result.Settings?.AlgorithmType ?? 0)); // Algorithm
            writer.Write(result.OriginalSampleRate);               // Sample rate
            writer.Write((short)result.OriginalChannels);          // Channels
            writer.Write((short)result.OriginalBitsPerSample);     // Bits per sample
            writer.Write(result.OriginalSampleCount);              // Sample count
            writer.Write(result.Settings?.TargetBitsPerSample ?? 8); // Target bits
            writer.Write(result.Settings?.DeltaStepSize ?? 256);   // Step size
            writer.Write(result.Settings?.AdaptiveFactor ?? 1.5);  // Adaptive factor
            writer.Write(result.CompressedData.Length);             // Data length
            writer.Write(result.CompressedData);                   // Compressed data
        }

        /// <summary>
        /// يقرأ ملف .wvp ويعيد نتيجة الضغط والإعدادات.
        /// Reads a .wvp file and returns the CompressionResult + Settings.
        /// </summary>
        public (CompressionResult Result, CompressionSettings Settings) LoadCompressedFile(string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.Open);
            using var reader = new BinaryReader(fs);

            // قراءة وتحقق من Magic bytes
            byte[] magic = reader.ReadBytes(4);
            if (magic[0] != (byte)'W' || magic[1] != (byte)'V' ||
                magic[2] != (byte)'P' || magic[3] != (byte)'S')
                throw new InvalidDataException("Not a valid WavePress file.");

            byte version = reader.ReadByte();
            var algorithmType = (AlgorithmType)reader.ReadByte();
            int sampleRate = reader.ReadInt32();
            short channels = reader.ReadInt16();
            short bitsPerSample = reader.ReadInt16();
            int sampleCount = reader.ReadInt32();
            int targetBits = reader.ReadInt32();
            int stepSize = reader.ReadInt32();
            double adaptiveFactor = reader.ReadDouble();
            int dataLength = reader.ReadInt32();
            byte[] compressedData = reader.ReadBytes(dataLength);

            var settings = new CompressionSettings
            {
                AlgorithmType = algorithmType,
                TargetBitsPerSample = targetBits,
                DeltaStepSize = stepSize,
                AdaptiveFactor = adaptiveFactor
            };

            var result = new CompressionResult
            {
                CompressedData = compressedData,
                OriginalSampleRate = sampleRate,
                OriginalChannels = channels,
                OriginalBitsPerSample = bitsPerSample,
                OriginalSampleCount = sampleCount,
                CompressedSize = compressedData.Length,
                OriginalSize = sampleCount * 2,
                AlgorithmName = _algorithms[algorithmType].Name,
                Settings = settings
            };

            return (result, settings);
        }
    }
}
