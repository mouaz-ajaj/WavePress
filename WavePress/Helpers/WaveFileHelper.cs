using WavePress.Models;

namespace WavePress.Helpers
{
    /// <summary>
    /// أدوات مساعدة لقراءة وكتابة ملفات WAV — يعمل مع بيانات PCM الخام.
    /// Helper utilities for reading and writing WAV files at the raw PCM level.
    /// </summary>
    public static class WaveFileHelper
    {
        /// <summary>
        /// يكتب بيانات العينات إلى ملف WAV.
        /// Writes AudioSampleData to a standard WAV file.
        /// </summary>
        public static void WriteWavFile(string filePath, AudioSampleData data)
        {
            using var writer = new NAudio.Wave.WaveFileWriter(
                filePath,
                new NAudio.Wave.WaveFormat(data.SampleRate, data.BitsPerSample, data.Channels));

            // تحويل short[] إلى byte[] للكتابة
            // Convert short samples to byte array
            byte[] buffer = new byte[data.Samples.Length * 2];
            Buffer.BlockCopy(data.Samples, 0, buffer, 0, buffer.Length);
            writer.Write(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// يقرأ عينات PCM خام من ملف WAV.
        /// Reads raw PCM samples from a WAV file.
        /// </summary>
        public static AudioSampleData ReadWavSamples(string filePath)
        {
            using var reader = new NAudio.Wave.WaveFileReader(filePath);
            var format = reader.WaveFormat;

            // قراءة كل البايتات
            byte[] allBytes = new byte[reader.Length];
            int bytesRead = reader.Read(allBytes, 0, allBytes.Length);

            // تحويل إلى short[] (16-bit PCM)
            // إذا كان الملف 8-bit، نحوّله إلى 16-bit
            short[] samples;
            if (format.BitsPerSample == 16)
            {
                samples = new short[bytesRead / 2];
                Buffer.BlockCopy(allBytes, 0, samples, 0, bytesRead);
            }
            else if (format.BitsPerSample == 8)
            {
                // تحويل 8-bit unsigned إلى 16-bit signed
                samples = new short[bytesRead];
                for (int i = 0; i < bytesRead; i++)
                {
                    samples[i] = (short)((allBytes[i] - 128) * 256);
                }
            }
            else if (format.BitsPerSample == 24)
            {
                // تحويل 24-bit إلى 16-bit (نأخذ البايتين العلويين)
                int sampleCount = bytesRead / 3;
                samples = new short[sampleCount];
                for (int i = 0; i < sampleCount; i++)
                {
                    int offset = i * 3;
                    // البايت الأوسط والعالي يمثلان القيمة الأهم
                    samples[i] = (short)((allBytes[offset + 2] << 8) | allBytes[offset + 1]);
                }
            }
            else if (format.BitsPerSample == 32)
            {
                // تحويل 32-bit int إلى 16-bit
                int sampleCount = bytesRead / 4;
                samples = new short[sampleCount];
                for (int i = 0; i < sampleCount; i++)
                {
                    int value = BitConverter.ToInt32(allBytes, i * 4);
                    samples[i] = (short)(value >> 16);
                }
            }
            else
            {
                throw new NotSupportedException($"Unsupported bit depth: {format.BitsPerSample}");
            }

            return new AudioSampleData
            {
                Samples = samples,
                SampleRate = format.SampleRate,
                Channels = format.Channels,
                BitsPerSample = 16 // نخزن دائماً كـ 16-bit داخلياً
            };
        }
    }
}
