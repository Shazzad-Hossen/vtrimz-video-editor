using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Vtrimz.Helpers;
using Vtrimz.Models;

namespace Vtrimz.Controls;

public partial class TimelineControl : UserControl
{
    public static readonly DependencyProperty TotalDurationMsProperty =
        DependencyProperty.Register(nameof(TotalDurationMs), typeof(long), typeof(TimelineControl),
            new PropertyMetadata(0L, OnTimelinePropertyChanged));

    public static readonly DependencyProperty PositionMsProperty =
        DependencyProperty.Register(nameof(PositionMs), typeof(long), typeof(TimelineControl),
            new PropertyMetadata(0L, OnPositionChanged));

    public long TotalDurationMs
    {
        get => (long)GetValue(TotalDurationMsProperty);
        set => SetValue(TotalDurationMsProperty, value);
    }

    public long PositionMs
    {
        get => (long)GetValue(PositionMsProperty);
        set => SetValue(PositionMsProperty, Math.Clamp(value, 0, Math.Max(0, TotalDurationMs)));
    }

    public event EventHandler<long>? PositionChanged;
    public event EventHandler? ClipsChanged;
    public event EventHandler? ScreenshotRequested;

    public bool CanExport => _clips.Count > 0 && TotalDurationMs > 0;

    private readonly List<TimelineClip> _clips = [];
    private readonly TimelineUndoRedo _undoRedo = new();
    private Guid? _selectedClipId;
    private Guid? _dragClipId;
    private Guid? _pendingDragClipId;
    private Point _dragMouseOffset;
    private Point _dragStartPoint;
    private bool _clipDragActive;
    private bool _dragUndoPushed;
    private double? _snapIndicatorX;
    private FrameworkElement? _dragClipElement;
    private double _pixelsPerSecond = 80;
    private bool _isDraggingMarker;
    private bool _suppressPositionEvent;
    private bool _suppressUndoPush;

    private const double LeaderOffset = 48;
    private const double RulerHeight = 22;
    private const double RowHeight = 38;
    private const double MarkerHitWidth = 24;
    private const double MinPixelsPerSecond = 20;
    private const double MaxPixelsPerSecond = 500;
    private const int MinVisibleRows = 1;
    private const int SingleTrackIndex = 0;
    private const double DragStartThreshold = 5;

    public TimelineControl()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Redraw();
            Focus();
        };
    }

    public IReadOnlyList<TimelineClip> Clips => _clips;

    public TimelineClip? SelectedClip =>
        _selectedClipId.HasValue
            ? _clips.FirstOrDefault(c => c.Id == _selectedClipId.Value)
            : null;

    public void SetClips(IEnumerable<TimelineClip> clips, bool resetHistory = true)
    {
        _clips.Clear();
        _clips.AddRange(clips);
        NormalizeSingleStack();
        _selectedClipId = null;
        if (resetHistory)
            _undoRedo.Clear();
        RecalculateDuration();
        Redraw();
        UpdateCommandStates();
    }

    public void InitializeSingleClip(string path, long durationMs)
    {
        _undoRedo.Clear();
        _clips.Clear();
        _clips.Add(new TimelineClip
        {
            SourcePath = path,
            StartMs = 0,
            EndMs = durationMs,
            TimelineStartMs = 0,
            TrackIndex = SingleTrackIndex,
            ColorIndex = 0
        });
        _selectedClipId = null;
        RecalculateDuration();
        Redraw();
        UpdateCommandStates();
        FitToViewport();
    }

    public void FitToViewport()
    {
        if (TotalDurationMs <= 0)
            return;

        void ApplyFit()
        {
            var available = TimelineScroll.ViewportWidth - LeaderOffset - 48;
            if (available <= 50)
                return;

            var durationSec = TotalDurationMs / 1000.0;
            if (durationSec <= 0)
                return;

            _pixelsPerSecond = Math.Clamp(available / durationSec, MinPixelsPerSecond, MaxPixelsPerSecond);
            TimelineScroll.ScrollToHorizontalOffset(0);
            Redraw();
        }

        if (TimelineScroll.ViewportWidth > 50)
            ApplyFit();
        else
            Dispatcher.BeginInvoke(ApplyFit, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void NormalizeSingleStack()
    {
        foreach (var clip in _clips)
            clip.TrackIndex = SingleTrackIndex;
    }

    public bool TryMapTimelineToSource(long timelineMs, out TimelineClip? clip, out long sourceMs) =>
        TimelineMapper.TryMapTimelineToSource(_clips, timelineMs, out clip, out sourceMs);

    public long MapSourceToTimeline(string path, long sourceMs) =>
        TimelineMapper.MapSourceToTimeline(_clips, path, sourceMs);

    public void SplitAtPosition()
    {
        if (_clips.Count == 0 || TotalDurationMs <= 0)
            return;

        var splitMs = PositionMs;
        foreach (var clip in _clips)
        {
            if (splitMs > clip.TimelineStartMs && splitMs < clip.TimelineEndMs)
            {
                PushUndo();
                var offsetInClip = splitMs - clip.TimelineStartMs;
                var left = new TimelineClip
                {
                    SourcePath = clip.SourcePath,
                    StartMs = clip.StartMs,
                    EndMs = clip.StartMs + offsetInClip,
                    TimelineStartMs = clip.TimelineStartMs,
                    TrackIndex = SingleTrackIndex,
                    ColorIndex = clip.ColorIndex
                };
                var right = new TimelineClip
                {
                    SourcePath = clip.SourcePath,
                    StartMs = clip.StartMs + offsetInClip,
                    EndMs = clip.EndMs,
                    TimelineStartMs = clip.TimelineStartMs + offsetInClip,
                    TrackIndex = SingleTrackIndex,
                    ColorIndex = ClipColors.NextIndex(clip.ColorIndex)
                };

                var index = _clips.IndexOf(clip);
                _clips.RemoveAt(index);
                _clips.Insert(index, right);
                _clips.Insert(index, left);
                _selectedClipId = right.Id;
                RecalculateDuration();
                Redraw();
                NotifyClipsChanged();
                return;
            }
        }
    }

    public void DeleteSelectedClip()
    {
        if (!_selectedClipId.HasValue || _clips.Count <= 1)
            return;

        var index = _clips.FindIndex(c => c.Id == _selectedClipId.Value);
        if (index < 0)
            return;

        PushUndo();
        _clips.RemoveAt(index);
        _selectedClipId = null;
        RecalculateDuration();
        PositionMs = Math.Min(PositionMs, TotalDurationMs);
        Redraw();
        NotifyClipsChanged();
    }

    public void Undo()
    {
        if (!_undoRedo.CanUndo)
            return;

        var current = CaptureSnapshot();
        var previous = _undoRedo.Undo(current);
        if (previous == null)
            return;

        RestoreSnapshot(previous);
    }

    public void Redo()
    {
        if (!_undoRedo.CanRedo)
            return;

        var current = CaptureSnapshot();
        var next = _undoRedo.Redo(current);
        if (next == null)
            return;

        RestoreSnapshot(next);
    }

    private void PushUndo()
    {
        if (_suppressUndoPush)
            return;
        _undoRedo.Push(CaptureSnapshot());
        UpdateCommandStates();
    }

    private TimelineSnapshot CaptureSnapshot() =>
        TimelineSnapshot.Capture(_clips, PositionMs, _selectedClipId);

    private void RestoreSnapshot(TimelineSnapshot snapshot)
    {
        _suppressUndoPush = true;
        _clips.Clear();
        _clips.AddRange(snapshot.Clips.Select(c => c.ClonePreserveId()));
        _selectedClipId = snapshot.SelectedClipId;
        _selectedClipId = _clips.Any(c => c.Id == _selectedClipId) ? _selectedClipId : null;
        PositionMs = Math.Min(snapshot.PositionMs, TotalDurationMs);
        RecalculateDuration();
        Redraw();
        UpdateCommandStates();
        _suppressUndoPush = false;
        NotifyClipsChanged();
    }

    private void NotifyClipsChanged() => ClipsChanged?.Invoke(this, EventArgs.Empty);

    private void RecalculateDuration() =>
        TotalDurationMs = TimelineMapper.TotalDurationMs(_clips);

    private static void OnTimelinePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TimelineControl tc)
            tc.Redraw();
    }

    private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TimelineControl tc && !tc._suppressPositionEvent)
            tc.UpdateMarkerPosition();
    }

    private int GetTrackCount() => 1;

    private void ApplyClipPositionDuringDrag(TimelineClip clip, long desiredStartMs)
    {
        clip.TrackIndex = SingleTrackIndex;

        var snappedStart = TimelineSnapHelper.SnapStartMs(
            desiredStartMs,
            clip.DurationMs,
            PositionMs,
            _clips,
            clip.Id,
            _pixelsPerSecond,
            out var didSnap);

        _snapIndicatorX = didSnap ? MsToX(snappedStart) : null;
        clip.TimelineStartMs = TimelineSnapHelper.ConstrainToTrack(clip, snappedStart, SingleTrackIndex, _clips);
    }

    private void ApplyClipPositionFinal(TimelineClip clip, long desiredStartMs)
    {
        clip.TrackIndex = SingleTrackIndex;

        var snappedStart = TimelineSnapHelper.SnapStartMs(
            desiredStartMs,
            clip.DurationMs,
            PositionMs,
            _clips,
            clip.Id,
            _pixelsPerSecond,
            out _);

        clip.TimelineStartMs = TimelineSnapHelper.ConstrainToTrack(clip, snappedStart, SingleTrackIndex, _clips);
    }

    private double MsToX(long ms) => LeaderOffset + ms / 1000.0 * _pixelsPerSecond;

    private long XToMs(double x)
    {
        var ms = (long)((x - LeaderOffset) / _pixelsPerSecond * 1000);
        return Math.Clamp(ms, 0, TotalDurationMs);
    }

    private long XToMsFree(double x)
    {
        var ms = (long)((x - LeaderOffset) / _pixelsPerSecond * 1000);
        return Math.Max(0, ms);
    }

    private int YToTrack(double canvasY)
    {
        var relative = canvasY - RulerHeight;
        if (relative < 0)
            return 0;
        return Math.Max(0, (int)(relative / RowHeight));
    }

    private FrameworkElement? FindClipElement(Guid clipId)
    {
        foreach (var child in ClipsLayer.Children)
        {
            if (child is FrameworkElement fe && fe.Tag is Guid id && id == clipId)
                return fe;
        }

        return null;
    }

    private void UpdateDragVisual(TimelineClip clip)
    {
        var width = GetCanvasWidth();
        var height = GetCanvasHeight();
        TimelineCanvas.Width = width;
        TimelineCanvas.Height = height;

        DrawTrackRows(width);

        _dragClipElement ??= FindClipElement(clip.Id);
        if (_dragClipElement != null)
        {
            Canvas.SetLeft(_dragClipElement, MsToX(clip.TimelineStartMs));
            Canvas.SetTop(_dragClipElement, RulerHeight + 4);
            Panel.SetZIndex(_dragClipElement, 200);
        }

        UpdateMarkerPosition();
    }

    private void DrawSnapIndicator()
    {
        if (!_snapIndicatorX.HasValue || !_dragClipId.HasValue)
            return;

        var x = _snapIndicatorX.Value;
        RulerLayer.Children.Add(new Line
        {
            X1 = x,
            X2 = x,
            Y1 = 0,
            Y2 = RulerHeight,
            Stroke = (Brush)FindResource("AccentBrush"),
            StrokeThickness = 2,
            StrokeDashArray = [3, 2],
            Opacity = 0.9
        });
    }

    private double GetCanvasWidth()
    {
        var maxEnd = _clips.Count > 0 ? _clips.Max(c => c.TimelineEndMs) : 0;
        var contentWidth = maxEnd > 0 ? MsToX(maxEnd) + 60 : LeaderOffset + 200;
        return Math.Max(contentWidth, TimelineScroll.ViewportWidth);
    }

    private double GetCanvasHeight() => RulerHeight + RowHeight + 8;

    private void Redraw()
    {
        var width = GetCanvasWidth();
        var height = GetCanvasHeight();
        TimelineCanvas.Width = width;
        TimelineCanvas.Height = height;

        DrawRuler(width);
        DrawTrackRows(width);
        DrawClips();
        DrawSnapIndicator();
        UpdateMarkerPosition();
        UpdateZoomLabel();
        UpdateCommandStates();
    }

    private void DrawRuler(double width)
    {
        RulerLayer.Children.Clear();

        RulerLayer.Children.Add(new Line
        {
            X1 = LeaderOffset,
            X2 = width,
            Y1 = RulerHeight - 1,
            Y2 = RulerHeight - 1,
            Stroke = (Brush)FindResource("BorderBrush"),
            StrokeThickness = 1
        });

        var maxMs = Math.Max(TotalDurationMs, _clips.Count > 0 ? _clips.Max(c => c.TimelineEndMs) : 0);
        if (maxMs <= 0)
            return;

        var intervalSec = GetRulerIntervalSec();
        var maxSec = maxMs / 1000.0 + intervalSec;

        for (var sec = 0.0; sec <= maxSec; sec += intervalSec)
        {
            var x = MsToX((long)(sec * 1000));
            var isMajor = sec % (intervalSec * 5) < 0.001 || intervalSec >= 5;

            RulerLayer.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = RulerHeight - (isMajor ? 12 : 6),
                Y2 = RulerHeight - 1,
                Stroke = (Brush)FindResource("TextSecondaryBrush"),
                StrokeThickness = 1
            });

            if (isMajor)
            {
                var label = new TextBlock
                {
                    Text = FormatRulerTime(sec),
                    Foreground = (Brush)FindResource("TextSecondaryBrush"),
                    FontSize = 10,
                    FontFamily = new FontFamily("Consolas")
                };
                Canvas.SetLeft(label, x + 2);
                Canvas.SetTop(label, 2);
                RulerLayer.Children.Add(label);
            }
        }
    }

    private static string FormatRulerTime(double seconds) =>
        seconds < 60 ? $"{seconds:0}s" : TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");

    private double GetRulerIntervalSec()
    {
        if (_pixelsPerSecond >= 200) return 0.5;
        if (_pixelsPerSecond >= 100) return 1;
        if (_pixelsPerSecond >= 50) return 2;
        if (_pixelsPerSecond >= 25) return 5;
        return 10;
    }

    private void DrawTrackRows(double width)
    {
        TracksLayer.Children.Clear();
        var trackCount = GetTrackCount();

        for (var row = 0; row < trackCount; row++)
        {
            var top = RulerHeight + row * RowHeight;
            var rowBg = new Border
            {
                Width = width - LeaderOffset,
                Height = RowHeight - 4,
                Background = (Brush)FindResource("BgElevatedBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Opacity = 0.6
            };
            Canvas.SetLeft(rowBg, LeaderOffset);
            Canvas.SetTop(rowBg, top + 2);
            TracksLayer.Children.Add(rowBg);
        }
    }

    private void DrawClips()
    {
        ClipsLayer.Children.Clear();

        foreach (var clip in _clips.OrderBy(c => c.TimelineStartMs))
        {
            var clipWidth = Math.Max(MsToX(clip.TimelineEndMs) - MsToX(clip.TimelineStartMs), 6);
            var isSelected = clip.Id == _selectedClipId;
            var isDragging = clip.Id == _dragClipId;
            var top = RulerHeight + 4;

            var panel = new Grid
            {
                Width = clipWidth,
                Height = RowHeight - 8,
                Cursor = Cursors.SizeAll,
                Tag = clip.Id,
                ToolTip = $"{System.IO.Path.GetFileName(clip.SourcePath)}\nTimeline: {TimeHelper.FormatMs(clip.TimelineStartMs)}\nSource: {TimeHelper.FormatMs(clip.StartMs)} → {TimeHelper.FormatMs(clip.EndMs)}"
            };

            var rect = new Border
            {
                Background = ClipColors.GetBrush(clip.ColorIndex),
                BorderBrush = isSelected
                    ? (Brush)FindResource("MarkerBrush")
                    : (Brush)FindResource("BorderBrush"),
                BorderThickness = isSelected ? new Thickness(2) : new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Opacity = isDragging ? 0.8 : isSelected ? 1.0 : 0.92,
                IsHitTestVisible = false
            };
            panel.Children.Add(rect);

            if (clipWidth > 36)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = System.IO.Path.GetFileName(clip.SourcePath),
                    Foreground = Brushes.White,
                    FontSize = 10,
                    Margin = new Thickness(6, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    IsHitTestVisible = false
                });
            }

            panel.MouseLeftButtonDown += Clip_MouseLeftButtonDown;
            panel.MouseLeftButtonUp += Clip_MouseLeftButtonUp;

            Canvas.SetLeft(panel, MsToX(clip.TimelineStartMs));
            Canvas.SetTop(panel, top);
            if (isDragging)
                Panel.SetZIndex(panel, 200);
            else
                Panel.SetZIndex(panel, 10);

            ClipsLayer.Children.Add(panel);
        }
    }

    private void MoveMarkerTo(long timelineMs)
    {
        _suppressPositionEvent = true;
        PositionMs = Math.Clamp(timelineMs, 0, Math.Max(0, TotalDurationMs));
        _suppressPositionEvent = false;
        UpdateMarkerPosition();
        PositionChanged?.Invoke(this, PositionMs);
    }

    private bool TryGetClipAt(Point canvasPos, out TimelineClip? clip)
    {
        clip = _clips
            .Where(c => canvasPos.X >= MsToX(c.TimelineStartMs) && canvasPos.X <= MsToX(c.TimelineEndMs))
            .Where(c =>
            {
                var top = RulerHeight + 4;
                var bottom = top + RowHeight - 8;
                return canvasPos.Y >= top && canvasPos.Y <= bottom;
            })
            .OrderBy(c => c.TimelineStartMs)
            .FirstOrDefault();

        return clip != null;
    }

    private void StartClipInteraction(Guid clipId, Point canvasPos)
    {
        var clip = _clips.First(c => c.Id == clipId);
        _selectedClipId = clipId;
        _pendingDragClipId = clipId;
        _clipDragActive = false;
        _dragUndoPushed = false;
        _snapIndicatorX = null;
        _dragStartPoint = canvasPos;

        var clipLeft = MsToX(clip.TimelineStartMs);
        _dragMouseOffset = new Point(canvasPos.X - clipLeft, canvasPos.Y - (RulerHeight + 4));

        Redraw();
        UpdateCommandStates();
        Focus();
    }

    private void BeginClipDrag(Guid clipId)
    {
        _clipDragActive = true;
        _dragClipId = clipId;
        _dragClipElement = FindClipElement(clipId);
        CaptureMouse();
    }

    private void CancelClipInteraction()
    {
        _pendingDragClipId = null;
        _clipDragActive = false;
        _dragClipId = null;
        _dragClipElement = null;
        _snapIndicatorX = null;

        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    private void Clip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not Guid clipId)
            return;

        if (e.ClickCount > 1)
            return;

        e.Handled = true;
        StartClipInteraction(clipId, e.GetPosition(TimelineCanvas));
    }

    private void Clip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not Guid clipId)
            return;

        if (e.ClickCount < 2)
            return;

        e.Handled = true;
        CancelClipInteraction();
        _selectedClipId = clipId;
        var clip = _clips.First(c => c.Id == clipId);
        MoveMarkerTo(clip.TimelineStartMs);
        Redraw();
    }

    private void FinishClipDrag()
    {
        if (!_dragClipId.HasValue)
            return;

        var clipId = _dragClipId.Value;
        _dragClipId = null;
        _pendingDragClipId = null;
        _clipDragActive = false;
        _snapIndicatorX = null;
        _dragClipElement = null;

        if (IsMouseCaptured)
            ReleaseMouseCapture();

        var clip = _clips.First(c => c.Id == clipId);
        _selectedClipId = clipId;
        ApplyClipPositionFinal(clip, clip.TimelineStartMs);

        RecalculateDuration();
        Redraw();
        UpdateCommandStates();
        NotifyClipsChanged();
    }

    private void UpdateClipDrag(Point pos)
    {
        if (!_dragClipId.HasValue)
            return;

        if (!_dragUndoPushed)
        {
            PushUndo();
            _dragUndoPushed = true;
        }

        var clip = _clips.First(c => c.Id == _dragClipId.Value);
        var desiredStart = XToMsFree(pos.X - _dragMouseOffset.X);

        ApplyClipPositionDuringDrag(clip, desiredStart);
        UpdateDragVisual(clip);
    }

    public void EnsureMarkerVisible()
    {
        var markerX = MsToX(PositionMs);
        const double margin = 120;
        var offset = TimelineScroll.HorizontalOffset;
        var viewport = TimelineScroll.ViewportWidth;

        if (viewport <= 0)
            return;

        if (markerX < offset + margin)
            TimelineScroll.ScrollToHorizontalOffset(Math.Max(0, markerX - margin));
        else if (markerX > offset + viewport - margin)
            TimelineScroll.ScrollToHorizontalOffset(Math.Max(0, markerX - viewport + margin));
    }

    private void UpdateMarkerPosition()
    {
        var x = MsToX(PositionMs);
        var height = GetCanvasHeight();

        Canvas.SetLeft(MarkerLine, x);
        MarkerLine.Y1 = RulerHeight;
        MarkerLine.Y2 = height;

        Canvas.SetLeft(MarkerHead, x - MarkerHead.Width / 2);
        Canvas.SetTop(MarkerHead, 2);

        Canvas.SetLeft(MarkerHitArea, x - MarkerHitWidth / 2);
        Canvas.SetTop(MarkerHitArea, 0);
        MarkerHitArea.Width = MarkerHitWidth;
        MarkerHitArea.Height = RulerHeight + 6;

        PositionLabel.Text = SelectedClip != null
            ? $"Selected: {System.IO.Path.GetFileName(SelectedClip.SourcePath)}  |  Marker: {TimeHelper.FormatMs(PositionMs)}"
            : $"Marker: {TimeHelper.FormatMs(PositionMs)}  ({PositionMs / 1000.0:F2}s)";

        EnsureMarkerVisible();
    }

    private bool IsNearMarker(Point pos)
    {
        var markerX = MsToX(PositionMs);
        return Math.Abs(pos.X - markerX) <= MarkerHitWidth / 2 + 2 && pos.Y <= GetCanvasHeight();
    }

    private void Marker_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _isDraggingMarker = true;
        _selectedClipId = null;
        UpdateCommandStates();
        CaptureMouse();
        TimelineCanvas.Cursor = Cursors.SizeWE;
        SeekFromMouse(e.GetPosition(TimelineCanvas));
        Focus();
    }

    private void Marker_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!_isDraggingMarker)
            TimelineCanvas.Cursor = Cursors.Hand;
    }

    private void Marker_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isDraggingMarker)
            TimelineCanvas.Cursor = Cursors.Arrow;
    }

    private void FinishMarkerDrag()
    {
        if (!_isDraggingMarker)
            return;

        _isDraggingMarker = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        TimelineCanvas.Cursor = Cursors.Arrow;
    }

    private void UpdateZoomLabel() =>
        ZoomLabel.Text = $"{(int)(_pixelsPerSecond / 80 * 100)}%";

    private void UpdateCommandStates()
    {
        DeleteButton.IsEnabled = _clips.Count > 1 && _selectedClipId.HasValue;
        UndoButton.IsEnabled = _undoRedo.CanUndo;
        RedoButton.IsEnabled = _undoRedo.CanRedo;
    }

    private void SeekFromMouse(Point pos)
    {
        var ms = XToMs(pos.X);
        _suppressPositionEvent = true;
        PositionMs = ms;
        _suppressPositionEvent = false;
        UpdateMarkerPosition();
        PositionChanged?.Invoke(this, ms);
    }

    private void TimelineCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingMarker || _dragClipId.HasValue)
            return;

        Focus();
        var pos = e.GetPosition(TimelineCanvas);

        if (TryGetClipAt(pos, out var clip) && clip != null)
        {
            e.Handled = true;
            StartClipInteraction(clip.Id, pos);
            return;
        }

        if (pos.X >= LeaderOffset)
        {
            _selectedClipId = null;
            _pendingDragClipId = null;
            Redraw();
            UpdateCommandStates();
            SeekFromMouse(pos);
        }
    }

    private void TimelineCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragClipId.HasValue)
            return;

        if (_isDraggingMarker)
            return;

        var pos = e.GetPosition(TimelineCanvas);
        TimelineCanvas.Cursor = IsNearMarker(pos) ? Cursors.Hand : Cursors.Arrow;
    }

    private void TimelineControl_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingMarker)
        {
            SeekFromMouse(e.GetPosition(TimelineCanvas));
            e.Handled = true;
            return;
        }

        if (_pendingDragClipId.HasValue && !_clipDragActive)
        {
            var pos = e.GetPosition(TimelineCanvas);
            var dx = pos.X - _dragStartPoint.X;
            var dy = pos.Y - _dragStartPoint.Y;
            if (Math.Abs(dx) >= DragStartThreshold || Math.Abs(dy) >= DragStartThreshold)
                BeginClipDrag(_pendingDragClipId.Value);
        }

        if (!_dragClipId.HasValue)
            return;

        UpdateClipDrag(e.GetPosition(TimelineCanvas));
        e.Handled = true;
    }

    private void TimelineControl_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingMarker)
        {
            e.Handled = true;
            FinishMarkerDrag();
            return;
        }

        if (_pendingDragClipId.HasValue && !_clipDragActive)
        {
            _pendingDragClipId = null;
            e.Handled = true;
            return;
        }

        if (!_dragClipId.HasValue)
            return;

        e.Handled = true;
        FinishClipDrag();
    }

    private void TimelineCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_dragClipId.HasValue)
            return;

        if (!_isDraggingMarker)
            TimelineCanvas.Cursor = Cursors.Arrow;
    }

    private void TimelineScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;

        e.Handled = true;
        if (e.Delta > 0)
            ZoomIn();
        else
            ZoomOut();
    }

    private void TimelineControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            if (_selectedClipId.HasValue && _clips.Count > 1)
            {
                DeleteSelectedClip();
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Undo();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Redo();
            e.Handled = true;
        }
    }

    private void ZoomIn() { _pixelsPerSecond = Math.Min(MaxPixelsPerSecond, _pixelsPerSecond * 1.2); Redraw(); }
    private void ZoomOut() { _pixelsPerSecond = Math.Max(MinPixelsPerSecond, _pixelsPerSecond / 1.2); Redraw(); }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e) => ZoomIn();
    private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => ZoomOut();
    private void FitButton_Click(object sender, RoutedEventArgs e) => FitToViewport();
    private void SplitButton_Click(object sender, RoutedEventArgs e) => SplitAtPosition();
    private void DeleteButton_Click(object sender, RoutedEventArgs e) => DeleteSelectedClip();
    private void UndoButton_Click(object sender, RoutedEventArgs e) => Undo();
    private void RedoButton_Click(object sender, RoutedEventArgs e) => Redo();
    private void ScreenshotButton_Click(object sender, RoutedEventArgs e) => ScreenshotRequested?.Invoke(this, EventArgs.Empty);
}
