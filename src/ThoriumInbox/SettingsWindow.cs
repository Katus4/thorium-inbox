using ThoriumInbox.Core;

namespace ThoriumInbox;

internal sealed class SettingsWindow : Form
{
    private readonly TextBox folder = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AccessibleName = "Watched folder path",
        Margin = new Padding(0, 5, 8, 0),
    };
    private readonly Label activity = new()
    {
        AutoSize = true,
        MaximumSize = new Size(510, 0),
        Text = "Watching for PDFs and EPUBs.",
        Margin = new Padding(0, 3, 0, 10),
    };

    public SettingsWindow(Icon icon, Action changeFolder, Action openInbox, Action openEpubs, Action viewActivity)
    {
        Text = "Thorium Inbox";
        Icon = icon;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(568, 320);
        MinimumSize = new Size(545, 340);
        MaximizeBox = false;
        BackColor = SystemColors.Window;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(23, 20, 23, 18),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var title = TextLabel("Thorium Inbox", 17, true);
        layout.Controls.Add(title);
        layout.Controls.Add(TextLabel("Save PDFs here to turn them into EPUBs and add them to Thorium.", margin: new Padding(0, 3, 0, 19)));

        layout.Controls.Add(TextLabel("Watched folder", bold: true));
        var pathRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 2, 0, 3) };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.Controls.Add(folder, 0, 0);
        pathRow.Controls.Add(ActionButton("Change folder…", changeFolder), 1, 0);
        layout.Controls.Add(pathRow);
        layout.Controls.Add(Buttons(ActionButton("Open inbox", openInbox), ActionButton("Open EPUBs", openEpubs)));

        layout.Controls.Add(new Panel { Height = 1, Dock = DockStyle.Top, Margin = new Padding(0, 13, 0, 13), BackColor = SystemColors.ControlLight });
        layout.Controls.Add(TextLabel("Latest activity", bold: true));
        layout.Controls.Add(activity);
        layout.Controls.Add(Buttons(ActionButton("View activity", viewActivity)));
        Controls.Add(layout);

        FormClosing += (_, args) =>
        {
            if (args.CloseReason != CloseReason.UserClosing)
                return;
            args.Cancel = true;
            Hide();
        };
    }

    public void SetFolder(string path) => folder.Text = path;

    public void SetActivity(InboxActivity update) => activity.Text = update.Message;

    private static Label TextLabel(string text, float size = 9, bool bold = false, Padding? margin = null) =>
        new()
        {
            Text = text,
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular),
            Margin = margin ?? Padding.Empty,
        };

    private static Button ActionButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(6, 2, 6, 2), Margin = new Padding(0, 0, 7, 0) };
        button.Click += (_, _) => action();
        return button;
    }

    private static FlowLayoutPanel Buttons(params Button[] buttons)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        row.Controls.AddRange(buttons);
        return row;
    }
}
