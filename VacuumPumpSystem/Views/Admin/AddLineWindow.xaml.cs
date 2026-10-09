using System.Windows;

namespace VacuumPumpSystem.Views.Admin;

public partial class AddLineWindow : Window
{
    public string LineCode =>
        LineCodeTextBox.Text.Trim();

    public string LineName =>
        LineNameTextBox.Text.Trim();

    public string Description =>
        DescriptionTextBox.Text.Trim();

    public AddLineWindow()
    {
        InitializeComponent();
    }

    private void Save_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LineCode))
        {
            MessageBox.Show(
                "Line code is required.",
                "Validation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (string.IsNullOrWhiteSpace(LineName))
        {
            MessageBox.Show(
                "Line name is required.",
                "Validation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = false;
    }
}