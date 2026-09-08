using System.Diagnostics;
using System.IO.Compression;
using Accounts.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace Accounts.Services.Services;

public sealed class LibreOfficeFileConversionService : IFileConversionService, IDisposable
{
    private const string EngineName = "LibreOffice";
    private readonly FileConversionOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<LibreOfficeFileConversionService> _logger;
    private readonly SemaphoreSlim _conversionSlots;
    private readonly string? _executablePath;

    /// <summary>
    /// High-fidelity LibreOffice pairs. PDF→Excel stays on the specialized browser table extractor.
    /// PDF→Word uses Writer PDF import filter for much closer layout fidelity.
    /// </summary>
    private static readonly IReadOnlyList<FileConversionPair> HighFidelityPairs =
    [
        new("word", "pdf", "high-fidelity"),
        new("excel", "pdf", "high-fidelity"),
        new("word", "excel", "high-fidelity"),
        new("excel", "word", "high-fidelity"),
        new("pdf", "word", "high-fidelity"),
    ];

    private static readonly Dictionary<(string Source, string Target), string> LibreOfficeFilters = new()
    {
        [("word", "pdf")] = "pdf",
        [("excel", "pdf")] = "pdf",
        [("word", "excel")] = "xlsx",
        [("excel", "word")] = "docx",
        [("pdf", "word")] = "docx",
    };

    public LibreOfficeFileConversionService(
        IOptions<FileConversionOptions> options,
        IWebHostEnvironment environment,
        ILogger<LibreOfficeFileConversionService> logger)
    {
        _options = options.Value;
        _environment = environment;
        _logger = logger;
        _conversionSlots = new SemaphoreSlim(
            Math.Max(1, _options.MaximumConcurrentConversions),
            Math.Max(1, _options.MaximumConcurrentConversions));
        _executablePath = ResolveExecutablePath(_options.LibreOfficeExecutablePath);
    }

    public FileConversionCapabilities GetCapabilities()
    {
        var available = _options.Enabled && _executablePath != null;
        var reason = available
            ? null
            : !_options.Enabled
                ? "High-fidelity conversion is disabled by server configuration."
                : "LibreOffice is not installed or its executable path is not configured.";

        return new FileConversionCapabilities(
            available,
            EngineName,
            Math.Max(1, _options.MaximumFileSizeMb) * 1024L * 1024L,
            available ? HighFidelityPairs : Array.Empty<FileConversionPair>(),
            reason);
    }

    public async Task<ConvertedFile> ConvertAsync(
        IFormFile sourceFile,
        string targetFormat,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || _executablePath == null)
            throw new FileConversionException(GetCapabilities().UnavailableReason!);

        if (sourceFile.Length <= 0)
            throw new FileConversionException("Select a non-empty file to convert.");

        var maximumBytes = Math.Max(1, _options.MaximumFileSizeMb) * 1024L * 1024L;
        if (sourceFile.Length > maximumBytes)
            throw new FileConversionException($"The selected file exceeds the {_options.MaximumFileSizeMb} MB limit.");

        var extension = Path.GetExtension(sourceFile.FileName).ToLowerInvariant();
        var sourceFormat = extension switch
        {
            ".docx" => "word",
            ".xls" or ".xlsx" => "excel",
            ".pdf" => "pdf",
            _ => null
        };
        var normalizedTarget = targetFormat.Trim().ToLowerInvariant();

        if (sourceFormat == null)
            throw new FileConversionException("Supported inputs are PDF, Word (.docx), and Excel (.xls or .xlsx). Legacy Word .doc is not accepted.");

        if (sourceFormat == "pdf" && normalizedTarget == "excel")
            throw new FileConversionException("PDF to Excel uses the specialized browser table extractor for better column detection.");

        if (!LibreOfficeFilters.TryGetValue((sourceFormat, normalizedTarget), out var convertFilter))
            throw new FileConversionException("This conversion path is not available on the high-fidelity server. Use browser reconstruction.");

        var outputExtension = normalizedTarget switch
        {
            "pdf" => ".pdf",
            "word" => ".docx",
            "excel" => ".xlsx",
            _ => throw new FileConversionException("Unsupported output format.")
        };
        var contentType = normalizedTarget switch
        {
            "pdf" => "application/pdf",
            "word" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "excel" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream"
        };

        var workRoot = Path.Combine(
            _environment.ContentRootPath,
            "App_Data",
            "file-conversion-temp",
            Guid.NewGuid().ToString("N"));
        var inputDirectory = Path.Combine(workRoot, "input");
        var outputDirectory = Path.Combine(workRoot, "output");
        var profileDirectory = Path.Combine(workRoot, "profile");
        Directory.CreateDirectory(inputDirectory);
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(profileDirectory);

        var safeBaseName = SanitizeBaseName(Path.GetFileNameWithoutExtension(sourceFile.FileName));
        var inputPath = Path.Combine(inputDirectory, safeBaseName + extension);

        await _conversionSlots.WaitAsync(cancellationToken);
        try
        {
            await using (var stream = new FileStream(
                inputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await sourceFile.CopyToAsync(stream, cancellationToken);
            }

            await ValidateSignatureAsync(inputPath, extension, cancellationToken);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(10, _options.TimeoutSeconds)));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            var result = await ExecuteLibreOfficeAsync(
                inputPath,
                outputDirectory,
                profileDirectory,
                convertFilter,
                sourceFormat == "pdf" ? "writer_pdf_import" : null,
                linked.Token);

            if (result.ExitCode != 0)
            {
                _logger.LogWarning("LibreOffice conversion failed with exit code {ExitCode}. Error: {Error}", result.ExitCode, result.Error);
                throw new FileConversionException("The document engine could not convert this file. The file may be encrypted, damaged, or use unsupported features.");
            }

            var outputPath = Directory.EnumerateFiles(outputDirectory, "*" + outputExtension, SearchOption.TopDirectoryOnly)
                .OrderByDescending(path => new FileInfo(path).LastWriteTimeUtc)
                .FirstOrDefault();
            if (outputPath == null)
                throw new FileConversionException($"The document engine did not produce a {normalizedTarget.ToUpperInvariant()} output.");

            var outputInfo = new FileInfo(outputPath);
            var maximumOutputBytes = Math.Max(1, _options.MaximumOutputSizeMb) * 1024L * 1024L;
            if (outputInfo.Length <= 4 || outputInfo.Length > maximumOutputBytes)
                throw new FileConversionException("The converted output is empty or exceeds the safe output-size limit.");

            await ValidateOutputAsync(outputPath, outputExtension, cancellationToken);
            var bytes = await File.ReadAllBytesAsync(outputPath, cancellationToken);
            return new ConvertedFile(
                bytes,
                safeBaseName + outputExtension,
                contentType,
                EngineName,
                "high-fidelity");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FileConversionException($"Conversion exceeded the {_options.TimeoutSeconds}-second time limit.");
        }
        finally
        {
            _conversionSlots.Release();
            TryDeleteDirectory(workRoot);
        }
    }

    private async Task<(int ExitCode, string Output, string Error)> ExecuteLibreOfficeAsync(
        string inputPath,
        string outputDirectory,
        string profileDirectory,
        string convertFilter,
        string? importFilter,
        CancellationToken cancellationToken)
    {
        var profileUri = new Uri(profileDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).AbsoluteUri;
        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = outputDirectory
        };
        startInfo.ArgumentList.Add("--headless");
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("--nodefault");
        startInfo.ArgumentList.Add("--norestore");
        startInfo.ArgumentList.Add($"-env:UserInstallation={profileUri}");
        // Writer PDF import keeps much more of the original page layout than a plain convert.
        if (!string.IsNullOrWhiteSpace(importFilter))
            startInfo.ArgumentList.Add($"--infilter={importFilter}");
        startInfo.ArgumentList.Add("--convert-to");
        startInfo.ArgumentList.Add(convertFilter);
        startInfo.ArgumentList.Add("--outdir");
        startInfo.ArgumentList.Add(outputDirectory);
        startInfo.ArgumentList.Add(inputPath);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new FileConversionException("The document conversion engine could not be started.");

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception exception) { _logger.LogDebug(exception, "Could not terminate timed-out LibreOffice process."); }
            }
            throw;
        }

        return (process.ExitCode, await outputTask, await errorTask);
    }

    private static async Task ValidateSignatureAsync(string path, string extension, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var header = new byte[8];
        var read = await stream.ReadAsync(header, cancellationToken);
        stream.Position = 0;

        if (extension == ".pdf")
        {
            if (read < 5 || header[0] != (byte)'%' || header[1] != (byte)'P' || header[2] != (byte)'D' || header[3] != (byte)'F' || header[4] != (byte)'-')
                throw new FileConversionException("The file extension says PDF, but its content is not a valid PDF.");
            return;
        }

        if (extension == ".xls")
        {
            byte[] ole = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
            if (read < ole.Length || !header.SequenceEqual(ole))
                throw new FileConversionException("The file extension says Excel, but its content is not a valid legacy workbook.");
            return;
        }

        if (read < 4 || header[0] != (byte)'P' || header[1] != (byte)'K')
            throw new FileConversionException("The selected Office document has an invalid file signature.");

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var requiredEntry = extension == ".docx" ? "word/document.xml" : "xl/workbook.xml";
        if (archive.GetEntry("[Content_Types].xml") == null || archive.GetEntry(requiredEntry) == null)
            throw new FileConversionException("The selected Office document is damaged or does not match its extension.");
    }

    private static async Task ValidateOutputAsync(string outputPath, string outputExtension, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(outputPath);
        var header = new byte[8];
        var read = await stream.ReadAsync(header, cancellationToken);

        if (outputExtension == ".pdf")
        {
            if (read < 5 || header[0] != (byte)'%' || header[1] != (byte)'P' || header[2] != (byte)'D' || header[3] != (byte)'F' || header[4] != (byte)'-')
                throw new FileConversionException("The document engine produced an invalid PDF output.");
            return;
        }

        if (read < 4 || header[0] != (byte)'P' || header[1] != (byte)'K')
            throw new FileConversionException("The document engine produced an invalid Office document.");
    }

    private static string SanitizeBaseName(string value)
    {
        var safe = new string(value
            .Where(character => !Path.GetInvalidFileNameChars().Contains(character))
            .Take(100)
            .ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "converted-document" : safe;
    }

    private static string? ResolveExecutablePath(string? configuredPath)
    {
        var candidates = new List<string?>
        {
            configuredPath,
            Environment.GetEnvironmentVariable("LIBREOFFICE_PATH"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program", "soffice.com"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LibreOffice", "program", "soffice.com"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program", "soffice.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LibreOffice", "program", "soffice.exe"),
            OperatingSystem.IsWindows() ? null : "/usr/bin/libreoffice",
            OperatingSystem.IsWindows() ? null : "/usr/bin/soffice"
        };

        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not remove temporary conversion directory {Path}.", path);
        }
    }

    public void Dispose() => _conversionSlots.Dispose();
}
