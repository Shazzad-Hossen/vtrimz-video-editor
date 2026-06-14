namespace Vtrimz.Helpers;

public static class TimeHelper
{
    public static string FormatMs(long ms)
    {
        if (ms < 0) ms = 0;
        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}"
            : $"{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}";
    }

    public static long FrameDurationMs(double fps) =>
        fps > 0 ? (long)Math.Round(1000.0 / fps) : 33;
}
