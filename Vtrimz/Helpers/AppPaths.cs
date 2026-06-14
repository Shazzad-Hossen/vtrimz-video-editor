using System.IO;

namespace Vtrimz.Helpers;

public static class AppPaths
{
    private static string? _bundleRoot;

    /// <summary>
    /// App content root — exe folder (folder publish) or single-file extract dir under %TEMP%\.net\VTRIMZ\.
    /// </summary>
    public static string BundleRoot => _bundleRoot ??= ResolveBundleRoot();

    public static string GetAssetPath(params string[] relativeParts) =>
        Path.Combine(BundleRoot, Path.Combine(relativeParts));

    public static string? FindLibVlcDirectory()
    {
        var arch = Environment.Is64BitProcess ? "win-x64" : "win-x86";
        var vlcPath = Path.Combine(BundleRoot, "libvlc", arch);
        return File.Exists(Path.Combine(vlcPath, "libvlc.dll")) ? vlcPath : null;
    }

    private static string ResolveBundleRoot()
    {
        if (HasBundledLayout(AppContext.BaseDirectory))
            return AppContext.BaseDirectory;

        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath)!;
            if (HasBundledLayout(exeDir))
                return exeDir;
        }

        var singleFileRoot = FindSingleFileExtractRoot();
        if (singleFileRoot != null)
            return singleFileRoot;

        return AppContext.BaseDirectory;
    }

    private static string? FindSingleFileExtractRoot()
    {
        var netDir = Path.Combine(Path.GetTempPath(), ".net", "VTRIMZ");
        if (!Directory.Exists(netDir))
            return null;

        return Directory.EnumerateDirectories(netDir)
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault(HasBundledLayout);
    }

    private static bool HasBundledLayout(string root) =>
        Directory.Exists(Path.Combine(root, "libvlc")) ||
        File.Exists(Path.Combine(root, "asset", "dev.png"));
}
