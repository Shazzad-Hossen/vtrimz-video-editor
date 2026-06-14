using System.Diagnostics;
using System.IO;
using System.Text;
using FFMpegCore;
using Vtrimz.Helpers;
using Vtrimz.Models;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace Vtrimz.Services;

public static class ExportService
{
    private static readonly SemaphoreSlim SetupLock = new(1, 1);
    private static string _ffmpegDir = string.Empty;
    private static bool _ffmpegReady;

    public static async Task ExportAsync(
        IReadOnlyList<TimelineClip> clips,
        string outputPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var ordered = TimelineMapper.ExportClips(clips).ToList();
        if (ordered.Count == 0)
            throw new InvalidOperationException("No clips to export.");

        progress?.Report("Preparing encoder...");
        await EnsureFfmpegAsync(cancellationToken);

        var tempDir = CreateShortTempDir();
        Directory.CreateDirectory(tempDir);

        try
        {
            var segments = new List<string>();

            for (var i = 0; i < ordered.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"Exporting segment {i + 1} of {ordered.Count}...");

                var segmentPath = Path.Combine(tempDir, $"s{i}.mp4");
                await ExportSegmentAsync(ordered[i], segmentPath, cancellationToken);
                ValidateSegment(segmentPath);
                segments.Add(segmentPath);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var finalPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

            if (segments.Count == 1)
            {
                progress?.Report("Saving file...");
                File.Copy(segments[0], finalPath, true);
            }
            else
            {
                progress?.Report("Merging segments...");
                await ConcatSegmentsAsync(segments, finalPath, cancellationToken);
            }

            if (!File.Exists(finalPath) || new FileInfo(finalPath).Length < 4096)
                throw new InvalidOperationException("Export did not produce a valid video file.");

            progress?.Report("Done!");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); }
            catch { /* ignore */ }
        }
    }

    private static string CreateShortTempDir()
    {
        var root = Path.Combine(Path.GetTempPath(), "vtrimz");
        Directory.CreateDirectory(root);
        return Path.Combine(root, Guid.NewGuid().ToString("N"));
    }

    private static async Task EnsureFfmpegAsync(CancellationToken cancellationToken)
    {
        if (_ffmpegReady)
            return;

        await SetupLock.WaitAsync(cancellationToken);
        try
        {
            if (_ffmpegReady)
                return;

            _ffmpegDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VTRIMZ",
                "ffmpeg");

            Directory.CreateDirectory(_ffmpegDir);

            var ffmpegExe = Path.Combine(_ffmpegDir, "ffmpeg.exe");
            if (!File.Exists(ffmpegExe))
                await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, _ffmpegDir);

            if (!File.Exists(ffmpegExe))
                throw new InvalidOperationException("Could not download FFmpeg. Check your internet connection.");

            GlobalFFOptions.Configure(new FFOptions { BinaryFolder = _ffmpegDir });
            FFmpeg.SetExecutablesPath(_ffmpegDir);
            _ffmpegReady = true;
        }
        finally
        {
            SetupLock.Release();
        }
    }

    private static async Task ExportSegmentAsync(
        TimelineClip clip,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (clip.DurationMs <= 0)
            throw new InvalidOperationException("Clip duration is zero.");

        var start = TimeSpan.FromMilliseconds(clip.StartMs);
        var duration = TimeSpan.FromMilliseconds(clip.DurationMs);

        await FFMpegArguments
            .FromFileInput(clip.SourcePath, verifyExists: true, options => options.Seek(start))
            .OutputToFile(outputPath, overwrite: true, options => options
                .WithDuration(duration)
                .WithVideoCodec("libx264")
                .WithAudioCodec("aac")
                .WithConstantRateFactor(23)
                .WithFastStart())
            .CancellableThrough(cancellationToken)
            .ProcessAsynchronously();
    }

    private static void ValidateSegment(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < 4096)
            throw new InvalidOperationException($"Failed to export segment: {Path.GetFileName(path)}");
    }

    private static async Task ConcatSegmentsAsync(
        IReadOnlyList<string> segments,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var listPath = Path.Combine(Path.GetTempPath(), $"vtrimz_{Guid.NewGuid():N}.txt");
        try
        {
            var lines = new StringBuilder();
            foreach (var seg in segments)
            {
                var normalized = Path.GetFullPath(seg).Replace('\\', '/');
                lines.AppendLine($"file '{normalized.Replace("'", "'\\''")}'");
            }
            await File.WriteAllTextAsync(listPath, lines.ToString(), cancellationToken);

            var listArg = QuotePath(listPath);
            var outArg = QuotePath(outputPath);

            try
            {
                await RunFfmpegAsync(
                    $"-y -f concat -safe 0 -i {listArg} -c copy -movflags +faststart {outArg}",
                    cancellationToken);
            }
            catch
            {
                await RunFfmpegAsync(
                    $"-y -f concat -safe 0 -i {listArg} -c:v libx264 -preset fast -crf 23 -c:a aac -b:a 192k -movflags +faststart {outArg}",
                    cancellationToken);
            }
        }
        finally
        {
            try { File.Delete(listPath); }
            catch { /* ignore */ }
        }
    }

    private static string QuotePath(string path) =>
        $"\"{Path.GetFullPath(path)}\"";

    private static async Task RunFfmpegAsync(string arguments, CancellationToken cancellationToken)
    {
        var ffmpegExe = Path.Combine(_ffmpegDir, "ffmpeg.exe");
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegExe,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start FFmpeg.");

        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(TrimFfmpegError(stderr));
    }

    private static string TrimFfmpegError(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return "FFmpeg export failed.";

        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var important = lines.Where(l =>
            l.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("Invalid", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("failed", StringComparison.OrdinalIgnoreCase)).ToList();

        if (important.Count > 0)
            return string.Join(Environment.NewLine, important.TakeLast(3));

        return string.Join(Environment.NewLine, lines.TakeLast(5));
    }
}
