using System.IO;
using NAudio.Wave;
using WavePress.Helpers;
using WavePress.Models;

namespace WavePress.Services
{
    /// <summary>
    /// خدمة قراءة ملفات الصوت — تستخرج المعلومات وبيانات العينات.
    /// Reads audio files (WAV/MP3) and extracts metadata + raw PCM samples.
    /// </summary>
    public class AudioFileService
    {
        /// <summary>
        /// يقرأ ملف صوتي ويعيد معلوماته وبيانات عيناته.
        /// Loads an audio file and returns its metadata and sample data.
        /// </summary>
        /// <param name="filePath">مسار الملف</param>
        /// <returns>tuple يحتوي معلومات الملف وبيانات العينات</returns>
        public (AudioFileInfo Info, AudioSampleData Samples) LoadAudioFile(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLowerInvariant();

            return extension switch
            {
                ".wav" => LoadWavFile(filePath),
                ".mp3" => LoadMp3File(filePath),
                _ => throw new NotSupportedException($"Unsupported audio format: {extension}")
            };
        }

        /// <summary>
        /// يقرأ ملف WAV.
        /// </summary>
        private (AudioFileInfo, AudioSampleData) LoadWavFile(string filePath)
        {
            var fileInfo = new FileInfo(filePath);

            using var reader = new WaveFileReader(filePath);
            var format = reader.WaveFormat;

            var info = new AudioFileInfo
            {
                FileName = Path.GetFileName(filePath),
                FilePath = filePath,
                FileSize = fileInfo.Length,
                Duration = reader.TotalTime,
                SampleRate = format.SampleRate,
                Channels = format.Channels,
                BitsPerSample = format.BitsPerSample,
                BitRate = format.AverageBytesPerSecond * 8 / 1000, // kbps
                Codec = format.Encoding.ToString()
            };

            // قراءة العينات الخام
            var samples = WaveFileHelper.ReadWavSamples(filePath);

            return (info, samples);
        }

        /// <summary>
        /// يقرأ ملف MP3 — يحوّله أولاً إلى PCM ثم يقرأ العينات.
        /// Reads MP3 by converting to PCM first, then extracting samples.
        /// </summary>
        private (AudioFileInfo, AudioSampleData) LoadMp3File(string filePath)
        {
            var fileInfo = new FileInfo(filePath);

            using var mp3Reader = new Mp3FileReader(filePath);
            var format = mp3Reader.WaveFormat;

            var info = new AudioFileInfo
            {
                FileName = Path.GetFileName(filePath),
                FilePath = filePath,
                FileSize = fileInfo.Length,
                Duration = mp3Reader.TotalTime,
                SampleRate = format.SampleRate,
                Channels = format.Channels,
                BitsPerSample = format.BitsPerSample,
                BitRate = format.AverageBytesPerSecond * 8 / 1000,
                Codec = "MP3"
            };

            // تحويل MP3 إلى PCM 16-bit
            using var pcmStream = WaveFormatConversionStream.CreatePcmStream(mp3Reader);
            var pcmFormat = pcmStream.WaveFormat;

            byte[] allBytes = new byte[pcmStream.Length];
            int bytesRead = pcmStream.Read(allBytes, 0, allBytes.Length);

            short[] samples = new short[bytesRead / 2];
            Buffer.BlockCopy(allBytes, 0, samples, 0, bytesRead);

            var sampleData = new AudioSampleData
            {
                Samples = samples,
                SampleRate = pcmFormat.SampleRate,
                Channels = pcmFormat.Channels,
                BitsPerSample = 16
            };

            return (info, sampleData);
        }
    }
}
