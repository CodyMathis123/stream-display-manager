using System.Threading;

namespace StreamDisplayManager.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
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
