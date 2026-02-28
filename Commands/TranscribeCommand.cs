using System.CommandLine;
using VideoScript.Tool.Services;

namespace VideoScript.Tool.Commands;

/// <summary>
/// The transcribe command for converting video/audio to text
/// </summary>
public static class TranscribeCommand
{
    public static Command Create()
    {
        // Input file argument
        var inputArgument = new Argument<string>(
            name: "input",
            description: "Path to the video or audio file to transcribe");

        // Output file option
        var outputOption = new Option<string?>(
            aliases: new[] { "--output", "-o" },
            description: "Path to save the transcription text file");

        // Model option
        var modelOption = new Option<string?>(
            aliases: new[] { "--model", "-m" },
            description: "MLX model to use for transcription",
            getDefaultValue: () => ModelDownloadService.DefaultModel);

        // Language option
        var languageOption = new Option<string?>(
            aliases: new[] { "--language", "-l" },
            description: "Language code for transcription (e.g., en, zh, ja)");

        // Force init option
        var forceInitOption = new Option<bool>(
            aliases: new[] { "--force-init", "-f" },
            description: "Force re-initialization of dependencies");

        // Verbose option
        var verboseOption = new Option<bool>(
            aliases: new[] { "--verbose", "-v" },
            description: "Enable verbose output");

        var command = new Command("transcribe", "Transcribe a video or audio file to text")
        {
            inputArgument,
            outputOption,
            modelOption,
            languageOption,
            forceInitOption,
            verboseOption
        };

        command.SetHandler(async (context) =>
        {
            var input = context.ParseResult.GetValueForArgument(inputArgument);
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

        return command;
    }
}
