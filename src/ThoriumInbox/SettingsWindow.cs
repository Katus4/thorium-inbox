using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ThoriumInbox;

internal sealed class SettingsWindow : Window
{
    private readonly TextBox folder = ReadOnlyPath("Watched folder path");
    private readonly TextBox reader = ReadOnlyPath("Thorium Reader executable");
    private readonly CheckBox startup = new() { Content = "Start at login" };
    private readonly TextBlock startupHint = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly TextBlock activity = new() { TextWrapping = TextWrapping.Wrap };
    private readonly SelectableTextBlock update = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly WrapPanel controls = new();
    private bool settingStartup;

    public SettingsWindow(WindowIcon icon, Action changeFolder, Action chooseReader, Action openInbox, Action openEpubs,
        Action viewActivity, Action<bool> changeStartup, Action checkUpdates, Action quit)
    {
        Title = "Thorium Inbox";
        Icon = icon;
        Width = 580;
        Height = 515;
        MinWidth = 430;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var content = new StackPanel { Spacing = 12, Margin = new Thickness(24) };
        content.Children.Add(new TextBlock { Text = "Thorium Inbox", FontSize = 23, FontWeight = FontWeight.SemiBold });
        content.Children.Add(new TextBlock
        {
            Text = "Add PDFs and EPUBs to this folder to send them to Thorium Reader.",
            TextWrapping = TextWrapping.Wrap,
        });

        content.Children.Add(PathRow("Watched folder", folder, Button("Change folder…", changeFolder)));
        content.Children.Add(PathRow("Thorium Reader", reader, Button("Choose Reader…", chooseReader)));
        content.Children.Add(new WrapPanel
        {
            Children = { Button("Open inbox", openInbox), Button("Open EPUBs", openEpubs), Button("View activity", viewActivity) },
        });

        startup.IsCheckedChanged += (_, _) =>
        {
            if (!settingStartup)
                changeStartup(startup.IsChecked == true);
        };
        content.Children.Add(startup);
        content.Children.Add(startupHint);
        content.Children.Add(new TextBlock { Text = "Latest activity", FontWeight = FontWeight.SemiBold });
        content.Children.Add(activity);
        controls.Children.Add(Button("Check for updates…", checkUpdates));
        controls.Children.Add(Button("Quit", quit));
        content.Children.Add(controls);
        content.Children.Add(update);
        Content = new ScrollViewer { Content = content };
    }

    public void SetFolder(string path) => folder.Text = path;

    public void SetReader(string? path) => reader.Text = path ?? "Automatically detected during import";

    public void SetActivity(string message) => activity.Text = message;

#if THORIUM_TEST_BUILD
    public void AddUpdatePreview(Action preview) => controls.Children.Insert(1, Button("Preview update…", preview));
#endif

    public void SetUpdateStatus(string? message)
    {
        update.Text = message;
        update.IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    public void SetStartup(bool available, bool enabled)
    {
        settingStartup = true;
        startup.IsEnabled = available;
        startup.IsChecked = enabled;
        startupHint.Text = available && OperatingSystem.IsMacOS()
            ? "Manage permission for login startup in macOS Login Items."
            : "Login startup is available after installing the app.";
        startupHint.IsVisible = !available || OperatingSystem.IsMacOS();
        settingStartup = false;
    }

    private static TextBox ReadOnlyPath(string accessibleName)
    {
        var path = new TextBox { IsReadOnly = true, MinWidth = 0 };
        AutomationProperties.SetName(path, accessibleName);
        return path;
    }

    private static StackPanel PathRow(string name, TextBox path, Button action)
    {
        var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        fields.Children.Add(path);
        Grid.SetColumn(action, 1);
        fields.Children.Add(action);
        return new StackPanel
        {
            Spacing = 5,
            Children = { new TextBlock { Text = name, FontWeight = FontWeight.SemiBold }, fields },
        };
    }

    private static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 3, 6, 3), VerticalAlignment = VerticalAlignment.Center };
        button.Click += (_, _) => action();
        return button;
    }
}
