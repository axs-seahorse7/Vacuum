
using System.Windows;
using VacuumPumpSystem.Services;

namespace VacuumPumpSystem.Views.Admin;

public partial class EditLineWindow : Window
{
    private readonly ProductionLine _line;

    public string LineCode => LineCodeTextBox.Text.Trim();
    public string LineName => LineNameTextBox.Text.Trim();
    public string Description => DescriptionTextBox.Text.Trim();

    public EditLineWindow(ProductionLine line)
    {
        InitializeComponent();

        _line = line;

        // Populate the form with the selected line.
        LineCodeTextBox.Text = line.LineCode;
        LineNameTextBox.Text = line.LineName;
        DescriptionTextBox.Text = line.Description;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LineCode))
        {
            MessageBox.Show("Line code is required.",
                "Validation", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            LineCodeTextBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(LineName))
        {
            MessageBox.Show("Line name is required.",
                "Validation", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            LineNameTextBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
