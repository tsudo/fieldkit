using System.Windows;
using System.Windows.Controls;

namespace FieldKit.Pages;

/// <summary>Lazy subpages keep their selections and refresh only when visible.</summary>
internal sealed class SectionPage : UserControl, IPage
{
    private readonly ContentControl _host = new();
    private readonly Dictionary<string, RadioButton> _buttons = new();
    internal string InitialSection { get; set; }

    internal SectionPage(params (string Title, Func<UserControl> Create)[] sections)
    {
        InitialSection = sections[0].Title;
        var group = Guid.NewGuid().ToString("N");
        var strip = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var layout = new DockPanel();
        DockPanel.SetDock(strip, Dock.Top);
        layout.Children.Add(strip);
        layout.Children.Add(_host);
        Content = layout;
        foreach (var (title, create) in sections)
        {
            var page = new Lazy<UserControl>(create);
            var button = new RadioButton { Content = title, GroupName = group };
            button.SetResourceReference(StyleProperty, "Fk.Chip");
            button.Checked += (_, _) =>
            {
                _host.Content = page.Value;
                (page.Value as IPage)?.OnShown();
            };
            strip.Children.Add(button);
            _buttons.Add(title, button);
        }
    }

    internal void Show(string title) => _buttons[title].IsChecked = true;
    public void OnShown()
    {
        if (_host.Content is null) Show(InitialSection);
        else (_host.Content as IPage)?.OnShown();
    }
}
