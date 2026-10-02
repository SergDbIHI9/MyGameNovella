using System;
using System.IO;
using System.Linq;

namespace EnginePlayer;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        string[] playerArgs;
        if (args.Contains("--play", StringComparer.Ordinal))
        {
            playerArgs = args;
        }
        else if (args.Length > 0)
        {
            playerArgs = new[] { "--play", Path.GetFullPath(args[0]) };
        }
        else
        {
            playerArgs = new[] { "--play", Path.Combine(AppContext.BaseDirectory, "Content", "game.novel") };
        }

        EngineEditor.Program.Main(playerArgs);
    }
}
