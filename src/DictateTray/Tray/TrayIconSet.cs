using DictateTray.Interop;
using System.Drawing;

namespace DictateTray.Tray;

internal sealed class TrayIconSet : IDisposable
{
    public Icon Off { get; } = CreateDotIcon(Color.FromArgb(120, 120, 120));

    public Icon On { get; } = CreateDotIcon(Color.FromArgb(46, 184, 92));

    public Icon Busy { get; } = CreateDotIcon(Color.FromArgb(232, 181, 19));

    public void Dispose()
    {
        Off.Dispose();
        On.Dispose();
        Busy.Dispose();
    }

    private static Icon CreateDotIcon(Color color)
    {
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(color);
            using var border = new Pen(Color.FromArgb(40, 40, 40), 1f);
            graphics.FillEllipse(brush, 2, 2, 12, 12);
            graphics.DrawEllipse(border, 2, 2, 12, 12);
        }

        var iconHandle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(iconHandle);
            return (Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(iconHandle);
        }
    }
}
