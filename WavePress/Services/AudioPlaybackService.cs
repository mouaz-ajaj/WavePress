using NAudio.Wave;

namespace WavePress.Services
{
    /// <summary>
    /// خدمة تشغيل الصوت — تدير Play, Pause, Stop مع تتبع الموضع.
    /// Audio playback service using NAudio WaveOutEvent.
    /// Manages Play/Pause/Stop with position tracking.
    /// </summary>
    public class AudioPlaybackService : IDisposable
    {
        private WaveOutEvent? _waveOut;
        private AudioFileReader? _audioFileReader;
        private System.Timers.Timer? _positionTimer;
        private bool _disposed;

        /// <summary>يُطلَق عند تحديث موضع التشغيل</summary>
        public event Action<TimeSpan>? PositionChanged;

        /// <summary>يُطلَق عند انتهاء التشغيل</summary>
        public event Action? PlaybackStopped;

        /// <summary>المدة الكلية للملف المحمَّل</summary>
        public TimeSpan TotalTime => _audioFileReader?.TotalTime ?? TimeSpan.Zero;

        /// <summary>الموضع الحالي</summary>
        public TimeSpan CurrentTime
        {
            get => _audioFileReader?.CurrentTime ?? TimeSpan.Zero;
            set
            {
                if (_audioFileReader != null)
                    _audioFileReader.CurrentTime = value;
            }
        }

        /// <summary>هل يوجد ملف محمَّل حالياً؟</summary>
        public bool IsLoaded => _audioFileReader != null;

        /// <summary>هل يتم التشغيل حالياً؟</summary>
        public bool IsPlaying => _waveOut?.PlaybackState == PlaybackState.Playing;

        /// <summary>
        /// يحمّل ملف صوتي للتشغيل.
        /// Loads an audio file for playback.
        /// </summary>
        public void LoadFile(string filePath)
        {
            // تنظيف المصادر السابقة
            CleanupPlayback();

            _audioFileReader = new AudioFileReader(filePath);
            _waveOut = new WaveOutEvent();
            _waveOut.Init(_audioFileReader);

            _waveOut.PlaybackStopped += (s, e) =>
            {
                _positionTimer?.Stop();
                PlaybackStopped?.Invoke();
            };

            // مؤقت لتحديث الموضع كل 100ms
            _positionTimer = new System.Timers.Timer(100);
            _positionTimer.Elapsed += (s, e) =>
            {
                if (_audioFileReader != null)
                    PositionChanged?.Invoke(_audioFileReader.CurrentTime);
            };
        }

        /// <summary>يبدأ أو يستأنف التشغيل</summary>
        public void Play()
        {
            if (_waveOut == null) return;
            _waveOut.Play();
            _positionTimer?.Start();
        }

        /// <summary>يوقف التشغيل مؤقتاً</summary>
        public void Pause()
        {
            if (_waveOut == null) return;
            _waveOut.Pause();
            _positionTimer?.Stop();
        }

        /// <summary>يوقف التشغيل ويعيد الموضع للبداية</summary>
        public void Stop()
        {
            if (_waveOut == null) return;
            _waveOut.Stop();
            _positionTimer?.Stop();
            if (_audioFileReader != null)
                _audioFileReader.Position = 0;
        }

        /// <summary>ينظف مصادر التشغيل الحالية</summary>
        private void CleanupPlayback()
        {
            _positionTimer?.Stop();
            _positionTimer?.Dispose();
            _positionTimer = null;

            _waveOut?.Stop();
            _waveOut?.Dispose();
            _waveOut = null;

            _audioFileReader?.Dispose();
            _audioFileReader = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CleanupPlayback();
            GC.SuppressFinalize(this);
        }
    }
}
