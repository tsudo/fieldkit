using System.IO;
using System.Windows;

namespace FieldKit;

public partial class DisclaimerWindow : Window
{
    private static readonly string AcceptancePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FieldKit", "accepted.txt");

    public bool Accepted { get; private set; }

    public static bool HasAccepted()
    {
        try { return File.Exists(AcceptancePath); }
        catch { return false; }
    }

    private static void RecordAcceptance()
    {
        try
        {
            var dir = Path.GetDirectoryName(AcceptancePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(AcceptancePath, $"Accepted: {DateTime.Now:O}");
        }
        catch { }
    }

    public DisclaimerWindow()
    {
        InitializeComponent();
    }

    private void Agree_Click(object sender, RoutedEventArgs e)
    {
        Accepted = true;
        RecordAcceptance();
        DialogResult = true;
        Close();
    }

    private void Decline_Click(object sender, RoutedEventArgs e)
    {
        Accepted = false;
        DialogResult = false;
        Close();
    }
}
