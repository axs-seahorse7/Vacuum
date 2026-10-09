using System.Windows;

namespace VacuumPumpSystem.Views;

public partial class ReceiverSelectionWindow : Window
{
    public string? SelectedPort { get; private set; }

    public ReceiverSelectionWindow(
        IEnumerable<string> ports)
    {
        InitializeComponent();

        PortsList.ItemsSource = ports.ToList();

        if (PortsList.Items.Count > 0)
        {
            PortsList.SelectedIndex = 0;
        }
    }

    private void Connect_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (PortsList.SelectedItem is not string port)
        {
            MessageBox.Show(
                "Please select a communication receiver.",
                "Receiver",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        SelectedPort = port;

        DialogResult = true;
    }

    private void Cancel_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = false;
    }
}