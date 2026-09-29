namespace WifiProfileSwitcher.Setup;

internal static class ConfirmDialog
{
    public static bool Show(Form owner, string title, string message, string action, bool destructive = false)
    {
        using var dialog = new Form
        {
            Text = title, Font = owner.Font, BackColor = owner.BackColor, ForeColor = owner.ForeColor,
            AutoScaleMode = AutoScaleMode.Dpi, FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false,
            ClientSize = new Size(440, 230)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = message, Dock = DockStyle.Fill, AutoSize = false }, 0, 0);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var proceed = new Button { Text = action, DialogResult = DialogResult.OK, AutoSize = true,
            MinimumSize = new Size(100, 34), FlatStyle = FlatStyle.Flat,
            BackColor = destructive ? Color.FromArgb(176, 45, 45) : Color.FromArgb(96, 205, 255),
            ForeColor = destructive ? Color.White : Color.FromArgb(24, 24, 24) };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, AutoSize = true,
            MinimumSize = new Size(80, 34), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(48, 48, 48), ForeColor = owner.ForeColor };
        buttons.Controls.AddRange([proceed, cancel]); layout.Controls.Add(buttons, 0, 1);
        dialog.Controls.Add(layout); dialog.CancelButton = cancel;
        dialog.AcceptButton = destructive ? cancel : proceed;
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }
}
