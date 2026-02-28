namespace VideoScript.Tool.Services;

/// <summary>
/// Service for managing Python environment and dependencies
/// </summary>
public class PythonEnvironmentService
{
    private readonly string _venvPath;
    private readonly string _pythonDir;
    private string? _pythonExecutable;
    private string? _pipExecutable;

    public PythonEnvironmentService()
    {
        _pythonDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".videoscript");
        _venvPath = Path.Combine(_pythonDir, "venv");
    }

    /// <summary>
    /// Gets the path to the Python executable in the virtual environment
    /// </summary>
    public string PythonExecutable
    {
        get
        {
            if (_pythonExecutable == null)
            {
                throw new InvalidOperationException("Python environment not initialized. Call InitializeAsync first.");
            }
            return _pythonExecutable;
        }
    }

    /// <summary>
    /// Gets the path to pip in the virtual environment
    /// </summary>
    public string PipExecutable
    {
        get
        {
            if (_pipExecutable == null)
            {
                throw new InvalidOperationException("Python environment not initialized. Call InitializeAsync first.");
            }
            return _pipExecutable;
        }
    }

    /// <summary>
    /// Gets the path to the Python scripts directory in the virtual environment
    /// </summary>
    public string ScriptsPath => Path.Combine(_venvPath, "bin");

    /// <summary>
    /// Checks if Python 3.10+ is installed on the system
    /// </summary>
    public async Task<bool> IsPythonInstalledAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Try python3 first (common on macOS/Linux)
            var python3Version = await GetPythonVersionAsync("python3", cancellationToken);
            if (python3Version != null && python3Version >= new Version(3, 10))
            {
                return true;
            }

            // Try python
            var pythonVersion = await GetPythonVersionAsync("python", cancellationToken);
            if (pythonVersion != null && pythonVersion >= new Version(3, 10))
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
    /// Checks if the virtual environment is initialized
    /// </summary>
    public bool IsVenvInitialized()
    {
        var pythonPath = Path.Combine(_venvPath, "bin", "python3");
        return File.Exists(pythonPath);
    }

    /// <summary>
    /// Checks if dependencies are installed
    /// </summary>
    public async Task<bool> AreDependenciesInstalledAsync(CancellationToken cancellationToken = default)
    {
        if (!IsVenvInitialized())
        {
            return false;
        }

        try
        {
            var venvPython = Path.Combine(_venvPath, "bin", "python3");
            var args = "-c \"import mlx_audio; import huggingface_hub; print('ok')\"";
            var (exitCode, _, _) = await RunProcessWithOutputAsync(venvPython, args, cancellationToken);
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Initializes the Python environment
    /// </summary>
    public async Task InitializeAsync(Action<string>? progressCallback = null, CancellationToken cancellationToken = default)
    {
        // Check if Python is installed
        if (!await IsPythonInstalledAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "Python 3.10 or higher is required. Please install Python: https://www.python.org/downloads/");
        }

        progressCallback?.Invoke("Setting up Python environment...");

        // Create directory if it doesn't exist
        if (!Directory.Exists(_pythonDir))
        {
            Directory.CreateDirectory(_pythonDir);
        }

        // Create virtual environment if it doesn't exist
        if (!IsVenvInitialized())
        {
            progressCallback?.Invoke("Creating Python virtual environment...");
            await CreateVirtualEnvironmentAsync(cancellationToken);
        }

        // Set the executable paths
        _pythonExecutable = Path.Combine(_venvPath, "bin", "python3");
        _pipExecutable = Path.Combine(_venvPath, "bin", "pip");

        // Install dependencies
        if (!await AreDependenciesInstalledAsync(cancellationToken))
        {
            progressCallback?.Invoke("Installing Python dependencies...");
            await InstallDependenciesAsync(progressCallback, cancellationToken);
        }

        progressCallback?.Invoke("Python environment ready.");
    }

    /// <summary>
    /// Gets the path to the transcribe.py script
    /// </summary>
    public string GetTranscribeScriptPath()
    {
        // First, check in the same directory as the assembly
        var assemblyPath = AppDomain.CurrentDomain.BaseDirectory;
        var scriptPath = Path.Combine(assemblyPath, "python", "transcribe.py");

        if (File.Exists(scriptPath))
        {
            return scriptPath;
        }

        // For development, check relative to the project directory
        var devPath = Path.Combine(Directory.GetCurrentDirectory(), "python", "transcribe.py");
        if (File.Exists(devPath))
        {
            return devPath;
        }

        throw new FileNotFoundException("Could not find transcribe.py script");
    }

    private async Task CreateVirtualEnvironmentAsync(CancellationToken cancellationToken)
    {
        // Find the system python3
        var systemPython = await FindPython3Async(cancellationToken);

        var (exitCode, _, error) = await RunProcessWithOutputAsync(
            systemPython,
            $"-m venv \"{_venvPath}\"",
            cancellationToken);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Failed to create virtual environment: {error}");
        }
    }

    private async Task InstallDependenciesAsync(Action<string>? progressCallback, CancellationToken cancellationToken)
    {
        // Find requirements.txt
        var assemblyPath = AppDomain.CurrentDomain.BaseDirectory;
        var requirementsPath = Path.Combine(assemblyPath, "python", "requirements.txt");

        if (!File.Exists(requirementsPath))
        {
            var devPath = Path.Combine(Directory.GetCurrentDirectory(), "python", "requirements.txt");
            if (File.Exists(devPath))
            {
                requirementsPath = devPath;
            }
            else
            {
                throw new FileNotFoundException("Could not find requirements.txt");
            }
        }

        var pipPath = Path.Combine(_venvPath, "bin", "pip");

        progressCallback?.Invoke("Upgrading pip...");
        var (upgradeExit, _, upgradeError) = await RunProcessWithOutputAsync(
            pipPath,
            "install --upgrade pip",
            cancellationToken);

        if (upgradeExit != 0)
        {
            progressCallback?.Invoke($"Warning: Failed to upgrade pip: {upgradeError}");
        }

        progressCallback?.Invoke("Installing mlx-audio and dependencies (this may take a few minutes)...");
        var (exitCode, output, error) = await RunProcessWithOutputAsync(
            pipPath,
            $"install -r \"{requirementsPath}\"",
            cancellationToken);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Failed to install dependencies: {error}");
        }
    }

    private async Task<string> FindPython3Async(CancellationToken cancellationToken)
    {
        // Try common paths
        var candidates = new[] { "python3", "/usr/bin/python3", "/usr/local/bin/python3" };

        foreach (var candidate in candidates)
        {
            var version = await GetPythonVersionAsync(candidate, cancellationToken);
            if (version != null && version >= new Version(3, 10))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not find Python 3.10+ installation");
    }

    private static async Task<Version?> GetPythonVersionAsync(string pythonPath, CancellationToken cancellationToken)
    {
        try
        {
            var (exitCode, output, _) = await RunProcessWithOutputAsync(
                pythonPath,
                "--version",
                cancellationToken);

            if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            // Parse version from output like "Python 3.11.0"
            var parts = output.Trim().Split(' ');
            if (parts.Length >= 2 && Version.TryParse(parts[1], out var version))
            {
                return version;
            }

            return null;
        }
        catch
        {
            return null;
        }
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
