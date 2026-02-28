using DictateTray.Core.Configuration;
using DictateTray.Interop;

namespace DictateTray.Hotkeys;

internal sealed class GlobalHotkeyManager : NativeWindow, IDisposable
{
    private const int ToggleId = 0x1000;
    private const int PushToTalkId = 0x1001;

    private readonly System.Windows.Forms.Timer _pttReleaseTimer;
    private bool _isDisposed;
    private bool _pttDown;

    public event EventHandler? TogglePressed;

    public event EventHandler? PushToTalkPressed;

    public event EventHandler? PushToTalkReleased;

    public GlobalHotkeyManager()
    {
        CreateHandle(new CreateParams());

        _pttReleaseTimer = new System.Windows.Forms.Timer { Interval = 40 };
        _pttReleaseTimer.Tick += (_, _) => PollPushToTalkRelease();
    }

    public bool Register(HotkeySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var toggleOk = NativeMethods.RegisterHotKey(
            Handle,
            ToggleId,
            NormalizeModifiers(settings.Toggle.Modifiers),
            settings.Toggle.VirtualKey);

        var pttOk = NativeMethods.RegisterHotKey(
            Handle,
            PushToTalkId,
            NormalizeModifiers(settings.PushToTalk.Modifiers),
            settings.PushToTalk.VirtualKey);

        return toggleOk && pttOk;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            var id = m.WParam.ToInt32();
            if (id == ToggleId)
            {
                TogglePressed?.Invoke(this, EventArgs.Empty);
            }
            else if (id == PushToTalkId && !_pttDown)
            {
                _pttDown = true;
                PushToTalkPressed?.Invoke(this, EventArgs.Empty);
                _pttReleaseTimer.Start();
            }
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _pttReleaseTimer.Stop();
        _pttReleaseTimer.Dispose();

        NativeMethods.UnregisterHotKey(Handle, ToggleId);
        NativeMethods.UnregisterHotKey(Handle, PushToTalkId);

        if (Handle != IntPtr.Zero)
        {
            DestroyHandle();
        }
    }

    private static uint NormalizeModifiers(uint modifiers)
    {
        return modifiers | NativeMethods.MOD_NOREPEAT;
    }

    private void PollPushToTalkRelease()
    {
        if (!_pttDown)
        {
            return;
        }

        var isSpaceDown = IsKeyDown(NativeMethods.VK_SPACE);
        if (isSpaceDown)
        {
            return;
        }

        _pttDown = false;
        _pttReleaseTimer.Stop();
        PushToTalkReleased?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsKeyDown(int key)
    {
        return (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
    }
}
