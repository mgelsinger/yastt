using DictateTray.Core.Audio;
using DictateTray.Core.Configuration;
using DictateTray.Core.Logging;
using DictateTray.Core.Vad;
using DictateTray.Hotkeys;
using DictateTray.Tray;

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
    private readonly VadSegmenter? _vadSegmenter;

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

        _iconSet = new TrayIconSet();
        _microphoneCapture = new MicrophoneCaptureService(_logger);
        _microphoneCapture.ChunkAvailable += OnAudioChunkAvailable;

        try
        {
            _vadSegmenter = new VadSegmenter(_settings, _logger);
            _vadSegmenter.SegmentFinalized += OnSegmentFinalized;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "VAD initialization failed. Segmentation is disabled.");
        }

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
        _hotkeyManager.PushToTalkReleased += (_, _) => _ = OnPushToTalkReleasedAsync();

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

        _vadSegmenter?.Dispose();
        _microphoneCapture.Dispose();
        _hotkeyManager.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _iconSet.Dispose();

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

    private async Task OnPushToTalkReleasedAsync()
    {
        _pttListening = false;
        _logger.Info("Push-to-talk released.");

        _busy = true;
        UpdateTrayState();

        _vadSegmenter?.FinalizeNow("ptt_release");
        await Task.Delay(250);
        _busy = false;
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
    }

    private void OpenSettings()
    {
        using var settingsForm = new SettingsForm();
        settingsForm.ShowDialog();
    }
}
