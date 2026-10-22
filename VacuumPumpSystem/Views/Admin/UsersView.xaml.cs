using System.Windows;
using System.Windows.Controls;
using VacuumPumpSystem.Services;
using VacuumPumpSystem.Models;

namespace VacuumPumpSystem.Views.Admin;

public partial class UsersView : UserControl
{
    private readonly UserService _userService = new();

    public UsersView()
    {
        InitializeComponent();
        Loaded += UsersView_Loaded;
    }

    private async void UsersView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= UsersView_Loaded;
        await LoadUsersAsync();
    }

    private async Task LoadUsersAsync()
    {
        try
        {
            var users = await _userService.GetUsersAsync();
            UsersGrid.ItemsSource = users;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to load users.\n\n{ex.Message}",
                "Users",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void AddUser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var lines = await _userService.GetActiveLinesAsync();

            var window = new AddUserWindow(lines)
            {
                Owner = Window.GetWindow(this)
            };

            if (window.ShowDialog() != true)
                return;

            await _userService.CreateUserAsync(
                window.Username,
                window.Password,
                window.Role,
                window.SelectedLineIds);

            await LoadUsersAsync();

            MessageBox.Show(
                "User created successfully.",
                "Users",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to create user.\n\n{ex.Message}",
                "Users",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ToggleUser_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        if (button.DataContext is not UserModel user)
            return;

        try
        {
            await _userService.SetUserActiveAsync(
                user.Id,
                !user.IsActive);

            await LoadUsersAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to update user.\n\n{ex.Message}",
                "Users",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
