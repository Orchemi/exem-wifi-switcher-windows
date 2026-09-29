using WifiProfileSwitcher.Windows;

namespace WifiProfileSwitcher.Setup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            if (args is ["--capture-ui", var directory])
            {
                Directory.CreateDirectory(directory);
                foreach (var scenario in new[] { (Stored: false, Blocked: false), (Stored: true, Blocked: false), (Stored: true, Blocked: true) })
                {
                    using var preview = new SetupForm(null, preview: true);
                    preview.LoadPreview(scenario.Stored, scenario.Blocked);
                    preview.Show();
                    Application.DoEvents();
                    using var bitmap = new Bitmap(preview.Width, preview.Height);
                    preview.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    using var output = new FileStream(Path.Combine(directory, scenario.Blocked ? "native-example-blocked.png" : scenario.Stored ? "native-example-saved.png" : "native-example-manual.png"), FileMode.CreateNew);
                    bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
                    preview.Close();
                }
                return 0;
            }
            Safety.RequireAdministrator();
            using var gate = new Mutex(true, @"Global\WifiProfileSwitcherSetup", out var owned);
            if (!owned) throw new SafeException("setup_already_running");
            using var payload = SetupPayload.Open();
            using var form = new SetupForm(payload);
            if (args is ["--verify-package"])
            {
                // CI-only read-only smoke path: no Shown event, capture, service, or network mutation.
                _ = form.Handle;
                form.PerformLayout();
                return 0;
            }
            if (args.Length != 0) throw new SafeException("invalid_command");
            System.Windows.Forms.Application.Run(form);
            return 0;
        }
        catch (Exception ex)
        {
            if (args is ["--verify-package"] or ["--capture-ui", _]) return 1;
            MessageBox.Show(SetupMessages.For(ex), "Wi-Fi Profile Switcher 설치", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
    }
}
