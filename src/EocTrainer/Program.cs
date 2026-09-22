namespace EocTrainer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Anything that is not a known switch is handled by the head-less CLI.
        if (args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal))
        {
            return Core.Cli.Run(args);
        }

        var tab = 0;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--tab=", StringComparison.Ordinal)
                && int.TryParse(arg["--tab=".Length..], out var parsed))
            {
                tab = parsed;
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(tab));
        return 0;
    }
}
