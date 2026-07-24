using System.Windows;

namespace NeuroTune;

public partial class App : Application
{
    public App() => DispatcherUnhandledException += (_, e) =>
    {
        LogService.Write($"Errore non gestito: {e.Exception.GetType().Name}: {e.Exception.Message}");
        MessageBox.Show(e.Exception.Message, "NeuroTune", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    };
}
