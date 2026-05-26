using WavePress.Models;

namespace WavePress.Algorithms
{
    /// <summary>
    /// واجهة موحدة لجميع خوارزميات ضغط الصوت.
    /// كل خوارزمية تنفذ هذه الواجهة لضمان التوافق مع نظام الضغط.
    /// 
    /// Unified interface for all audio compression algorithms.
    /// Each algorithm implements Compress and Decompress with async support,
    /// progress reporting, and cancellation.
    /// </summary>
    public interface IAudioCompressionAlgorithm
    {
        /// <summary>اسم الخوارزمية للعرض في الواجهة</summary>
        string Name { get; }

        /// <summary>وصف مختصر لطريقة عمل الخوارزمية</summary>
        string Description { get; }

        /// <summary>
        /// ينفذ عملية الضغط بشكل غير متزامن.
        /// Performs asynchronous compression of audio sample data.
        /// </summary>
        /// <param name="input">بيانات العينات الخام</param>
        /// <param name="settings">إعدادات الضغط</param>
        /// <param name="progress">كائن الإبلاغ عن التقدم</param>
        /// <param name="cancellationToken">رمز الإلغاء</param>
        /// <returns>نتيجة الضغط</returns>
        Task<CompressionResult> CompressAsync(
            AudioSampleData input,
            CompressionSettings settings,
            IProgress<CompressionProgress> progress,
            CancellationToken cancellationToken);

        /// <summary>
        /// ينفذ عملية فك الضغط بشكل غير متزامن.
        /// Performs asynchronous decompression of compressed data.
        /// </summary>
        /// <param name="compressedData">البيانات المضغوطة</param>
        /// <param name="settings">الإعدادات المستخدمة أثناء الضغط</param>
        /// <param name="progress">كائن الإبلاغ عن التقدم</param>
        /// <param name="cancellationToken">رمز الإلغاء</param>
        /// <returns>بيانات العينات المُفكَّة</returns>
        Task<AudioSampleData> DecompressAsync(
            CompressionResult compressedData,
            CompressionSettings settings,
            IProgress<CompressionProgress> progress,
            CancellationToken cancellationToken);
    }
}
