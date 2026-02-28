# VideoScript

A powerful .NET Global Tool for transcribing video and audio files to text using Apple Silicon GPU acceleration.

## Features

- **Automatic Video/Audio Detection**: Automatically detects input file type and extracts audio from video files
- **GPU Acceleration**: Uses MLX-Audio framework with Apple Silicon GPU for fast transcription
- **Qwen3-ASR Model**: High-quality speech recognition supporting multiple languages
- **Smart Paragraph Formatting**: Automatically formats text with line breaks at natural pause points
- **First-Run Setup**: Automatically installs FFmpeg, creates Python virtual environment, and downloads the model
- **ModelScope Support**: Faster model downloads in China region

## Installation

```bash
dotnet tool install -g VideoScript
```

## Usage

### Basic Usage

```bash
# Transcribe a video file
videoscript input.mp4

# Transcribe an audio file
videoscript input.mp3

# Specify output file
videoscript input.mp4 -o output.txt

# Specify language (zh, en, etc.)
videoscript input.mp4 --language zh
```

### Advanced Options

```bash
# Use a different model
videoscript input.mp4 -m mlx-community/Qwen3-ASR-0.6B-4bit

# Disable text formatting
videoscript input.mp4 --no-format

# Verbose output
videoscript input.mp4 --verbose

# Force re-initialization
videoscript input.mp4 --force-init
```

### Utility Commands

```bash
# Show help
videoscript --help

# Show version
videoscript --version

# Check environment status
videoscript status

# List available models
videoscript models
```

## Requirements

- **Operating System**: macOS with Apple Silicon (M1/M2/M3/M4)
- **Runtime**: .NET 8.0
- **Python**: Python 3.10+ (automatically configured)
- **Package Manager**: Homebrew (for FFmpeg auto-installation)

## First Run

On first run, VideoScript will automatically:

1. Install FFmpeg via Homebrew (if not already installed)
2. Create a Python virtual environment in `~/.videoscript/venv`
3. Install required Python packages (mlx-audio, huggingface_hub, modelscope)
4. Download the Qwen3-ASR-1.7B-8bit model (~2.5GB) to `~/.videoscript/models/`

This initialization process may take several minutes depending on your internet connection.

## Output Format

The transcription output is automatically formatted with:
- Line breaks at sentence boundaries (based on Chinese and English punctuation)
- Paragraphs grouped by ~150 characters or 2+ sentences

Example output:
```
欢迎回到 Google 与 Kaggle 联合推出的 5-day AI Agents Intensive 白皮书解读。
前面三天我们已经打好了基础。
Day 1，我们定义了 agent 的架构基石。Day 2，通过 MCP 协议让 agent 学会了行动。
```

## Available Models

- `mlx-community/Qwen3-ASR-1.7B-8bit` (default) - Best quality
- `mlx-community/Qwen3-ASR-1.7B-4bit` - Smaller memory footprint
- `mlx-community/Qwen3-ASR-0.6B-4bit` - Fastest, lowest memory
- `mlx-community/whisper-small-asr-6bit` - Alternative model

## Performance

On Apple Silicon M-series chips:
- Audio extraction: ~2 seconds for a 10-minute video
- Transcription: ~60 seconds for a 10-minute audio (using 1.7B model)

## License

MIT License

## Support

For issues and feature requests, please visit: https://github.com/videoscript/videoscript/issues
