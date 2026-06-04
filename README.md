<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-blueviolet?style=for-the-badge&logo=dotnet" alt=".NET 8" />
  <img src="https://img.shields.io/badge/WPF-Desktop-blue?style=for-the-badge&logo=windows" alt="WPF" />
  <img src="https://img.shields.io/badge/Pattern-MVVM-green?style=for-the-badge" alt="MVVM" />
  <img src="https://img.shields.io/badge/License-MIT-yellow?style=for-the-badge" alt="MIT License" />
</p>

<h1 align="center">🎵 WavePress</h1>
<p align="center">
  <strong>A modern desktop application for audio file compression & decompression</strong>
</p>
<p align="center">
  Built with C# / WPF / .NET 8 — featuring four real-time audio compression algorithms,<br/>
  live performance monitoring, and a premium dark glassmorphism UI.
</p>

---

## ✨ Features

- **Import & Preview** — Load WAV or MP3 files via file dialog or drag-and-drop, with built-in audio playback (play, pause, stop).
- **File Analysis** — Instantly view file metadata: size, duration, sample rate, channels, bit depth, bit rate, and codec.
- **4 Compression Algorithms** — Nonlinear Quantization (µ-law), DPCM, Delta Modulation, and Adaptive Delta Modulation.
- **Configurable Parameters** — Adjust bits per sample, quantization levels, delta step size, and adaptive factor per algorithm.
- **Live Performance Monitoring** — Real-time charts for compression ratio and processing speed during compression.
- **Compression Report** — Detailed post-compression summary: original size, compressed size, savings %, time elapsed.
- **Custom File Format** — Save compressed output as `.wvp` (WavePress format) with full metadata header for lossless decompression.
- **Export to WAV** — Decompress any `.wvp` file back to a standard WAV file.
- **Async & Cancellable** — All compression operations run asynchronously with cancellation support.
- **Premium Dark UI** — Glassmorphism design, custom title bar, gradient accents, and animated progress indicators.

---

## 🖥️ Screenshots

> _Run the application to see the full UI — a modern dark-themed interface with gradient accents and glassmorphism cards._

---

## 🏗️ Architecture

The project follows the **MVVM (Model-View-ViewModel)** pattern with clean separation of concerns:

```
WavePress/
│
├── Algorithms/                     # Compression algorithm implementations
│   ├── IAudioCompressionAlgorithm.cs    → Unified interface for all algorithms
│   ├── NonlinearQuantizationAlgorithm.cs → µ-law companding + quantization
│   ├── DpcmAlgorithm.cs                 → Differential Pulse Code Modulation
│   ├── DeltaModulationAlgorithm.cs      → 1-bit delta encoding (fixed step)
│   └── AdaptiveDeltaModulationAlgorithm.cs → 1-bit delta (adaptive step)
│
├── Models/                         # Data models (no logic)
│   ├── AudioFileInfo.cs                 → Audio file metadata
│   ├── AudioSampleData.cs               → Raw PCM sample container
│   ├── CompressionSettings.cs           → User-configurable settings + AlgorithmType enum
│   ├── CompressionProgress.cs           → Real-time progress data
│   ├── CompressionResult.cs             → Compression output + decompression metadata
│   └── ReportModel.cs                   → Final report with computed properties
│
├── Services/                       # Business logic layer
│   ├── AudioFileService.cs              → Reads WAV/MP3, extracts metadata + samples
│   ├── AudioPlaybackService.cs          → Play/Pause/Stop with position tracking
│   ├── CompressionService.cs            → Orchestrates algorithms + .wvp file I/O
│   └── FileDialogService.cs             → Win32 Open/Save dialogs (MVVM-friendly)
│
├── ViewModels/                     # UI logic layer
│   └── MainViewModel.cs                 → Connects all services to UI bindings
│
├── Helpers/                        # Utility classes
│   ├── WaveFileHelper.cs                → Low-level WAV read/write (8/16/24/32-bit)
│   └── FileSizeFormatter.cs             → Byte count → human-readable string
│
├── Converters/                     # XAML value converters
│   └── BooleanConverters.cs             → Bool↔Visibility, InverseBool, Null↔Visibility
│
├── Resources/                      # UI theming
│   ├── Colors.xaml                      → Full color palette + gradient brushes
│   └── Styles.xaml                      → All control templates & styles
│
├── MainWindow.xaml                 # Main UI layout (XAML)
├── MainWindow.xaml.cs              # Code-behind (drag-drop, title bar buttons)
├── App.xaml                        # Application entry point & resource merging
└── App.xaml.cs                     # App startup configuration
```

---

## 🔬 Algorithms

### 1. Nonlinear Quantization (µ-law)
Uses logarithmic µ-law companding (µ = 255) to compress the dynamic range before quantization. Quiet signals get finer resolution while loud signals are compressed — preserving perceptual quality with fewer bits.

**Formula:** `F(x) = sgn(x) · ln(1 + µ|x|) / ln(1 + µ)`

### 2. DPCM (Differential Pulse Code Modulation)
Encodes the *difference* between consecutive samples instead of absolute values. Since adjacent audio samples are highly correlated, differences are small and can be represented with fewer bits. Includes automatic scale factor calculation.

### 3. Delta Modulation (DM)
The simplest differential encoder: **1 bit per sample**. Each bit indicates "step up by δ" or "step down by δ" with a fixed step size. Achieves 16:1 compression ratio at the cost of slope overload and granular noise.

### 4. Adaptive Delta Modulation (ADM)
Improves upon standard DM by dynamically adjusting the step size:
- **Same direction repeats** → step size increases (handles steep slopes)
- **Direction changes** → step size decreases (reduces granular noise)

Same 16:1 ratio as DM but with significantly better audio quality.

---

## 📦 .wvp File Format

WavePress uses a custom binary format (`.wvp`) for compressed audio:

| Offset | Size | Field |
|--------|------|-------|
| 0 | 4 bytes | Magic bytes: `WVPS` |
| 4 | 1 byte | Format version |
| 5 | 1 byte | Algorithm type |
| 6 | 4 bytes | Original sample rate |
| 10 | 2 bytes | Original channels |
| 12 | 2 bytes | Original bits per sample |
| 14 | 4 bytes | Original sample count |
| 18 | 4 bytes | Target bits per sample |
| 22 | 4 bytes | Delta step size |
| 26 | 8 bytes | Adaptive factor |
| 34 | 4 bytes | Compressed data length |
| 38 | N bytes | Compressed data |

---

## 🚀 Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10/11
- Visual Studio 2022+ (recommended) or any C# IDE

### Build & Run

```bash
# Clone the repository
git clone https://github.com/mouaz-ajaj/WavePress.git
cd WavePress

# Restore packages & build
dotnet restore
dotnet build

# Run the application
dotnet run --project WavePress/WavePress.csproj
```

Or open `WavePress.sln` in Visual Studio and press **F5**.

---

## 📚 Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| [NAudio](https://github.com/naudio/NAudio) | 2.2.1 | Audio file I/O and playback |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.4.0 | MVVM source generators |
| [LiveChartsCore](https://github.com/beto-rodriguez/LiveCharts2) | 2.0.0-rc5.4 | Real-time performance charts |

---

## 🎓 Academic Context

This project was developed as a practical assignment for the **Multimedia** course, demonstrating real-world application of digital audio compression concepts:

- **Quantization theory** — Linear vs. nonlinear quantization, µ-law companding
- **Predictive coding** — DPCM and differential encoding techniques
- **Delta modulation** — Fixed and adaptive step-size approaches
- **Signal processing** — PCM sampling, bit depth conversion, dynamic range compression

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).