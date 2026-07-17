using Avalonia;
using System;

namespace EngineEditor
{
    class Program
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