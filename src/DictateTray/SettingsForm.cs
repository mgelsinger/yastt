using DictateTray.Core.Configuration;

namespace DictateTray;

internal sealed class SettingsForm : Form
{
    private readonly TextBox _modelPathTextBox;
    private readonly TextBox _whisperExePathTextBox;
    private readonly ComboBox _modeComboBox;

    public SettingsForm(AppSettings settings)
    {
        Text = "DictateTray Settings";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 200);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _modelPathTextBox = new TextBox { Dock = DockStyle.Fill, Text = settings.ModelPath };
        _whisperExePathTextBox = new TextBox { Dock = DockStyle.Fill, Text = settings.WhisperExePath };
        _modeComboBox = new ComboBox
        {
            Dock = DockStyle.Left,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 180
        };
        _modeComboBox.Items.AddRange([DictationMode.Auto, DictationMode.Normal, DictationMode.Code]);
        _modeComboBox.SelectedItem = settings.Mode;

        layout.Controls.Add(new Label { Text = "Model Path", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(_modelPathTextBox, 1, 0);
        layout.Controls.Add(new Label { Text = "Whisper EXE", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(_whisperExePathTextBox, 1, 1);
        layout.Controls.Add(new Label { Text = "Mode", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        layout.Controls.Add(_modeComboBox, 1, 2);

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true
        };

        var saveButton = new Button { Text = "Save", DialogResult = DialogResult.OK, Width = 90 };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };

        buttonPanel.Controls.Add(saveButton);
        buttonPanel.Controls.Add(cancelButton);

        layout.Controls.Add(buttonPanel, 1, 3);

        Controls.Add(layout);

        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    public string ModelPath => _modelPathTextBox.Text.Trim();

    public string WhisperExePath => _whisperExePathTextBox.Text.Trim();

    public DictationMode Mode => _modeComboBox.SelectedItem is DictationMode mode ? mode : DictationMode.Auto;
}
