using Vtrimz.Models;

namespace Vtrimz.Helpers;

public static class TimelineSnapHelper
{
    private const long MinSnapThresholdMs = 40;

    public static bool RangesOverlap(long startA, long endA, long startB, long endB) =>
        startA < endB && startB < endA;

    public static long SnapThresholdMs(double pixelsPerSecond) =>
        Math.Max(MinSnapThresholdMs, (long)(12.0 / pixelsPerSecond * 1000.0));

    public static long SnapStartMs(
        long desiredStart,
        long duration,
        long playheadMs,
        IEnumerable<TimelineClip> clips,
        Guid movingClipId,
        double pixelsPerSecond,
        out bool snapped)
    {
        snapped = false;
        desiredStart = Math.Max(0, desiredStart);
        var threshold = SnapThresholdMs(pixelsPerSecond);
        var end = desiredStart + duration;

        var points = new List<long> { 0, playheadMs };
        foreach (var clip in clips)
        {
            if (clip.Id == movingClipId)
                continue;
            points.Add(clip.TimelineStartMs);
            points.Add(clip.TimelineEndMs);
        }

        long bestStart = desiredStart;
        long bestDistance = threshold + 1;

        foreach (var point in points.Distinct())
        {
            var dStart = Math.Abs(desiredStart - point);
            if (dStart <= threshold && dStart < bestDistance)
            {
                bestDistance = dStart;
                bestStart = point;
                snapped = true;
            }

            var dEnd = Math.Abs(end - point);
            if (dEnd <= threshold && dEnd < bestDistance)
            {
                bestDistance = dEnd;
                bestStart = Math.Max(0, point - duration);
                snapped = true;
            }
        }

        return Math.Max(0, bestStart);
    }

    public static bool OverlapsOnTrack(
        Guid clipId,
        long startMs,
        long durationMs,
        int track,
        IReadOnlyList<TimelineClip> clips) =>
        clips.Any(c =>
            c.Id != clipId &&
            c.TrackIndex == track &&
            RangesOverlap(startMs, startMs + durationMs, c.TimelineStartMs, c.TimelineEndMs));

    public static long ResolveSameTrackPosition(
        TimelineClip moving,
        long desiredStart,
        int track,
        IReadOnlyList<TimelineClip> clips)
    {
        desiredStart = Math.Max(0, desiredStart);
        var duration = moving.DurationMs;

        if (!OverlapsOnTrack(moving.Id, desiredStart, duration, track, clips))
            return desiredStart;

        var others = clips
            .Where(c => c.Id != moving.Id && c.TrackIndex == track)
            .OrderBy(c => c.TimelineStartMs)
            .ToList();

        if (others.Count == 0)
            return desiredStart;

        var candidates = new HashSet<long>();

        var beforeFirst = others[0].TimelineStartMs - duration;
        if (beforeFirst >= 0)
            candidates.Add(beforeFirst);

        for (var i = 0; i < others.Count; i++)
        {
            candidates.Add(others[i].TimelineEndMs);

            if (i < others.Count - 1)
            {
                var gapStart = others[i].TimelineEndMs;
                var gapSize = others[i + 1].TimelineStartMs - gapStart;
                if (gapSize >= duration)
                    candidates.Add(gapStart);
            }
        }

        long best = desiredStart;
        var bestDistance = long.MaxValue;

        foreach (var start in candidates)
        {
            if (start < 0)
                continue;

            if (OverlapsOnTrack(moving.Id, start, duration, track, clips))
                continue;

            var distance = Math.Abs(start - desiredStart);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = start;
            }
        }

        if (bestDistance == long.MaxValue)
            best = others.Max(o => o.TimelineEndMs);

        return Math.Max(0, best);
    }

    public static long ConstrainToTrack(
        TimelineClip clip,
        long desiredStart,
        int track,
        IReadOnlyList<TimelineClip> clips)
    {
        desiredStart = Math.Max(0, desiredStart);
        return ResolveSameTrackPosition(clip, desiredStart, track, clips);
    }
}
