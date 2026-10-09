using System.Windows;

namespace VacuumPumpSystem.Views;

public partial class RegisterMachineWindow : Window
{
    public string MachineName { get; private set; } = "";

    public RegisterMachineWindow(byte modbusAddress)
    {
        InitializeComponent();

        ModbusAddressTextBox.Text =
            modbusAddress.ToString();
    }

    private void Save_Click(
        object sender,
        RoutedEventArgs e)
    {
        string machineName =
            MachineNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(machineName))
        {
            MessageBox.Show(
                "Please enter a Machine Name.",
                "Validation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        MachineName = machineName;

        DialogResult = true;
    }

    private void Cancel_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = false;
    }
}