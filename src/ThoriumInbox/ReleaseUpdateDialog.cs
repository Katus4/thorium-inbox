using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ThoriumInbox.Core;

namespace ThoriumInbox;

internal sealed class ReleaseUpdateDialog : Window
{
    private readonly TaskCompletionSource<bool> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool accepted;

    public ReleaseUpdateDialog(GitHubUpdate release, bool canInstall)
    {
        Title = "Thorium Inbox update";
        Width = 540;
        Height = 390;
        MinWidth = 385;
        MinHeight = 295;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 12,
            Margin = new Thickness(20),
        };
        layout.Children.Add(new TextBlock
        {
            Text = $"Version {release.Version} is available",
            FontWeight = FontWeight.SemiBold,
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
        });
        var notes = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Text = string.IsNullOrWhiteSpace(release.Notes) ? "Read more in the GitHub release." : release.Notes,
        };
        AutomationProperties.SetName(notes, "Release notes");
        Grid.SetRow(notes, 1);
        layout.Children.Add(notes);

        var later = new Button { Content = "Later" };
        later.Click += (_, _) => Close();
        var install = new Button { Content = canInstall ? "Install now" : "Get update now" };
        install.Click += (_, _) => { accepted = true; Close(); };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { later, install },
        };
        Grid.SetRow(actions, 2);
        layout.Children.Add(actions);
        Content = layout;
        Closed += (_, _) => decision.TrySetResult(accepted);
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
                Close();
        };
    }

    public Task<bool> AskAsync(Window owner)
    {
        Icon = owner.Icon;
        if (owner.IsVisible)
            _ = ShowDialog(owner);
        else
            Show();
        return decision.Task;
    }
}
