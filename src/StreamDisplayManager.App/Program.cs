using System.Threading;

namespace StreamDisplayManager.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Independent recovery entry point works even when the tray process is
        // stuck. Run from the same interactive Windows user account.
        if (args.Length == 1 && (args[0] == "--restore" || args[0] == "--extend"))
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StreamDisplayManager", "logs");
            var controller = new VirtualOnlyDisplayController(new JsonLog(dir));
            bool ok = args[0] == "--restore"
                ? controller.Restore("manual-command") : controller.EmergencyExtend();
            Environment.ExitCode = ok ? 0 : 1;
            return;
        }

        // Local, interactive-user singleton. No service or privilege elevation.
        using var singleInstance = new Mutex(
            initiallyOwned: true,
            name: @"Local\StreamDisplayManager.Observer",
            createdNew: out bool acquired);

        if (!acquired)
            return;

        ApplicationConfiguration.Initialize();
        Application.Run(new ObserverContext());
    }
}
