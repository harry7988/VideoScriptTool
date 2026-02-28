namespace VideoScript.Tool.Services;

/// <summary>
/// Interface for transcription services
/// </summary>
public interface ITranscriptionService
{
    /// <summary>
    /// Transcribes a video or audio file to text
    /// </summary>
    /// <param name="inputPath">Path to the input video or audio file</param>
    /// <param name="outputPath">Optional path for the output text file</param>
    /// <param name="modelName">Optional model name to use for transcription</param>
    /// <param name="language">Optional language code for transcription</param>
    /// <param name="progressCallback">Optional callback for progress updates</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Path to the output text file</returns>
    Task<string> TranscribeAsync(
        string inputPath,
        string? outputPath = null,
        string? modelName = null,
        string? language = null,
        Action<string>? progressCallback = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the transcription service is properly initialized
    /// </summary>
    Task<bool> IsInitializedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Initializes the transcription service (installs dependencies, downloads model, etc.)
    /// </summary>
    Task InitializeAsync(Action<string>? progressCallback = null, CancellationToken cancellationToken = default);
}
