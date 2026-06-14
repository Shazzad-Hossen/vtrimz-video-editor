namespace Vtrimz.Models;

public class TimelineClip
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string SourcePath { get; init; } = string.Empty;
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public long TimelineStartMs { get; set; }
    public int TrackIndex { get; set; }
    public int ColorIndex { get; set; }

    public long DurationMs => Math.Max(0, EndMs - StartMs);
    public long TimelineEndMs => TimelineStartMs + DurationMs;

    public TimelineClip Clone() => new()
    {
        Id = Guid.NewGuid(),
        SourcePath = SourcePath,
        StartMs = StartMs,
        EndMs = EndMs,
        TimelineStartMs = TimelineStartMs,
        TrackIndex = TrackIndex,
        ColorIndex = ColorIndex
    };

    public TimelineClip ClonePreserveId() => new()
    {
        Id = Id,
        SourcePath = SourcePath,
        StartMs = StartMs,
        EndMs = EndMs,
        TimelineStartMs = TimelineStartMs,
        TrackIndex = TrackIndex,
        ColorIndex = ColorIndex
    };
}
