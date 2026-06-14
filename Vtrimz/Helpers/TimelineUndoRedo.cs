using Vtrimz.Models;

namespace Vtrimz.Helpers;

public sealed class TimelineSnapshot
{
    public List<TimelineClip> Clips { get; init; } = [];
    public long PositionMs { get; init; }
    public Guid? SelectedClipId { get; init; }

    public static TimelineSnapshot Capture(
        IEnumerable<TimelineClip> clips,
        long positionMs,
        Guid? selectedClipId) =>
        new()
        {
            Clips = clips.Select(c => c.ClonePreserveId()).ToList(),
            PositionMs = positionMs,
            SelectedClipId = selectedClipId
        };
}

public sealed class TimelineUndoRedo
{
    private readonly Stack<TimelineSnapshot> _undo = new();
    private readonly Stack<TimelineSnapshot> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Push(TimelineSnapshot snapshot)
    {
        _undo.Push(snapshot);
        _redo.Clear();
    }

    public TimelineSnapshot? Undo(TimelineSnapshot current)
    {
        if (!CanUndo)
            return null;

        _redo.Push(current);
        return _undo.Pop();
    }

    public TimelineSnapshot? Redo(TimelineSnapshot current)
    {
        if (!CanRedo)
            return null;

        _undo.Push(current);
        return _redo.Pop();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
