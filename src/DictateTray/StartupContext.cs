using DictateTray.Core.Audio;
using DictateTray.Core.Configuration;
using DictateTray.Core.Insertion;
using DictateTray.Core.Logging;
using DictateTray.Core.Stt;
using DictateTray.Core.Text;
using DictateTray.Core.Vad;
using DictateTray.Core.Windows;
using DictateTray.Hotkeys;
using DictateTray.Tray;
using System.Threading.Channels;

namespace DictateTray;

internal sealed class StartupContext : ApplicationContext
{
    private readonly IAppLogger _logger;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly NotifyIcon _notifyIcon;
    private readonly TrayIconSet _iconSet;
    private readonly GlobalHotkeyManager _hotkeyManager;
    private readonly MicrophoneCaptureService _microphoneCapture;
    private VadSegmenter? _vadSegmenter;
    private readonly WhisperCliTranscriber _whisperTranscriber;
    private readonly TextPostProcessor _textPostProcessor;
    private readonly ForegroundProcessDetector _foregroundProcessDetector;
    private readonly ClipboardPasteInserter _clipboardPasteInserter;

    private readonly Channel<SpeechSegment> _segmentChannel;
    private readonly CancellationTokenSource _pipelineCts;
    private readonly Task _segmentWorker;
    private readonly SynchronizationContext _uiContext;

    private readonly ToolStripMenuItem _toggleMenuItem;
    private readonly ToolStripMenuItem _autoModeItem;
    private readonly ToolStripMenuItem _normalModeItem;
    private readonly ToolStripMenuItem _codeModeItem;

    private bool _toggleListening;
    private bool _pttListening;
    private bool _busy;

    public StartupContext(IAppLogger logger, SettingsService settingsService, AppSettings settings)
    {
        _logger = logger;
        _settingsService = settingsService;
        _settings = settings;
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _iconSet = new TrayIconSet();
        _microphoneCapture = new MicrophoneCaptureService(_logger);
        _microphoneCapture.ChunkAvailable += OnAudioChunkAvailable;
        RecreateVadSegmenter();

        _whisperTranscriber = new WhisperCliTranscriber(_logger);
        _textPostProcessor = new TextPostProcessor();
        _foregroundProcessDetector = new ForegroundProcessDetector();
        _clipboardPasteInserter = new ClipboardPasteInserter(_logger);
        _segmentChannel = Channel.CreateUnbounded<SpeechSegment>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _pipelineCts = new CancellationTokenSource();
        _segmentWorker = Task.Run(() => SegmentWorkerAsync(_pipelineCts.Token));

        _toggleMenuItem = new ToolStripMenuItem("Turn On", null, (_, _) => ToggleListening());

        _autoModeItem = new ToolStripMenuItem("Auto", null, (_, _) => SetMode(DictationMode.Auto));
        _normalModeItem = new ToolStripMenuItem("Normal", null, (_, _) => SetMode(DictationMode.Normal));
        _codeModeItem = new ToolStripMenuItem("Code", null, (_, _) => SetMode(DictationMode.Code));

        var modeMenu = new ToolStripMenuItem("Mode");
        modeMenu.DropDownItems.Add(_autoModeItem);
        modeMenu.DropDownItems.Add(_normalModeItem);
        modeMenu.DropDownItems.Add(_codeModeItem);

        var settingsItem = new ToolStripMenuItem("Settings", null, (_, _) => OpenSettings());
        var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitThread());

        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Text = "DictateTray",
            ContextMenuStrip = new ContextMenuStrip()
        };
        _notifyIcon.ContextMenuStrip.Items.Add(_toggleMenuItem);
        _notifyIcon.ContextMenuStrip.Items.Add(modeMenu);
        _notifyIcon.ContextMenuStrip.Items.Add(settingsItem);
        _notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _notifyIcon.ContextMenuStrip.Items.Add(exitItem);

        _hotkeyManager = new GlobalHotkeyManager();
        _hotkeyManager.TogglePressed += (_, _) => ToggleListening();
        _hotkeyManager.PushToTalkPressed += (_, _) => OnPushToTalkPressed();
        _hotkeyManager.PushToTalkReleased += (_, _) => OnPushToTalkReleased();

        var hotkeysRegistered = _hotkeyManager.Register(_settings.Hotkeys);
        _logger.Info(hotkeysRegistered
            ? "Global hotkeys registered."
            : "Failed to register one or more global hotkeys.");

        RefreshModeChecks();
        UpdateTrayState();
        _logger.Info($"Settings loaded from {AppPaths.SettingsFilePath}");
    }

    protected override void ExitThreadCore()
    {
        _logger.Info("Application exiting.");

        _pipelineCts.Cancel();
        _segmentChannel.Writer.TryComplete();
        try
        {
            _segmentWorker.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Ignore shutdown races.
        }

        if (_vadSegmenter is not null)
        {
            _vadSegmenter.SegmentFinalized -= OnSegmentFinalized;
            _vadSegmenter.Dispose();
        }
        _microphoneCapture.Dispose();
        _hotkeyManager.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _iconSet.Dispose();
        _pipelineCts.Dispose();

        base.ExitThreadCore();
    }

    private void ToggleListening()
    {
        _toggleListening = !_toggleListening;
        _logger.Info(_toggleListening ? "Listening enabled." : "Listening disabled.");
        UpdateTrayState();
    }

    private void OnPushToTalkPressed()
    {
        _pttListening = true;
        _logger.Info("Push-to-talk pressed.");
        UpdateTrayState();
    }

    private void OnPushToTalkReleased()
    {
        _pttListening = false;
        _logger.Info("Push-to-talk released.");

        _vadSegmenter?.FinalizeNow("ptt_release");
        UpdateTrayState();
    }

    private void SetMode(DictationMode mode)
    {
        if (_settings.Mode == mode)
        {
            return;
        }

        _settings.Mode = mode;
        _settingsService.Save(_settings);
        _logger.Info($"Mode set to {mode}.");
        RefreshModeChecks();
    }

    private void RefreshModeChecks()
    {
        _autoModeItem.Checked = _settings.Mode == DictationMode.Auto;
        _normalModeItem.Checked = _settings.Mode == DictationMode.Normal;
        _codeModeItem.Checked = _settings.Mode == DictationMode.Code;
    }

    private void UpdateTrayState()
    {
        var listening = _toggleListening || _pttListening;

        SyncCaptureState(listening);

        var state = _busy
            ? TrayState.Busy
            : listening ? TrayState.On : TrayState.Off;

        _notifyIcon.Icon = state switch
        {
            TrayState.Off => _iconSet.Off,
            TrayState.On => _iconSet.On,
            _ => _iconSet.Busy
        };

        _toggleMenuItem.Text = _toggleListening ? "Turn Off" : "Turn On";
    }

    private void SyncCaptureState(bool listening)
    {
        try
        {
            if (listening && !_microphoneCapture.IsCapturing)
            {
                _microphoneCapture.Start(_settings.MicrophoneDeviceId, _settings.DebugWriteWav);
                _settings.MicrophoneDeviceId = _microphoneCapture.CurrentDeviceId;
                _settings.MicrophoneDeviceName = _microphoneCapture.CurrentDeviceName;
                _settingsService.Save(_settings);
                _logger.Info($"Using microphone: {_settings.MicrophoneDeviceName}");
            }
            else if (!listening && _microphoneCapture.IsCapturing)
            {
                _vadSegmenter?.FinalizeNow("listening_stopped");
                _microphoneCapture.Stop();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to update capture state.");
        }
    }

    private void OnAudioChunkAvailable(object? sender, AudioChunkEventArgs e)
    {
        _vadSegmenter?.ProcessChunk(e.Samples);
    }

    private void OnSegmentFinalized(object? sender, SpeechSegmentEventArgs e)
    {
        _logger.Info(
            $"Segment ready: {e.Segment.DurationMs}ms [{e.Segment.FinalizeReason}] {e.Segment.WavPath}");
        _segmentChannel.Writer.TryWrite(e.Segment);
    }

    private async Task SegmentWorkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var segment in _segmentChannel.Reader.ReadAllAsync(cancellationToken))
            {
                await RunOnUiAsync(() =>
                {
                    _busy = true;
                    UpdateTrayState();
                });

                try
                {
                    var result = await _whisperTranscriber.TranscribeAsync(_settings, segment.WavPath, cancellationToken);
                    if (result is null)
                    {
                        continue;
                    }

                    var foregroundProcess = _foregroundProcessDetector.GetForegroundProcessName();
                    var post = _textPostProcessor.Process(result.Text, _settings.Mode, foregroundProcess);
                    _logger.Info(
                        $"Post-processed text ({post.EffectiveMode}, process={post.ForegroundProcess ?? "unknown"}): {post.Text}");

                    if (string.IsNullOrWhiteSpace(post.Text))
                    {
                        continue;
                    }

                    var inserted = await _clipboardPasteInserter.InsertTextAsync(post.Text, cancellationToken);
                    _logger.Info(inserted ? "Text insertion succeeded." : "Text insertion failed.");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Transcription failed for {segment.WavPath}");
                }
                finally
                {
                    await RunOnUiAsync(() =>
                    {
                        _busy = false;
                        UpdateTrayState();
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
    }

    private Task RunOnUiAsync(Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _uiContext.Post(_ =>
        {
            try
            {
                action();
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }, null);
        return tcs.Task;
    }

    private void OpenSettings()
    {
        using var settingsForm = new SettingsForm(_settings);
        if (settingsForm.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        var previousModelPath = _settings.ModelPath;
        var previousWhisperPath = _settings.WhisperExePath;
        var previousMode = _settings.Mode;

        _settings.ModelPath = settingsForm.ModelPath;
        _settings.WhisperExePath = settingsForm.WhisperExePath;
        _settings.Mode = settingsForm.Mode;
        _settingsService.Save(_settings);

        RefreshModeChecks();
        _logger.Info("Settings updated from UI.");

        var changed =
            !string.Equals(previousModelPath, _settings.ModelPath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(previousWhisperPath, _settings.WhisperExePath, StringComparison.OrdinalIgnoreCase) ||
            previousMode != _settings.Mode;

        if (changed)
        {
            RestartPipeline();
        }
    }

    private void RestartPipeline()
    {
        try
        {
            var listening = _toggleListening || _pttListening;
            if (_microphoneCapture.IsCapturing)
            {
                _vadSegmenter?.FinalizeNow("settings_changed");
                _microphoneCapture.Stop();
            }

            RecreateVadSegmenter();

            if (listening)
            {
                SyncCaptureState(true);
            }

            UpdateTrayState();
            _logger.Info("Audio pipeline restarted after settings change.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to restart pipeline after settings change.");
        }
    }

    private void RecreateVadSegmenter()
    {
        if (_vadSegmenter is not null)
        {
            _vadSegmenter.SegmentFinalized -= OnSegmentFinalized;
            _vadSegmenter.Dispose();
            _vadSegmenter = null;
        }

        try
        {
            _vadSegmenter = new VadSegmenter(_settings, _logger);
            _vadSegmenter.SegmentFinalized += OnSegmentFinalized;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "VAD initialization failed. Segmentation is disabled.");
        }
    }
}
