using System.Windows;
using VacuumPumpSystem.Services;

namespace VacuumPumpSystem.Views.Admin;

public partial class AddUserWindow : Window
{
    public string Username { get; private set; } = "";

    public string Password { get; private set; } = "";

    public string Role { get; private set; } = "USER";

    public List<int> SelectedLineIds { get; private set; } = new();

    public AddUserWindow(List<ProductionLine> lines)
    {
        InitializeComponent();

        LinesListBox.ItemsSource = lines;
    }

    private void Create_Click(
        object sender,
        RoutedEventArgs e)
    {
        Username = UsernameTextBox.Text.Trim();
        Password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(Username))
        {
            MessageBox.Show(
                "Username is required.",
                "Validation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            MessageBox.Show(
                "Password is required.",
                "Validation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (Password.Length < 6)
        {
            MessageBox.Show(
                "Password must contain at least 6 characters.",
                "Validation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (RoleComboBox.SelectedItem is not System.Windows.Controls.ComboBoxItem roleItem)
        {
            MessageBox.Show(
                "Please select a role.",
                "Validation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        Role = roleItem.Content?.ToString() ?? "USER";

        SelectedLineIds = LinesListBox.SelectedItems
            .Cast<ProductionLine>()
            .Select(x => x.Id)
            .ToList();

        if (SelectedLineIds.Count == 0)
        {
            MessageBox.Show(
                "Please select at least one production line.",
                "Validation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}