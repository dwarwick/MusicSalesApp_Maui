namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Free space on the filesystem that actually holds a cache directory.
///
/// Not <c>Path.GetPathRoot</c>. On every Unix-like platform that returns "/", which is a different
/// filesystem from the one the cache lives on - and on Android it is a full, read-only system partition
/// reporting **zero** bytes free. Probing it made the device free-space guard reject every download
/// while /data had 100 GB spare, and because the rejection logged at Debug it failed silently. The
/// Android audio cache never hit this only because it probes the directory directly via StatFs.
/// </summary>
internal static class CacheStorageProbe
{
    /// <summary>
    /// Bytes available to the filesystem containing <paramref name="directoryPath"/>, or
    /// <see cref="long.MaxValue"/> if it cannot be determined.
    ///
    /// Unknown deliberately means "do not block": a probe that cannot answer must not be able to stop
    /// caching altogether, which is the failure this whole type exists to prevent.
    /// </summary>
    public static long GetAvailableFreeSpaceBytes(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return long.MaxValue;
        }

        try
        {
            // Windows needs a drive root ("C:\"); DriveInfo rejects a plain directory there. On Unix
            // DriveInfo resolves whatever path it is given to the mount point containing it, which is
            // the whole point.
            var probePath = OperatingSystem.IsWindows()
                ? Path.GetPathRoot(directoryPath)
                : directoryPath;

            return string.IsNullOrWhiteSpace(probePath)
                ? long.MaxValue
                : new DriveInfo(probePath).AvailableFreeSpace;
        }
        catch (Exception)
        {
            return long.MaxValue;
        }
    }
}
