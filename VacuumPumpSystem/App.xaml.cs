using System.Windows;
using VacuumPumpSystem.Services;
using VacuumPumpSystem.Views;

namespace VacuumPumpSystem;

public partial class App : Application
{
    public App()
    {
        // Catch WPF UI-thread exceptions
        DispatcherUnhandledException += (sender, e) =>
        {
            MessageBox.Show(
                e.Exception.ToString(),
                "UNHANDLED WPF EXCEPTION",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = true;
        };

        // Catch non-UI unhandled exceptions
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            MessageBox.Show(
                e.ExceptionObject?.ToString() ?? "Unknown exception",
                "UNHANDLED EXCEPTION",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var migrationService = new DatabaseMigrationService();
            await migrationService.InitializeAsync();   

            var userService = new UserService();
            await userService.SeedAdminAsync();

            var loginWindow =
                new LoginWindow();

            loginWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Database initialization failed.\n\n{ex}",
                "Database Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown();
        }
    }
}