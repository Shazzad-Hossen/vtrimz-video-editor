using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Microsoft.Win32;
using Vtrimz.Helpers;
using Vtrimz.Models;
using Vtrimz.Services;

namespace Vtrimz.Views;

public partial class EditorView : UserControl, IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private readonly DispatcherTimer _positionTimer;

    private Media? _media;
    private string _videoPath = string.Empty;
    private long _timelineDurationMs;
    private double _fps = 30;
    private bool _isPlaying;
    private bool _suppressTimelineSeek;
    private bool _videoViewReady;
    private string? _pendingVideoPath;
    private Guid? _activeClipId;

    public EditorView(LibVLC libVlc)
    {
        InitializeComponent();

        _libVlc = libVlc;
        _mediaPlayer = new MediaPlayer(_libVlc);

        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _positionTimer.Tick += PositionTimer_Tick;

        _mediaPlayer.EndReached += (_, _) => Dispatcher.BeginInvoke(OnPlaybackEnded);

        VideoView.Loaded += OnVideoViewLoaded;
        Timeline.ClipsChanged += (_, _) => OnClipsChanged();
        Timeline.ExportRequested += (_, _) => ExportVideo();

        Focusable = true;
        PreviewKeyDown += EditorView_PreviewKeyDown;
    }

    private void EditorView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            Timeline.DeleteSelectedClip();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Timeline.Undo();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Timeline.Redo();
            e.Handled = true;
        }
    }

    private void OnVideoViewLoaded(object sender, RoutedEventArgs e)
    {
        if (_videoViewReady)
            return;

        VideoView.MediaPlayer = _mediaPlayer;
        _videoViewReady = true;

        if (_pendingVideoPath != null)
        {
            var path = _pendingVideoPath;
            _pendingVideoPath = null;
            LoadVideo(path);
        }
    }

    public void LoadVideo(string path)
    {
        if (!_videoViewReady)
        {
            _pendingVideoPath = path;
            return;
        }

        DisposeMedia();

        _videoPath = path;
        _media = new Media(_libVlc, path, FromType.FromPath);
        _media.Parse(MediaParseOptions.ParseLocal, 10000);

        var parseDeadline = DateTime.UtcNow.AddSeconds(10);
        while (_media.ParsedStatus != MediaParsedStatus.Done && DateTime.UtcNow < parseDeadline)
            Thread.Sleep(15);

        var sourceDuration = Math.Max(_media.Duration, 0);
        if (sourceDuration == 0)
        {
            MessageBox.Show("Could not read video duration. The file may be unsupported or corrupted.",
                "VTRIMZ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _fps = DetectFps(_media);
        _mediaPlayer.Media = _media;
        _mediaPlayer.Volume = (int)VolumeSlider.Value;

        Timeline.InitializeSingleClip(path, sourceDuration);
        _timelineDurationMs = Timeline.TotalDurationMs;
        Timeline.PositionMs = 0;

        UpdateTimeDisplay(0);
        UpdatePlayButton(false);

        SeekToSourceTime(0, pause: true);
        _positionTimer.Start();
    }

    private void OnClipsChanged()
    {
        _timelineDurationMs = Timeline.TotalDurationMs;
        var timelinePos = Math.Min(Timeline.PositionMs, _timelineDurationMs);
        Timeline.PositionMs = timelinePos;
        UpdateTimeDisplay(timelinePos);

        if (Timeline.TryMapTimelineToSource(timelinePos, out var clip, out var sourceMs) && clip != null)
            SeekToSourceTime(sourceMs, pause: !_isPlaying, clip.SourcePath);
    }

    private static double DetectFps(Media media)
    {
        foreach (var track in media.Tracks)
        {
            if (track.TrackType == TrackType.Video)
            {
                var rate = track.Data.Video.FrameRateNum / (double)Math.Max(1, track.Data.Video.FrameRateDen);
                if (rate > 1)
                    return rate;
            }
        }
        return 30;
    }

    private void PositionTimer_Tick(object? sender, EventArgs e)
    {
        if (_mediaPlayer.Media == null || !_isPlaying || _suppressTimelineSeek)
            return;

        SyncPlaybackFromMedia();
    }

    private void SyncPlaybackFromMedia()
    {
        if (!_activeClipId.HasValue)
        {
            if (!Timeline.TryMapTimelineToSource(Timeline.PositionMs, out var initClip, out var initSource) || initClip == null)
                return;

            _activeClipId = initClip.Id;
            if (initClip.SourcePath != _videoPath || Math.Abs(_mediaPlayer.Time - initSource) > 80)
                SeekToSourceTime(initSource, pause: false, initClip.SourcePath);
            return;
        }

        var clip = Timeline.Clips.FirstOrDefault(c => c.Id == _activeClipId.Value);
        if (clip == null)
        {
            _activeClipId = null;
            return;
        }

        if (clip.SourcePath != _videoPath)
        {
            var src = clip.StartMs + Math.Max(0, Timeline.PositionMs - clip.TimelineStartMs);
            SeekToSourceTime(src, pause: false, clip.SourcePath);
            return;
        }

        var sourceMs = Math.Max(0, _mediaPlayer.Time);
        var timelineMs = clip.TimelineStartMs + (sourceMs - clip.StartMs);

        if (sourceMs >= clip.EndMs - 40 || timelineMs >= clip.TimelineEndMs - 40)
        {
            AdvancePastClipEnd(clip);
            return;
        }

        UpdateTimelinePosition(timelineMs);
    }

    private void AdvancePastClipEnd(TimelineClip endedClip)
    {
        var at = endedClip.TimelineEndMs;
        if (at >= _timelineDurationMs)
        {
            StopPlaybackAtEnd();
            return;
        }

        if (!TimelineMapper.TryGetClipAt(Timeline.Clips, at, out var nextClip, out var nextSourceMs))
        {
            var nextStart = TimelineMapper.GetNextClipStart(Timeline.Clips, at);
            if (nextStart < 0)
            {
                StopPlaybackAtEnd();
                return;
            }

            at = nextStart;
            if (!TimelineMapper.TryGetClipAt(Timeline.Clips, at, out nextClip, out nextSourceMs))
            {
                StopPlaybackAtEnd();
                return;
            }
        }

        if (nextClip == null || nextClip.Id == endedClip.Id)
        {
            StopPlaybackAtEnd();
            return;
        }

        _activeClipId = nextClip.Id;
        UpdateTimelinePosition(at);
        SeekToSourceTime(nextSourceMs, pause: false, nextClip.SourcePath);
    }

    private void UpdateTimelinePosition(long timelineMs)
    {
        timelineMs = Math.Clamp(timelineMs, 0, _timelineDurationMs);
        _suppressTimelineSeek = true;
        Timeline.PositionMs = timelineMs;
        UpdateTimeDisplay(timelineMs);
        _suppressTimelineSeek = false;
    }

    private void StopPlaybackAtEnd()
    {
        _mediaPlayer.SetPause(true);
        _isPlaying = false;
        UpdatePlayButtonIcon(false);
        _activeClipId = null;
    }

    private void UpdatePlayButtonIcon(bool playing) =>
        PlayPauseButton.Content = playing ? "\uE769" : "\uE768";

    private void UpdateTimeDisplay(long currentMs)
    {
        TimeDisplay.Text = $"{TimeHelper.FormatMs(currentMs)} / {TimeHelper.FormatMs(_timelineDurationMs)}";
    }

    private void UpdatePlayButton(bool playing)
    {
        _isPlaying = playing;
        UpdatePlayButtonIcon(playing);
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaPlayer.Media == null)
            return;

        if (_isPlaying)
        {
            _mediaPlayer.SetPause(true);
            _isPlaying = false;
            UpdatePlayButtonIcon(false);
            return;
        }

        if (!Timeline.TryMapTimelineToSource(Timeline.PositionMs, out var clip, out var sourceMs) || clip == null)
            return;

        _activeClipId = clip.Id;
        SeekToSourceTime(sourceMs, pause: false, clip.SourcePath);
        _mediaPlayer.Play();
        _isPlaying = true;
        UpdatePlayButtonIcon(true);
    }

    private void PrevFrameButton_Click(object sender, RoutedEventArgs e) => StepFrame(-1);

    private void NextFrameButton_Click(object sender, RoutedEventArgs e) => StepFrame(1);

    private void StepFrame(int direction)
    {
        if (_mediaPlayer.Media == null)
            return;

        if (_isPlaying)
        {
            _mediaPlayer.Pause();
            _isPlaying = false;
        }

        var frameMs = TimeHelper.FrameDurationMs(_fps);
        var newTimeline = Math.Clamp(Timeline.PositionMs + direction * frameMs, 0, _timelineDurationMs);
        SeekToTimeline(newTimeline, pause: true);
    }

    private void SeekToTimeline(long timelineMs, bool pause)
    {
        if (!Timeline.TryMapTimelineToSource(timelineMs, out var clip, out var sourceMs) || clip == null)
            return;

        _suppressTimelineSeek = true;
        Timeline.PositionMs = timelineMs;
        UpdateTimeDisplay(timelineMs);
        _suppressTimelineSeek = false;

        _activeClipId = clip.Id;
        SeekToSourceTime(sourceMs, pause, clip.SourcePath);
    }

    private void SeekToSourceTime(long sourceMs, bool pause, string? sourcePath = null)
    {
        sourcePath ??= _videoPath;
        sourceMs = Math.Max(0, sourceMs);

        if (sourcePath != _videoPath || _media == null)
        {
            _media?.Dispose();
            _videoPath = sourcePath;
            _media = new Media(_libVlc, sourcePath, FromType.FromPath);
            _mediaPlayer.Media = _media;
        }

        if (!pause && !_mediaPlayer.IsPlaying)
            _mediaPlayer.Play();

        if (Math.Abs(_mediaPlayer.Time - sourceMs) > 35)
            _mediaPlayer.Time = sourceMs;

        if (pause)
            _mediaPlayer.SetPause(true);
    }

    private void Timeline_PositionChanged(object sender, long ms)
    {
        if (_mediaPlayer.Media == null)
            return;

        SeekToTimeline(ms, pause: true);
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_mediaPlayer != null)
            _mediaPlayer.Volume = (int)e.NewValue;
    }

    private void Timeline_ScreenshotRequested(object sender, EventArgs e)
    {
        if (_mediaPlayer.Media == null)
            return;

        if (_isPlaying)
        {
            _mediaPlayer.Pause();
            _isPlaying = false;
        }

        SeekToTimeline(Timeline.PositionMs, pause: true);

        var dialog = new SaveFileDialog
        {
            Title = "Save Screenshot",
            Filter = "PNG Image|*.png|JPEG Image|*.jpg",
            FileName = $"VTRIMZ_{DateTime.Now:yyyyMMdd_HHmmss}.png",
            DefaultExt = ".png"
        };

        if (dialog.ShowDialog() != true)
            return;

        Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(120);
            _mediaPlayer.TakeSnapshot(0, dialog.FileName, 0, 0);

            if (File.Exists(dialog.FileName))
            {
                MessageBox.Show($"Screenshot saved:\n{dialog.FileName}", "VTRIMZ",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }, DispatcherPriority.Background);
    }

    private async void ExportVideo()
    {
        if (Timeline.Clips.Count == 0)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Export Video",
            Filter = "MP4 Video|*.mp4",
            FileName = $"VTRIMZ_export_{DateTime.Now:yyyyMMdd_HHmmss}.mp4",
            DefaultExt = ".mp4"
        };

        if (dialog.ShowDialog() != true)
            return;

        if (_isPlaying)
        {
            _mediaPlayer.Pause();
            _isPlaying = false;
            UpdatePlayButton(false);
        }

        var owner = Window.GetWindow(this);
        var exportDialog = new ExportDialog(owner!);
        exportDialog.Show();

        try
        {
            var progress = new Progress<string>(msg => exportDialog.UpdateStatus(msg));
            var clips = Timeline.Clips.Select(c => c.Clone()).ToList();

            await ExportService.ExportAsync(clips, dialog.FileName, progress);

            exportDialog.Close();
            MessageBox.Show($"Video exported successfully:\n{dialog.FileName}", "VTRIMZ",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            exportDialog.Close();
            MessageBox.Show($"Export failed:\n{ex.Message}", "VTRIMZ",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnPlaybackEnded()
    {
        if (!_isPlaying || !_activeClipId.HasValue)
            return;

        var clip = Timeline.Clips.FirstOrDefault(c => c.Id == _activeClipId.Value);
        if (clip == null)
        {
            StopPlaybackAtEnd();
            return;
        }

        AdvancePastClipEnd(clip);

        if (_isPlaying && !_mediaPlayer.IsPlaying)
            _mediaPlayer.Play();
    }

    private void DisposeMedia()
    {
        _positionTimer.Stop();
        if (_mediaPlayer.IsPlaying)
            _mediaPlayer.Stop();

        _media?.Dispose();
        _media = null;
    }

    public void Dispose()
    {
        DisposeMedia();
        _positionTimer.Stop();
        VideoView.MediaPlayer = null!;
        _mediaPlayer.Dispose();
    }
}
