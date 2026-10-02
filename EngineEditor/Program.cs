using Avalonia;
using System;

namespace EngineEditor
{
    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(args);
        }
    }
}