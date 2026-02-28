namespace VideoScript.Tool.Services;

/// <summary>
/// Service for managing MLX model downloads
/// Supports both HuggingFace and ModelScope sources
/// </summary>
public class ModelDownloadService
{
    private readonly PythonEnvironmentService _pythonEnv;
    private readonly string _modelsDir;

    // Default model: Qwen3-ASR 1.7B with 8-bit quantization
    public const string DefaultModel = "mlx-community/Qwen3-ASR-1.7B-8bit";

    // HuggingFace mirror options
    public const string HuggingFaceMirror = "hf-mirror.com";

    public ModelDownloadService(PythonEnvironmentService pythonEnv)
    {
        _pythonEnv = pythonEnv;
        _modelsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".videoscript",
            "models");
    }

    /// <summary>
    /// Gets the path where models are stored
    /// </summary>
    public string ModelsDirectory => _modelsDir;

    /// <summary>
    /// Checks if a model is downloaded
    /// </summary>
    public async Task<bool> IsModelDownloadedAsync(string modelName, CancellationToken cancellationToken = default)
    {
        try
        {
            var script = $@"
import sys
import os

model_name = ""{modelName}""
model_dir = os.path.expanduser(""~/.videoscript/models"")
local_path = os.path.join(model_dir, model_name.replace(""/"", ""--""))

if os.path.exists(local_path):
    # Check for essential model files
    has_config = os.path.exists(os.path.join(local_path, ""config.json""))
    has_model = any(os.path.exists(os.path.join(local_path, f)) for f in [""model.safetensors"", ""model.safetensors.index.json""])
    if has_config and has_model:
        print(""EXISTS:"" + local_path)
    else:
        print(""NOT_CACHED"")
else:
    print(""NOT_CACHED"")
";

            var result = await RunPythonScriptAsync(script, null, cancellationToken);

            if (result.StartsWith("EXISTS:"))
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Downloads a model (tries ModelScope first, then HuggingFace)
    /// </summary>
    public async Task<string> DownloadModelAsync(
        string modelName,
        Action<string>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        progressCallback?.Invoke($"Checking model {modelName}...");

        // Check if already downloaded
        if (await IsModelDownloadedAsync(modelName, cancellationToken))
        {
            progressCallback?.Invoke("Model already downloaded.");
            return await GetModelPathAsync(modelName, cancellationToken);
        }

        progressCallback?.Invoke($"Downloading model {modelName}...");

        // Try ModelScope first (faster in China)
        var result = await TryDownloadFromModelScopeAsync(modelName, progressCallback, cancellationToken);

        if (result == null)
        {
            // Fallback to HuggingFace with mirror
            progressCallback?.Invoke("Trying HuggingFace mirror...");
            result = await TryDownloadFromHuggingFaceAsync(modelName, progressCallback, cancellationToken);
        }

        if (result != null)
        {
            progressCallback?.Invoke($"Model downloaded to {result}");
            return result;
        }

        throw new InvalidOperationException($"Failed to download model from all sources");
    }

    private async Task<string?> TryDownloadFromModelScopeAsync(
        string modelName,
        Action<string>? progressCallback,
        CancellationToken cancellationToken)
    {
        progressCallback?.Invoke("Trying ModelScope...");

        var script = $@"
import sys
import os

try:
    from modelscope import snapshot_download

    model_name = ""{modelName}""
    model_dir = os.path.expanduser(""~/.videoscript/models"")
    local_path = os.path.join(model_dir, model_name.replace(""/"", ""--""))

    os.makedirs(model_dir, exist_ok=True)

    cache_path = snapshot_download(
        model_id=model_name,
        local_dir=local_path,
        revision=""master""
    )
    print(""SUCCESS:"" + local_path)
except ImportError:
    print(""MODELSCOPE_NOT_INSTALLED"")
except Exception as e:
    print(""ERROR:"" + str(e))
";

        var result = await RunPythonScriptAsync(script, progressCallback, cancellationToken);

        if (result.StartsWith("SUCCESS:"))
        {
            return result.Substring("SUCCESS:".Length).Trim();
        }

        if (result.Contains("MODELSCOPE_NOT_INSTALLED"))
        {
            progressCallback?.Invoke("ModelScope not available, trying HuggingFace...");
        }

        return null;
    }

    private async Task<string?> TryDownloadFromHuggingFaceAsync(
        string modelName,
        Action<string>? progressCallback,
        CancellationToken cancellationToken)
    {
        progressCallback?.Invoke("Downloading from HuggingFace (using mirror)...");

        var script = $@"
import sys
import os

try:
    # Set HuggingFace mirror for faster download in China
    os.environ[""HF_ENDPOINT""] = ""https://hf-mirror.com""

    from huggingface_hub import snapshot_download

    model_name = ""{modelName}""
    model_dir = os.path.expanduser(""~/.videoscript/models"")
    local_path = os.path.join(model_dir, model_name.replace(""/"", ""--""))

    os.makedirs(model_dir, exist_ok=True)

    cache_path = snapshot_download(
        repo_id=model_name,
        local_dir=local_path,
        resume_download=True
    )
    print(""SUCCESS:"" + local_path)
except Exception as e:
    print(""ERROR:"" + str(e))
    sys.exit(1)
";

        var result = await RunPythonScriptAsync(script, progressCallback, cancellationToken);

        if (result.StartsWith("SUCCESS:"))
        {
            return result.Substring("SUCCESS:".Length).Trim();
        }

        return null;
    }

    /// <summary>
    /// Gets the local path to a downloaded model
    /// </summary>
    public async Task<string> GetModelPathAsync(string modelName, CancellationToken cancellationToken = default)
    {
        var modelDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".videoscript",
            "models",
            modelName.Replace("/", "--"));

        if (Directory.Exists(modelDir))
        {
            return modelDir;
        }

        throw new InvalidOperationException($"Model not found: {modelName}");
    }

    /// <summary>
    /// Lists available models
    /// </summary>
    public static IReadOnlyList<string> GetAvailableModels()
    {
        return new List<string>
        {
            "mlx-community/Qwen3-ASR-1.7B-8bit",
            "mlx-community/Qwen3-ASR-0.6B-4bit",
            "mlx-community/Qwen3-ASR-1.7B-4bit",
            "mlx-community/whisper-small-asr-6bit"
        }.AsReadOnly();
    }

    private async Task<string> RunPythonScriptAsync(string script, Action<string>? progressCallback, CancellationToken cancellationToken)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, script, cancellationToken);

            var (exitCode, output, error) = await RunProcessWithOutputAsync(
                _pythonEnv.PythonExecutable,
                $"\"{tempFile}\"",
                progressCallback,
                cancellationToken);

            if (exitCode != 0 && !output.Contains("SUCCESS"))
            {
                throw new InvalidOperationException($"Python script failed: {error}");
            }

            return output.Trim();
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
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
                // Real-time output for progress
                if (progressCallback != null && !e.Data.StartsWith("SUCCESS") && !e.Data.StartsWith("ERROR") && !e.Data.StartsWith("EXISTS"))
                {
                    progressCallback(e.Data);
                }
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                errorBuilder.AppendLine(e.Data);
                // Show download progress from stderr (tqdm outputs to stderr)
                if (progressCallback != null && !string.IsNullOrWhiteSpace(e.Data))
                {
                    // Filter out debug/info lines, show progress bars
                    var line = e.Data.Trim();
                    if (line.Contains("%") || line.Contains("Fetching") || line.Contains("Downloading") || line.Contains("it/s") || line.Contains("B/s"))
                    {
                        progressCallback(line);
                    }
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
