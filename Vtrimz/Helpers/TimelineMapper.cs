using Vtrimz.Models;

namespace Vtrimz.Helpers;

public static class TimelineMapper
{
    public static IEnumerable<TimelineClip> MainTrackClips(IReadOnlyList<TimelineClip> clips) =>
        clips.Where(c => c.TrackIndex == 0).OrderBy(c => c.TimelineStartMs);

    public static List<TimelineClip> ExportClips(IReadOnlyList<TimelineClip> clips)
    {
        var main = clips
            .Where(c => c.TrackIndex == 0)
            .OrderBy(c => c.TimelineStartMs)
            .ToList();

        return main.Count > 0
            ? main
            : clips.OrderBy(c => c.TimelineStartMs).ThenBy(c => c.TrackIndex).ToList();
    }

    public static long TotalDurationMs(IReadOnlyList<TimelineClip> clips) =>
        clips.Count == 0 ? 0 : clips.Max(c => c.TimelineEndMs);

    public static bool TryGetClipAt(
        IReadOnlyList<TimelineClip> clips,
        long timelineMs,
        out TimelineClip? clip,
        out long sourceMs)
    {
        clip = clips
            .Where(c => timelineMs >= c.TimelineStartMs && timelineMs < c.TimelineEndMs)
            .OrderBy(c => c.TrackIndex)
            .FirstOrDefault();

        if (clip == null)
        {
            sourceMs = 0;
            return false;
        }

        sourceMs = clip.StartMs + (timelineMs - clip.TimelineStartMs);
        return true;
    }

    public static long GetNextClipStart(IReadOnlyList<TimelineClip> clips, long afterTimelineMs)
    {
        var next = clips
            .Where(c => c.TimelineStartMs >= afterTimelineMs)
            .Select(c => c.TimelineStartMs)
            .DefaultIfEmpty(-1)
            .Min();

        return next;
    }

    public static bool TryMapTimelineToSource(
        IReadOnlyList<TimelineClip> clips,
        long timelineMs,
        out TimelineClip? clip,
        out long sourceMs)
    {
        clip = null;
        sourceMs = 0;

        // Lower track index = higher row on timeline = visible on top (standard NLE).
        if (TryGetClipAt(clips, timelineMs, out clip, out sourceMs))
            return true;

        var fallback = clips.OrderBy(c => c.TimelineStartMs).ThenBy(c => c.TrackIndex).LastOrDefault();
        if (fallback == null)
            return false;

        clip = fallback;
        sourceMs = fallback.EndMs;
        return true;
    }

    public static long MapSourceToTimeline(IReadOnlyList<TimelineClip> clips, string path, long sourceMs)
    {
        var hit = clips
            .Where(c => c.SourcePath == path && sourceMs >= c.StartMs && sourceMs <= c.EndMs)
            .OrderBy(c => c.TrackIndex)
            .FirstOrDefault();

        return hit != null
            ? hit.TimelineStartMs + (sourceMs - hit.StartMs)
            : 0;
    }
}
