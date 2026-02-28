using DictateTray.Core.Logging;
using System.Runtime.InteropServices;

namespace DictateTray.Core.Insertion;

public sealed class ClipboardPasteInserter
{
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const int InputKeyboard = 1;

    private readonly IAppLogger _logger;

    public ClipboardPasteInserter(IAppLogger logger)
    {
        _logger = logger;
    }

    public Task<bool> InsertTextAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(false);
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                    return;
                }

                var backup = TryGetClipboardBackup();
                var setOk = RetryClipboardAction(() =>
                {
                    Clipboard.SetText(text);
                    return true;
                });

                if (!setOk)
                {
                    _logger.Warn("Failed to set clipboard text for insertion.");
                    tcs.TrySetResult(false);
                    return;
                }

                var pasted = SendCtrlV();
                if (!pasted)
                {
                    _logger.Warn("SendInput Ctrl+V failed.");
                }

                Thread.Sleep(80);
                RestoreClipboard(backup);
                tcs.TrySetResult(pasted);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Clipboard insertion failed.");
                tcs.TrySetResult(false);
            }
        })
        {
            IsBackground = true,
            Name = "ClipboardInserterSTA"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return tcs.Task;
    }

    private static IDataObject? TryGetClipboardBackup()
    {
        return RetryClipboardAction(() => Clipboard.GetDataObject());
    }

    private static void RestoreClipboard(IDataObject? backup)
    {
        if (backup is null)
        {
            return;
        }

        RetryClipboardAction(() =>
        {
            Clipboard.SetDataObject(backup, true);
            return true;
        });
    }

    private static T? RetryClipboardAction<T>(Func<T> action)
    {
        const int maxAttempts = 8;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return action();
            }
            catch (ExternalException)
            {
                Thread.Sleep(25);
            }
            catch (COMException)
            {
                Thread.Sleep(25);
            }
            catch (ThreadStateException)
            {
                Thread.Sleep(25);
            }
        }

        return default;
    }

    private static bool SendCtrlV()
    {
        INPUT[] inputs =
        [
            CreateKeyInput(VK_CONTROL, keyUp: false),
            CreateKeyInput(VK_V, keyUp: false),
            CreateKeyInput(VK_V, keyUp: true),
            CreateKeyInput(VK_CONTROL, keyUp: true)
        ];

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return sent == inputs.Length;
    }

    private static INPUT CreateKeyInput(ushort vk, bool keyUp)
    {
        return new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0
                }
            }
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
