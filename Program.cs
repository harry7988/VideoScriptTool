using System.CommandLine;
using VideoScript.Tool.Commands;
using VideoScript.Tool.Services;

namespace VideoScript.Tool;

/// <summary>
/// VideoScript - A .NET tool for transcribing video and audio files using MLX
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        // Root command options (for direct usage: videoscript input.mp4)
        var inputArgument = new Argument<string?>(
            name: "input",
            description: "Path to the video or audio file to transcribe")
        {
            Arity = ArgumentArity.ZeroOrOne
        };

        var outputOption = new Option<string?>(
            aliases: new[] { "--output", "-o" },
            description: "Path to save the transcription text file");

        var modelOption = new Option<string?>(
            aliases: new[] { "--model", "-m" },
            description: "MLX model to use for transcription",
            getDefaultValue: () => ModelDownloadService.DefaultModel);

        var languageOption = new Option<string?>(
            aliases: new[] { "--language", "-l" },
            description: "Language code for transcription (e.g., en, zh, ja)");

        var forceInitOption = new Option<bool>(
            aliases: new[] { "--force-init", "-f" },
            description: "Force re-initialization of dependencies");

        var verboseOption = new Option<bool>(
            aliases: new[] { "--verbose", "-v" },
            description: "Enable verbose output");

        var rootCommand = new RootCommand(
            "VideoScript - Transcribe video and audio files using MLX and Qwen3-ASR model")
        {
            inputArgument,
            outputOption,
            modelOption,
            languageOption,
            forceInitOption,
            verboseOption,
            TranscribeCommand.Create(),
            CreateInitCommand(),
            CreateModelsCommand(),
            CreateStatusCommand()
        };

        rootCommand.SetHandler(async (context) =>
        {
            var input = context.ParseResult.GetValueForArgument(inputArgument);

            // If no input file provided, show help
            if (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine("VideoScript - Video/Audio Transcription Tool");
                Console.WriteLine();
                Console.WriteLine("Usage:");
                Console.WriteLine("  videoscript <input> [options]");
                Console.WriteLine();
                Console.WriteLine("Examples:");
                Console.WriteLine("  videoscript video.mp4");
                Console.WriteLine("  videoscript audio.mp3 -o output.txt");
                Console.WriteLine("  videoscript video.mp4 -m Qwen3-ASR-0.6B-4bit");
                Console.WriteLine("  videoscript video.mp4 -l zh");
                Console.WriteLine();
                Console.WriteLine("Commands:");
                Console.WriteLine("  init      Initialize VideoScript (install dependencies)");
                Console.WriteLine("  status    Check installation status");
                Console.WriteLine("  models    List available models");
                Console.WriteLine();
                Console.WriteLine("Run 'videoscript --help' for more information.");
                return;
            }

            // Direct transcription mode
            var output = context.ParseResult.GetValueForOption(outputOption);
            var model = context.ParseResult.GetValueForOption(modelOption);
            var language = context.ParseResult.GetValueForOption(languageOption);
            var forceInit = context.ParseResult.GetValueForOption(forceInitOption);
            var verbose = context.ParseResult.GetValueForOption(verboseOption);
            var cancellationToken = context.GetCancellationToken();

            var service = new MlxTranscriptionService();

            void ProgressCallback(string message)
            {
                if (verbose || message.StartsWith("Error") || message.Contains("complete"))
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
                }
                else
                {
                    Console.WriteLine(message);
                }
            }

            try
            {
                // Check if input file exists
                if (!File.Exists(input))
                {
                    Console.Error.WriteLine($"Error: Input file not found: {input}");
                    context.ExitCode = 1;
                    return;
                }

                // Force initialization if requested
                if (forceInit)
                {
                    ProgressCallback("Force re-initialization requested...");
                    await service.InitializeAsync(ProgressCallback, cancellationToken);
                }

                // Perform transcription
                var resultPath = await service.TranscribeAsync(
                    input,
                    output,
                    model,
                    language,
                    ProgressCallback,
                    cancellationToken);

                Console.WriteLine($"\nTranscription completed successfully!");
                Console.WriteLine($"Output saved to: {resultPath}");
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("\nOperation cancelled.");
                context.ExitCode = 130;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"\nError: {ex.Message}");
                if (verbose)
                {
                    Console.Error.WriteLine(ex.StackTrace);
                }
                context.ExitCode = 1;
            }
        });

        return await rootCommand.InvokeAsync(args);
    }

    private static Command CreateInitCommand()
    {
        var command = new Command("init", "Initialize VideoScript (install dependencies and download model)");

        command.SetHandler(async (context) =>
        {
            var cancellationToken = context.GetCancellationToken();
            var service = new MlxTranscriptionService();

            void ProgressCallback(string message)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
            }

            try
            {
                Console.WriteLine("Initializing VideoScript...");
                Console.WriteLine();

                await service.InitializeAsync(ProgressCallback, cancellationToken);

                Console.WriteLine();
                Console.WriteLine("Initialization complete! You can now use 'videoscript <input>' to transcribe.");
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("\nOperation cancelled.");
                context.ExitCode = 130;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"\nError: {ex.Message}");
                context.ExitCode = 1;
            }
        });

        return command;
    }

    private static Command CreateModelsCommand()
    {
        var command = new Command("models", "List available models for transcription");

        command.SetHandler(() =>
        {
            Console.WriteLine("Available Models:");
            Console.WriteLine();

            var models = ModelDownloadService.GetAvailableModels();
            foreach (var model in models)
            {
                var isDefault = model == ModelDownloadService.DefaultModel;
                Console.WriteLine($"  {model}{(isDefault ? " (default)" : "")}");
            }

            Console.WriteLine();
            Console.WriteLine("Use --model option to specify a model:");
            Console.WriteLine("  videoscript input.mp4 --model mlx-community/Qwen3-ASR-0.6B-4bit");
        });

        return command;
    }

    private static Command CreateStatusCommand()
    {
        var command = new Command("status", "Check VideoScript installation status");

        command.SetHandler(async (context) =>
        {
            var cancellationToken = context.GetCancellationToken();

            Console.WriteLine("VideoScript Status Check");
            Console.WriteLine("========================");
            Console.WriteLine();

            // Check FFmpeg
            var ffmpegService = new FFmpegService();
            var ffmpegInstalled = await ffmpegService.IsInstalledAsync(cancellationToken);
            PrintStatus("FFmpeg", ffmpegInstalled, "Required for video processing");

            // Check Python
            var pythonEnv = new PythonEnvironmentService();
            var pythonInstalled = await pythonEnv.IsPythonInstalledAsync(cancellationToken);
            PrintStatus("Python 3.10+", pythonInstalled, "Required for MLX");

            // Check venv
            var venvReady = pythonEnv.IsVenvInitialized();
            PrintStatus("Virtual Environment", venvReady, "Isolated Python environment");

            // Check dependencies
            var depsInstalled = false;
            if (venvReady)
            {
                depsInstalled = await pythonEnv.AreDependenciesInstalledAsync(cancellationToken);
            }
            PrintStatus("MLX Dependencies", depsInstalled, "mlx-audio, huggingface_hub");

            // Check model
            var modelService = new ModelDownloadService(pythonEnv);
            var modelDownloaded = false;
            if (depsInstalled)
            {
                modelDownloaded = await modelService.IsModelDownloadedAsync(
                    ModelDownloadService.DefaultModel, cancellationToken);
            }
            PrintStatus("ASR Model", modelDownloaded, ModelDownloadService.DefaultModel);

            Console.WriteLine();
            Console.WriteLine($"Overall Status: {(ffmpegInstalled && pythonInstalled && venvReady && depsInstalled && modelDownloaded ? "Ready ✓" : "Needs Initialization")}");

            if (!ffmpegInstalled || !pythonInstalled || !venvReady || !depsInstalled || !modelDownloaded)
            {
                Console.WriteLine();
                Console.WriteLine("Run 'videoscript init' to set up VideoScript.");
            }
        });

        return command;
    }

    private static void PrintStatus(string component, bool isInstalled, string description)
    {
        var status = isInstalled ? "✓ Installed" : "✗ Not Installed";
        var color = isInstalled ? ConsoleColor.Green : ConsoleColor.Red;

        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write($"  {status}");
        Console.ForegroundColor = originalColor;
        Console.WriteLine($" - {component}");
        Console.WriteLine($"    {description}");
    }
}
