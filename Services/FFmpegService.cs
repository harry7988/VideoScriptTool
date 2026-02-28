namespace VideoScript.Tool.Services;

/// <summary>
/// Service for managing FFmpeg installation and operations
/// </summary>
public class FFmpegService
{
    private const string FFmpegExecutable = "ffmpeg";
    private const string FFprobeExecutable = "ffprobe";

    /// <summary>
    /// Checks if FFmpeg is installed and available in PATH
    /// </summary>
    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await RunCommandAsync("which", "ffmpeg", cancellationToken);
            return !string.IsNullOrWhiteSpace(result);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Installs FFmpeg using Homebrew on macOS
    /// </summary>
    public async Task InstallAsync(Action<string>? progressCallback = null, CancellationToken cancellationToken = default)
    {
        if (await IsInstalledAsync(cancellationToken))
        {
            progressCallback?.Invoke("FFmpeg is already installed.");
            return;
        }

        progressCallback?.Invoke("Installing FFmpeg via Homebrew...");

        // Check if Homebrew is installed
        var brewCheck = await RunCommandAsync("which", "brew", cancellationToken);
        if (string.IsNullOrWhiteSpace(brewCheck))
        {
            throw new InvalidOperationException(
                "Homebrew is not installed. Please install Homebrew first: https://brew.sh");
        }

        // Install FFmpeg
        var (exitCode, output, error) = await RunProcessWithOutputAsync("brew", "install ffmpeg", cancellationToken);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Failed to install FFmpeg: {error}");
        }

        progressCallback?.Invoke("FFmpeg installed successfully.");
    }

    /// <summary>
    /// Gets the duration of a media file in seconds
    /// </summary>
    public async Task<double> GetDurationAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var args = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"";
        var result = await RunCommandAsync(FFprobeExecutable, args, cancellationToken);

        if (double.TryParse(result.Trim(), out var duration))
        {
            return duration;
        }

        throw new InvalidOperationException($"Failed to get duration for file: {filePath}");
    }

    /// <summary>
    /// Extracts audio from a video file
    /// </summary>
    public async Task ExtractAudioAsync(
        string inputPath,
        string outputPath,
        Action<string>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var args = $"-i \"{inputPath}\" -vn -acodec pcm_s16le -ar 16000 -ac 1 \"{outputPath}\" -y";
        progressCallback?.Invoke($"Extracting audio from {inputPath}...");

        var (exitCode, _, error) = await RunProcessWithOutputAsync(FFmpegExecutable, args, cancellationToken);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Failed to extract audio: {error}");
        }

        progressCallback?.Invoke($"Audio extracted to {outputPath}");
    }

    /// <summary>
    /// Checks if a file is a video file based on extension
    /// </summary>
    public static bool IsVideoFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var videoExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v" };
        return videoExtensions.Contains(extension);
    }

    /// <summary>
    /// Checks if a file is an audio file based on extension
    /// </summary>
    public static bool IsAudioFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var audioExtensions = new[] { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma" };
        return audioExtensions.Contains(extension);
    }

    private static async Task<string> RunCommandAsync(string command, string args, CancellationToken cancellationToken)
    {
        var (exitCode, output, error) = await RunProcessWithOutputAsync(command, args, cancellationToken);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Command failed: {error}");
        }

        return output;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunProcessWithOutputAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<(int, string, string)>();

        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        var outputBuilder = new System.Text.StringBuilder();
        var errorBuilder = new System.Text.StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
                outputBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
                errorBuilder.AppendLine(e.Data);
        };

        process.Exited += (sender, e) =>
        {
            tcs.TrySetResult((process.ExitCode, outputBuilder.ToString(), errorBuilder.ToString()));
            process.Dispose();
        };

        cancellationToken.Register(() =>
        {
            try
            {
                process.Kill();
            }
            catch { }
            tcs.TrySetCanceled(cancellationToken);
        });

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return await tcs.Task;
    }
}
