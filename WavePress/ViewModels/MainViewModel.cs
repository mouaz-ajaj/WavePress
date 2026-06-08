using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using WavePress.Helpers;
using WavePress.Models;
using WavePress.Services;

namespace WavePress.ViewModels
{
    /// <summary>
    /// الـ ViewModel الرئيسي — يربط بين الواجهة والخدمات.
    /// يدير كل العمليات: استيراد الملف، التشغيل، الضغط، فك الضغط، التقرير، والحفظ.
    /// 
    /// Main ViewModel — connects UI to services.
    /// Uses CommunityToolkit.Mvvm for ObservableProperty and RelayCommand source generators.
    /// </summary>
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        // ── الخدمات ──
        private readonly AudioFileService _audioFileService = new();
        private readonly AudioPlaybackService _playbackService = new();
        private readonly CompressionService _compressionService = new();
        private readonly FileDialogService _fileDialogService = new();

        private CancellationTokenSource? _cts;
        private bool _disposed;

        // ══════════════════════════════════════
        //  الخصائص المرتبطة بالملف
        // ══════════════════════════════════════

        [ObservableProperty]
        private AudioFileInfo? _audioInfo;

        [ObservableProperty]
        private AudioSampleData? _sampleData;

        [ObservableProperty]
        private bool _isFileLoaded;

        [ObservableProperty]
        private string _statusMessage = "Ready — drag and drop an audio file or click Browse";

        [ObservableProperty]
        private string _loadedFileName = "";

        // ══════════════════════════════════════
        //  خصائص التشغيل (Playback)
        // ══════════════════════════════════════

        [ObservableProperty]
        private bool _isPlaying;

        [ObservableProperty]
        private string _currentPositionText = "00:00";

        [ObservableProperty]
        private string _totalDurationText = "00:00";

        [ObservableProperty]
        private double _playbackProgress; // 0-100

        // ══════════════════════════════════════
        //  إعدادات الضغط
        // ══════════════════════════════════════

        [ObservableProperty]
        private int _selectedAlgorithmIndex;

        public string[] AlgorithmNames { get; } = new[]
        {
            "Nonlinear Quantization (µ-law)",
            "DPCM (Differential PCM)",
            "Delta Modulation",
            "Adaptive Delta Modulation"
        };

        [ObservableProperty]
        private int _targetBitsPerSample = 8;

        /// <summary>
        /// QuantizationLevels مشتق تلقائياً من TargetBitsPerSample = 2^bits.
        /// لا يقبل إدخال مستخدم — القيمة دائماً 2^TargetBitsPerSample.
        /// Auto-derived: always 2^TargetBitsPerSample. Not user-editable.
        /// </summary>
        public int QuantizationLevels => 1 << TargetBitsPerSample;

        /// <summary>نص عرض مستويات التكميم المحسوبة تلقائياً في الواجهة.</summary>
        public string QuantizationLevelsDisplay => $"{QuantizationLevels} (auto: 2^{TargetBitsPerSample})";

        [ObservableProperty]
        private int _deltaStepSize = 128;

        [ObservableProperty]
        private double _adaptiveFactor = 1.5;

        [ObservableProperty]
        private string _outputFileName = "output";

        // ── حالة تفعيل/تعطيل الإعدادات حسب الخوارزمية ──
        // 0 = Nonlinear Quantization → BitsPerSample فقط (QuantizationLevels مشتق تلقائياً)
        // 1 = DPCM                   → BitsPerSample فقط
        // 2 = Delta Modulation       → DeltaStepSize فقط
        // 3 = Adaptive Delta Mod.    → DeltaStepSize + AdaptiveFactor

        [ObservableProperty]
        private bool _isBitsPerSampleEnabled = true;

        // QuantizationLevels معطّل دائماً (مشتق تلقائياً — لا حاجة لإدخال المستخدم)
        // QuantizationLevels is always disabled — it's auto-derived from BitsPerSample.
        public bool IsQuantizationLevelsEnabled => false;

        [ObservableProperty]
        private bool _isDeltaStepSizeEnabled = false;

        [ObservableProperty]
        private bool _isAdaptiveFactorEnabled = false;

        /// <summary>
        /// يُستدعى تلقائياً بواسطة CommunityToolkit عند تغيير SelectedAlgorithmIndex.
        /// يحدّث حالة تفعيل/تعطيل الإعدادات بناءً على الخوارزمية المختارة.
        /// Called automatically by CommunityToolkit when SelectedAlgorithmIndex changes.
        /// </summary>
        partial void OnSelectedAlgorithmIndexChanged(int value)
        {
            switch (value)
            {
                case 0: // Nonlinear Quantization (µ-law)
                case 1: // DPCM
                    IsBitsPerSampleEnabled  = true;
                    IsDeltaStepSizeEnabled  = false;
                    IsAdaptiveFactorEnabled = false;
                    break;

                case 2: // Delta Modulation
                    IsBitsPerSampleEnabled  = false;
                    IsDeltaStepSizeEnabled  = true;
                    IsAdaptiveFactorEnabled = false;
                    break;

                case 3: // Adaptive Delta Modulation
                    IsBitsPerSampleEnabled  = false;
                    IsDeltaStepSizeEnabled  = true;
                    IsAdaptiveFactorEnabled = true;
                    break;
            }
        }

        /// <summary>
        /// يُستدعى تلقائياً عند تغيير TargetBitsPerSample — يُخطر الواجهة بتحديث الحقول المشتقة.
        /// Fires when TargetBitsPerSample changes to refresh derived QuantizationLevels display.
        /// </summary>
        partial void OnTargetBitsPerSampleChanged(int value)
        {
            OnPropertyChanged(nameof(QuantizationLevels));
            OnPropertyChanged(nameof(QuantizationLevelsDisplay));
        }

        /// <summary>
        /// يتحقق من صحة TargetBitsPerSample قبل الضغط (1-16 فقط).
        /// Returns an error message if invalid, null if valid.
        /// </summary>
        private string? ValidateSettings()
        {
            // التحقق من BitsPerSample فقط إن كانت الخوارزمية تستخدمه
            bool usesBits = SelectedAlgorithmIndex == 0 || SelectedAlgorithmIndex == 1;
            if (usesBits && (TargetBitsPerSample < 1 || TargetBitsPerSample > 16))
                return $"Bits Per Sample must be between 1 and 16. You entered: {TargetBitsPerSample}";

            if (IsDeltaStepSizeEnabled && DeltaStepSize < 1)
                return "Delta Step Size must be at least 1.";

            if (IsAdaptiveFactorEnabled && AdaptiveFactor < 1.0)
                return "Adaptive Factor must be >= 1.0.";

            return null; // صالح
        }

        // ══════════════════════════════════════
        //  حالة الضغط والتقدم
        // ══════════════════════════════════════

        [ObservableProperty]
        private bool _isProcessing;

        [ObservableProperty]
        private double _progressValue; // 0-100

        [ObservableProperty]
        private string _progressPhase = "";

        [ObservableProperty]
        private string _currentCompressionRatio = "—";

        [ObservableProperty]
        private string _currentProcessingSpeed = "—";

        // ══════════════════════════════════════
        //  نتيجة الضغط والتقرير
        // ══════════════════════════════════════

        [ObservableProperty]
        private CompressionResult? _compressionResult;

        [ObservableProperty]
        private ReportModel? _report;

        [ObservableProperty]
        private bool _hasReport;

        [ObservableProperty]
        private string _reportOriginalSize = "";

        [ObservableProperty]
        private string _reportPcmSize = ""; // حجم PCM الخام قبل الضغط

        [ObservableProperty]
        private string _reportCompressedSize = "";

        [ObservableProperty]
        private string _reportSavedSize = "";

        [ObservableProperty]
        private string _reportSavingPercentage = "";

        [ObservableProperty]
        private string _reportCompressionRatio = "";

        [ObservableProperty]
        private string _reportTimeElapsed = "";

        [ObservableProperty]
        private string _reportAlgorithm = "";

        [ObservableProperty]
        private string _reportOutputPath = "";

        // ══════════════════════════════════════
        //  عرض خصائص الملف (للـ UI)
        // ══════════════════════════════════════

        [ObservableProperty]
        private string _displayFileSize = "—";

        [ObservableProperty]
        private string _displayDuration = "—";

        [ObservableProperty]
        private string _displaySampleRate = "—";

        [ObservableProperty]
        private string _displayChannels = "—";

        [ObservableProperty]
        private string _displayBitsPerSample = "—";

        [ObservableProperty]
        private string _displayBitRate = "—";

        [ObservableProperty]
        private string _displayCodec = "—";

        // ══════════════════════════════════════
        //  الرسوم البيانية (Charts)
        // ══════════════════════════════════════

        public ObservableCollection<ObservableValue> CompressionRatioValues { get; } = new();
        public ObservableCollection<ObservableValue> ProcessingSpeedValues { get; } = new();

        public ISeries[] CompressionRatioSeries { get; }
        public ISeries[] ProcessingSpeedSeries { get; }

        // إعدادات المحاور — مخفية لإظهار المخطط فقط بدون أرقام
        public Axis[] ChartXAxes { get; } = new Axis[]
        {
            new Axis { ShowSeparatorLines = false, IsVisible = false }
        };
        public Axis[] ChartYAxes { get; } = new Axis[]
        {
            new Axis { ShowSeparatorLines = false, IsVisible = false }
        };

        // ══════════════════════════════════════
        //  المنشئ (Constructor)
        // ══════════════════════════════════════

        public MainViewModel()
        {
            // إعداد الرسوم البيانية
            CompressionRatioSeries = new ISeries[]
            {
                new LineSeries<ObservableValue>
                {
                    Values = CompressionRatioValues,
                    Fill = new SolidColorPaint(SKColors.Cyan.WithAlpha(40)),
                    Stroke = new SolidColorPaint(SKColors.Cyan, 2),
                    GeometrySize = 0,
                    LineSmoothness = 0.5
                }
            };

            ProcessingSpeedSeries = new ISeries[]
            {
                new LineSeries<ObservableValue>
                {
                    Values = ProcessingSpeedValues,
                    Fill = new SolidColorPaint(SKColors.MediumPurple.WithAlpha(40)),
                    Stroke = new SolidColorPaint(SKColors.MediumPurple, 2),
                    GeometrySize = 0,
                    LineSmoothness = 0.5
                }
            };

            // الاستماع لتحديثات الموضع أثناء التشغيل
            _playbackService.PositionChanged += OnPlaybackPositionChanged;
            _playbackService.PlaybackStopped += OnPlaybackStopped;
        }

        // ══════════════════════════════════════
        //  أوامر استيراد الملف
        // ══════════════════════════════════════

        /// <summary>يفتح حوار اختيار ملف صوتي</summary>
        [RelayCommand]
        private void Browse()
        {
            var filePath = _fileDialogService.OpenAudioFile();
            if (filePath != null)
                LoadFile(filePath);
        }

        /// <summary>
        /// يحمّل ملف صوتي من مسار معين (يُستدعى من Browse أو Drag & Drop).
        /// Loads an audio file and populates all display properties.
        /// </summary>
        public void LoadFile(string filePath)
        {
            try
            {
                StatusMessage = "Loading audio file...";

                var (info, samples) = _audioFileService.LoadAudioFile(filePath);

                AudioInfo = info;
                SampleData = samples;
                IsFileLoaded = true;
                LoadedFileName = info.FileName;

                // تحديث خصائص العرض
                DisplayFileSize = FileSizeFormatter.Format(info.FileSize);
                DisplayDuration = info.Duration.ToString(@"mm\:ss\.ff");
                DisplaySampleRate = $"{info.SampleRate} Hz";
                DisplayChannels = info.Channels == 1 ? "Mono" : "Stereo";
                DisplayBitsPerSample = $"{info.BitsPerSample} bit";
                DisplayBitRate = $"{info.BitRate} kbps";
                DisplayCodec = info.Codec;

                // تحميل الملف في مشغل الصوت
                _playbackService.LoadFile(filePath);
                TotalDurationText = info.Duration.ToString(@"mm\:ss");
                CurrentPositionText = "00:00";
                PlaybackProgress = 0;

                StatusMessage = $"Loaded: {info.FileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading file: {ex.Message}";
                MessageBox.Show($"Failed to load audio file:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ══════════════════════════════════════
        //  أوامر التشغيل (Playback)
        // ══════════════════════════════════════

        [RelayCommand]
        private void Play()
        {
            if (!_playbackService.IsLoaded) return;
            _playbackService.Play();
            IsPlaying = true;
            StatusMessage = "Playing...";
        }

        [RelayCommand]
        private void PausePlayback()
        {
            _playbackService.Pause();
            IsPlaying = false;
            StatusMessage = "Paused";
        }

        [RelayCommand]
        private void StopPlayback()
        {
            _playbackService.Stop();
            IsPlaying = false;
            PlaybackProgress = 0;
            CurrentPositionText = "00:00";
            StatusMessage = "Stopped";
        }

        private void OnPlaybackPositionChanged(TimeSpan position)
        {
            // يُستدعى من thread آخر — نحتاج Dispatcher
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                CurrentPositionText = position.ToString(@"mm\:ss");
                if (_playbackService.TotalTime.TotalSeconds > 0)
                    PlaybackProgress = position.TotalSeconds / _playbackService.TotalTime.TotalSeconds * 100.0;
            });
        }

        private void OnPlaybackStopped()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsPlaying = false;
                StatusMessage = "Playback finished";
            });
        }

        // ══════════════════════════════════════
        //  أمر الضغط (Compress)
        // ══════════════════════════════════════

        [RelayCommand]
        private async Task CompressAsync()
        {
            if (SampleData == null)
            {
                MessageBox.Show("Please load an audio file first.", "No File", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // ── التحقق من صحة الإعدادات قبل الضغط ──
            string? validationError = ValidateSettings();
            if (validationError != null)
            {
                MessageBox.Show(validationError, "Invalid Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // إعداد الإعدادات
            var settings = BuildSettings();

            // إعداد CancellationToken
            _cts = new CancellationTokenSource();
            IsProcessing = true;
            ProgressValue = 0;
            HasReport = false;

            // تنظيف بيانات الرسوم البيانية
            CompressionRatioValues.Clear();
            ProcessingSpeedValues.Clear();

            StatusMessage = $"Compressing with {AlgorithmNames[SelectedAlgorithmIndex]}...";

            // إعداد Progress handler
            var progressHandler = new Progress<CompressionProgress>(p =>
            {
                ProgressValue = p.PercentComplete;
                ProgressPhase = p.CurrentPhase;
                CurrentCompressionRatio = p.CompressionRatio > 0 ? $"{p.CompressionRatio:F2}:1" : "—";
                CurrentProcessingSpeed = p.ProcessingSpeedMBps > 0 ? $"{p.ProcessingSpeedMBps:F2} MB/s" : "—";

                // إضافة نقاط للرسوم البيانية (كل 5%)
                if (p.CompressionRatio > 0 && CompressionRatioValues.Count < 100)
                {
                    CompressionRatioValues.Add(new ObservableValue(p.CompressionRatio));
                }
                if (p.ProcessingSpeedMBps > 0 && ProcessingSpeedValues.Count < 100)
                {
                    ProcessingSpeedValues.Add(new ObservableValue(p.ProcessingSpeedMBps));
                }
            });

            try
            {
                var result = await _compressionService.CompressAsync(
                    SampleData, settings, progressHandler, _cts.Token);

                CompressionResult = result;

                // بناء التقرير
                BuildReport(result, settings);

                StatusMessage = $"Compression complete — ratio: {Report!.CompressionRatio:F2}:1, saved: {Report.SavingPercentage:F1}%";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Compression cancelled.";
                ProgressValue = 0;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                MessageBox.Show($"Compression failed:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsProcessing = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        // ══════════════════════════════════════
        //  أمر فك الضغط (Decompress)
        // ══════════════════════════════════════

        [RelayCommand]
        private async Task DecompressAsync()
        {
            CompressionResult? resultToDecompress = CompressionResult;
            CompressionSettings? settingsToUse = CompressionResult?.Settings;

            // إذا لم يوجد نتيجة ضغط في الذاكرة، نطلب ملف .wvp
            if (resultToDecompress == null)
            {
                var filePath = _fileDialogService.OpenCompressedFile();
                if (filePath == null) return;

                try
                {
                    var (loaded, loadedSettings) = _compressionService.LoadCompressedFile(filePath);
                    resultToDecompress = loaded;
                    settingsToUse = loadedSettings;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to load compressed file:\n{ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            _cts = new CancellationTokenSource();
            IsProcessing = true;
            ProgressValue = 0;
            StatusMessage = "Decompressing...";

            var progressHandler = new Progress<CompressionProgress>(p =>
            {
                ProgressValue = p.PercentComplete;
                ProgressPhase = p.CurrentPhase;
            });

            try
            {
                var decompressed = await _compressionService.DecompressAsync(
                    resultToDecompress, settingsToUse!, progressHandler, _cts.Token);

                // حفظ الملف المفكوك
                var savePath = _fileDialogService.SaveWavFile(OutputFileName + "_decompressed");
                if (savePath != null)
                {
                    WaveFileHelper.WriteWavFile(savePath, decompressed);
                    StatusMessage = $"Decompressed and saved to: {Path.GetFileName(savePath)}";
                }
                else
                {
                    StatusMessage = "Decompression complete (not saved).";
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Decompression cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                MessageBox.Show($"Decompression failed:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsProcessing = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        // ══════════════════════════════════════
        //  أمر الإلغاء (Cancel)
        // ══════════════════════════════════════

        [RelayCommand]
        private void Cancel()
        {
            _cts?.Cancel();
            StatusMessage = "Cancelling...";
        }

        // ══════════════════════════════════════
        //  أمر إعادة الضبط (Reset)
        // ══════════════════════════════════════

        [RelayCommand]
        private void Reset()
        {
            // إيقاف التشغيل
            _playbackService.Stop();
            IsPlaying = false;

            // مسح الملف
            AudioInfo = null;
            SampleData = null;
            IsFileLoaded = false;
            LoadedFileName = "";

            // مسح خصائص العرض
            DisplayFileSize = "—";
            DisplayDuration = "—";
            DisplaySampleRate = "—";
            DisplayChannels = "—";
            DisplayBitsPerSample = "—";
            DisplayBitRate = "—";
            DisplayCodec = "—";

            // إعادة ضبط التشغيل
            CurrentPositionText = "00:00";
            TotalDurationText = "00:00";
            PlaybackProgress = 0;

            // إعادة ضبط الإعدادات
            SelectedAlgorithmIndex = 0;
            TargetBitsPerSample    = 8;
            // QuantizationLevels مشتق تلقائياً — لا يحتاج إعادة ضبط
            DeltaStepSize  = 128;
            AdaptiveFactor = 1.5;
            OutputFileName = "output";

            // مسح التقدم والتقرير
            ProgressValue = 0;
            ProgressPhase = "";
            CurrentCompressionRatio = "—";
            CurrentProcessingSpeed = "—";
            CompressionResult = null;
            Report = null;
            HasReport = false;

            // مسح الرسوم البيانية
            CompressionRatioValues.Clear();
            ProcessingSpeedValues.Clear();

            // مسح نصوص التقرير
            ReportOriginalSize = "";
            ReportCompressedSize = "";
            ReportSavedSize = "";
            ReportSavingPercentage = "";
            ReportCompressionRatio = "";
            ReportTimeElapsed = "";
            ReportAlgorithm = "";
            ReportOutputPath = "";

            StatusMessage = "Reset — ready for new file";
        }

        // ══════════════════════════════════════
        //  أمر الحفظ (Save)
        // ══════════════════════════════════════

        [RelayCommand]
        private void SaveCompressed()
        {
            if (CompressionResult == null)
            {
                MessageBox.Show("No compressed data to save. Please compress a file first.",
                    "Nothing to Save", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var savePath = _fileDialogService.SaveCompressedFile(OutputFileName);
            if (savePath == null) return;

            try
            {
                _compressionService.SaveCompressedFile(savePath, CompressionResult);
                StatusMessage = $"Saved compressed file: {Path.GetFileName(savePath)}";

                if (Report != null)
                    ReportOutputPath = savePath;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ══════════════════════════════════════
        //  دوال مساعدة
        // ══════════════════════════════════════

        /// <summary>يبني إعدادات الضغط من القيم الحالية في الواجهة</summary>
        private CompressionSettings BuildSettings()
        {
            return new CompressionSettings
            {
                AlgorithmType       = (AlgorithmType)SelectedAlgorithmIndex,
                TargetBitsPerSample = TargetBitsPerSample,
                // QuantizationLevels مشتق تلقائياً من BitsPerSample — لا حاجة لإدخال المستخدم
                QuantizationLevels  = QuantizationLevels, // = 1 << TargetBitsPerSample
                DeltaStepSize       = DeltaStepSize,
                AdaptiveFactor      = AdaptiveFactor,
                OutputFileName      = OutputFileName
            };
        }

        /// <summary>يبني نموذج التقرير من نتيجة الضغط</summary>
        private void BuildReport(CompressionResult result, CompressionSettings settings)
        {
            // الحجم الأصلي للملف على القرص (وليس PCM الموسّع)
            long diskFileSize = AudioInfo?.FileSize ?? result.OriginalSize;
            // حجم PCM الخام (هو ما تعمل عليه الخوارزمية فعلياً)
            long pcmSize = result.OriginalSize;

            Report = new ReportModel
            {
                // نستخدم حجم الملف الفعلي للمقارنة المنطقية في التقرير
                OriginalSize = diskFileSize,
                CompressedSize = result.CompressedSize,
                TimeElapsed = result.TimeElapsed,
                AlgorithmName = result.AlgorithmName,
                SampleRate = AudioInfo?.SampleRate ?? 0,
                QuantizationLevels = settings.QuantizationLevels,
                BitsPerSample = settings.TargetBitsPerSample,
                StepSize = settings.DeltaStepSize
            };

            HasReport = true;

            // تحديث نصوص التقرير للعرض
            ReportOriginalSize = FileSizeFormatter.Format(diskFileSize);
            // نعرض حجم PCM الخام للتوضيح الأكاديمي
            ReportPcmSize = FileSizeFormatter.Format(pcmSize);
            ReportCompressedSize = FileSizeFormatter.Format(result.CompressedSize);
            // نسبة التوفير والحجم الموفوّر مقارنةً بـ PCM الخام (وهو ما ضغطناه فعلاً)
            long savedFromPcm = pcmSize - result.CompressedSize;
            double savingPct = pcmSize > 0 ? (double)savedFromPcm / pcmSize * 100.0 : 0;
            double ratioFromPcm = result.CompressedSize > 0 ? (double)pcmSize / result.CompressedSize : 0;
            ReportSavedSize = FileSizeFormatter.Format(Math.Max(0, savedFromPcm));
            ReportSavingPercentage = $"{Math.Max(0, savingPct):F1}%";
            ReportCompressionRatio = $"{ratioFromPcm:F2}:1";
            ReportTimeElapsed = $"{result.TimeElapsed.TotalMilliseconds:F0} ms";
            ReportAlgorithm = result.AlgorithmName;
        }

        // ══════════════════════════════════════
        //  تنظيف الموارد
        // ══════════════════════════════════════

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _playbackService.Dispose();
            _cts?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
