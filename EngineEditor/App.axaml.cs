using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using EngineEditor.Views;

namespace EngineEditor
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                string? playProjectPath = null;
                string[] args = desktop.Args ?? Array.Empty<string>();
                for (int index = 0; index < args.Length - 1; index++)
                {
                    if (args[index] == "--play")
                    {
                        playProjectPath = args[index + 1];
                        break;
                    }
                }

                desktop.MainWindow = new MainWindow(playProjectPath);
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}