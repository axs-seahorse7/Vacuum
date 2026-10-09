using System.Windows;
using VacuumPumpSystem.Services;

namespace VacuumPumpSystem.Views;

public partial class LoginWindow : Window
{
    private readonly UserService _userService = new();

    public LoginWindow()
    {
        InitializeComponent();
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        string username = UsernameTextBox.Text.Trim();
        string password = PasswordBox.Password;

        ErrorText.Text = "";

        if (string.IsNullOrWhiteSpace(username))
        {
            ErrorText.Text = "Username is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            ErrorText.Text = "Password is required.";
            return;
        }

        try
        {
            var user = await _userService.AuthenticateAsync(username, password);

            if (user == null)
            {
                ErrorText.Text = "Invalid username or password.";
                return;
            }

            var lines = await _userService.GetUserLinesAsync(user.Id);

            if (user.Role == "ADMIN")
            {
                var adminWindow = new AdminWindow();
                adminWindow.Show();

                Close();
                return;
            }

            if (user.Role == "USER")
            {
                if (lines.Count == 0)
                {
                    ErrorText.Text = "No production line is assigned to this user.";

                    return;
                }

                var userWindow = new UserWindow(user, lines);

                userWindow.Show();

                Close();
                return;
            }

            ErrorText.Text = "Invalid user role.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Login failed.\n\n{ex.Message}",
                "Login Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}