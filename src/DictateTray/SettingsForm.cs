namespace DictateTray;

internal sealed class SettingsForm : Form
{
    public SettingsForm()
    {
        Text = "DictateTray Settings";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(360, 140);

        var label = new Label
        {
            AutoSize = false,
            Text = "Settings UI will be added in a later step.",
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill
        };

        Controls.Add(label);
    }
}
