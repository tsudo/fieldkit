namespace FieldKit;

/// <summary>
/// First-run disclaimer dialog. Shows once; acceptance is stored in %APPDATA%.
/// </summary>
public class DisclaimerForm : Form
{
    private static readonly string AcceptancePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FieldKit", "accepted.txt");

    public static bool HasAccepted()
    {
        try { return File.Exists(AcceptancePath); }
        catch { return false; }
    }

    public static void RecordAcceptance()
    {
        try
        {
            var dir = Path.GetDirectoryName(AcceptancePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(AcceptancePath, $"Accepted: {DateTime.Now:O}");
        }
        catch { }
    }

    public bool Accepted { get; private set; }

    public DisclaimerForm()
    {
        Text = "FieldKit — License Agreement";
        Size = new Size(540, 440);
        MinimumSize = new Size(440, 360);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        Font = new Font("Segoe UI", 9.5f);

        var heading = new Label
        {
            Text = "FieldKit v1.0",
            Dock = DockStyle.Top,
            Height = 36,
            Padding = new Padding(16, 10, 16, 0),
            Font = new Font("Segoe UI", 11f, FontStyle.Bold)
        };

        var textBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Window,
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(16),
            Text = DisclaimerText
        };

        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = SystemColors.Control,
            Padding = new Padding(16, 10, 16, 10)
        };

        var acceptButton = new Button
        {
            Text = "Agree",
            Width = 90, Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.OK
        };
        acceptButton.Click += (_, _) =>
        {
            Accepted = true;
            RecordAcceptance();
            Close();
        };

        var declineButton = new Button
        {
            Text = "Decline",
            Width = 90, Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.Cancel
        };
        declineButton.Click += (_, _) =>
        {
            Accepted = false;
            Close();
        };

        buttonPanel.Layout += (_, _) =>
        {
            acceptButton.Left = buttonPanel.Width - acceptButton.Width - 16;
            acceptButton.Top = (buttonPanel.Height - acceptButton.Height) / 2;
            declineButton.Left = acceptButton.Left - declineButton.Width - 8;
            declineButton.Top = acceptButton.Top;
        };

        buttonPanel.Controls.AddRange([acceptButton, declineButton]);

        Controls.Add(textBox);
        Controls.Add(heading);
        Controls.Add(buttonPanel);

        AcceptButton = acceptButton;
        CancelButton = declineButton;
    }

    private const string DisclaimerText = """
        This software is provided "as is," without warranty of any kind, express or implied, including but not limited to the warranties of merchantability, fitness for a particular purpose, and noninfringement. In no event shall the author be liable for any claim, damages, or other liability arising from the use of this software.

        SYSTEM MODIFICATIONS

        FieldKit performs privileged operations that modify your system state, including but not limited to:

        - Deleting temporary files and emptying the Recycle Bin
        - Flushing DNS cache
        - Writing Windows Registry keys (Storage Sense configuration)
        - Running Windows Update, DISM, and System File Checker
        - Installing application updates via winget
        - Performing disk defragmentation or SSD retrim

        You assume all risk associated with running these operations.

        NOT AFFILIATED WITH MICROSOFT

        This application is an independent, open-source project. It is not affiliated with, endorsed by, or supported by Microsoft Corporation.

        NO TELEMETRY

        This application makes no network calls beyond those required by the maintenance tasks you select.

        Created by Keith S. Crawford (@tsudo). Licensed under the MIT License.
        """;
}
