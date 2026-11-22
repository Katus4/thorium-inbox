using ThoriumInbox.Core;

namespace ThoriumInbox;

internal sealed class ReleaseUpdateDialog : Form
{
    public ReleaseUpdateDialog(GitHubUpdate update)
    {
        Text = "Thorium Inbox update";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(400, 280);
        ClientSize = new Size(520, 350);
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        var heading = new Label { Text = $"Version {update.Version} is available", AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
        layout.Controls.Add(heading, 0, 0);
        var notes = new TextBox
        {
            AccessibleName = "Release notes",
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text = string.IsNullOrWhiteSpace(update.Notes)
                ? "Open the release on GitHub for details."
                : update.Notes.Replace("\r\n", "\n").Replace("\n", Environment.NewLine),
        };
        layout.Controls.Add(notes, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Padding = new Padding(0, 8, 0, 0),
        };
        var later = new Button { Text = "Later", DialogResult = DialogResult.Cancel, AutoSize = true };
        var get = new Button { Text = "Get update now", DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.Add(later);
        buttons.Controls.Add(get);
        layout.Controls.Add(buttons, 0, 2);
        AcceptButton = get;
        CancelButton = later;
    }
}
