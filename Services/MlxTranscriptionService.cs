namespace VideoScript.Tool.Services;

/// <summary>
/// MLX-based transcription service implementation
/// </summary>
public class MlxTranscriptionService : ITranscriptionService
{
    private readonly PythonEnvironmentService _pythonEnv;
    private readonly FFmpegService _ffmpegService;
    private readonly ModelDownloadService _modelService;
    private bool _isInitialized;

    public MlxTranscriptionService()
    {
        _pythonEnv = new PythonEnvironmentService();
        _ffmpegService = new FFmpegService();
        _modelService = new ModelDownloadService(_pythonEnv);
    }

    /// <inheritdoc />
    public async Task<bool> IsInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            return true;
        }

        // Check all dependencies
        var ffmpegInstalled = await _ffmpegService.IsInstalledAsync(cancellationToken);
        var pythonReady = _pythonEnv.IsVenvInitialized() && await _pythonEnv.AreDependenciesInstalledAsync(cancellationToken);
        var modelDownloaded = await _modelService.IsModelDownloadedAsync(ModelDownloadService.DefaultModel, cancellationToken);

        _isInitialized = ffmpegInstalled && pythonReady && modelDownloaded;
        return _isInitialized;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(Action<string>? progressCallback = null, CancellationToken cancellationToken = default)
    {
        progressCallback?.Invoke("Initializing VideoScript...");

        // Step 1: Install FFmpeg
        await _ffmpegService.InstallAsync(progressCallback, cancellationToken);

        // Step 2: Initialize Python environment
        await _pythonEnv.InitializeAsync(progressCallback, cancellationToken);

        // Step 3: Download model
        await _modelService.DownloadModelAsync(ModelDownloadService.DefaultModel, progressCallback, cancellationToken);

        _isInitialized = true;
        progressCallback?.Invoke("VideoScript initialized successfully!");
    }

    /// <inheritdoc />
    public async Task<string> TranscribeAsync(
        string inputPath,
        string? outputPath = null,
        string? modelName = null,
        string? language = null,
        Action<string>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        // Validate input file
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException($"Input file not found: {inputPath}");
        }

        // Auto-initialize if needed
        if (!await IsInitializedAsync(cancellationToken))
        {
            await InitializeAsync(progressCallback, cancellationToken);
        }

        // Determine output path
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            var inputDir = Path.GetDirectoryName(inputPath) ?? Directory.GetCurrentDirectory();
            var inputFileName = Path.GetFileNameWithoutExtension(inputPath);
            outputPath = Path.Combine(inputDir, $"{inputFileName}.txt");
        }

        // Determine file type and prepare audio
        string audioPath;
        bool needsCleanup = false;

        if (FFmpegService.IsVideoFile(inputPath))
        {
            progressCallback?.Invoke("Detected video file, extracting audio...");
            audioPath = Path.Combine(Path.GetTempPath(), $"videoscript_{Guid.NewGuid()}.wav");
            await _ffmpegService.ExtractAudioAsync(inputPath, audioPath, progressCallback, cancellationToken);
            needsCleanup = true;
        }
        else if (FFmpegService.IsAudioFile(inputPath))
        {
            progressCallback?.Invoke("Detected audio file.");
            audioPath = inputPath;
        }
        else
        {
            throw new NotSupportedException($"Unsupported file format: {Path.GetExtension(inputPath)}");
        }

        try
        {
            // Use specified model or default
            var model = modelName ?? ModelDownloadService.DefaultModel;

            // Download model if not cached
            var modelPath = await _modelService.DownloadModelAsync(model, progressCallback, cancellationToken);

            progressCallback?.Invoke("Starting transcription...");

            // Run Python transcription script
            var result = await RunTranscriptionAsync(
                audioPath,
                outputPath!,
                modelPath,
                language,
                progressCallback,
                cancellationToken);

            progressCallback?.Invoke($"Transcription complete: {outputPath}");
            return outputPath!;
        }
        finally
        {
            // Cleanup temporary audio file if created
            if (needsCleanup && File.Exists(audioPath))
            {
                try
                {
                    File.Delete(audioPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    private async Task<string> RunTranscriptionAsync(
        string audioPath,
        string outputPath,
        string modelPath,
        string? language,
        Action<string>? progressCallback,
        CancellationToken cancellationToken)
    {
        var scriptPath = _pythonEnv.GetTranscribeScriptPath();

        var args = new List<string>
        {
            $"\"{scriptPath}\"",
            $"--audio \"{audioPath}\"",
            $"--output \"{outputPath}\"",
            $"--model \"{modelPath}\""
        };

        if (!string.IsNullOrWhiteSpace(language))
        {
            args.Add($"--language \"{language}\"");
        }

        var (exitCode, output, error) = await RunProcessWithOutputAsync(
            _pythonEnv.PythonExecutable,
            string.Join(" ", args),
            progressCallback,
            cancellationToken);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Transcription failed: {error}");
        }

        return output;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunProcessWithOutputAsync(
        string fileName,
        string arguments,
        Action<string>? progressCallback,
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
            {
                outputBuilder.AppendLine(e.Data);
                progressCallback?.Invoke(e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                errorBuilder.AppendLine(e.Data);
                // Some progress info comes through stderr
                if (!e.Data.Contains("Error") && !e.Data.Contains("Exception"))
                {
                    progressCallback?.Invoke(e.Data);
                }
            }
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
