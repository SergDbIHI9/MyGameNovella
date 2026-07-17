using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
                // Теперь он увидит MainWindow и перестанет ругаться
                desktop.MainWindow = new MainWindow(); 
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}