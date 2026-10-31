using System;
using System.Windows;
using System.Windows.Threading;

namespace DiskChecker
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Ловим все необработанные исключения в UI-потоке
            DispatcherUnhandledException += (_, ex) =>
            {
                MessageBox.Show(
                    $"Необработанное исключение:\n\n{ex.Exception.GetType().Name}: {ex.Exception.Message}\n\n{ex.Exception.StackTrace}",
                    "Критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true; // не даём упасть, продолжаем работу
            };

            // Ловим исключения из Task/async без await
            AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            {
                MessageBox.Show(
                    $"Неперехваченное исключение:\n\n{ex.ExceptionObject}",
                    "Критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            };
        }
    }
}
