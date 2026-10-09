
using System.Windows;
using System.Windows.Input;
using VacuumPumpSystem.Services;

namespace VacuumPumpSystem.Views.Admin;

public partial class SetLineChannelWindow : Window
{
    public int? SelectedChannel { get; private set; }

    public SetLineChannelWindow(ProductionLine line)
    {
        InitializeComponent();

        LineTitle.Text = $"LoRa Channel — {line.LineName}";

        ChannelTextBox.Text = line.LoraChannel?.ToString() ?? "";
        ChannelTextBox.SelectAll();

        Loaded += (_, _) =>
        {
            ChannelTextBox.Focus();
            Keyboard.Focus(ChannelTextBox);
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string value = ChannelTextBox.Text.Trim();

        if (string.IsNullOrEmpty(value))
        {
            SelectedChannel = null;
            DialogResult = true;
            return;
        }

        if (!int.TryParse(value, out int channel) ||
            channel < 0 ||
            channel > 65535)
        {
            MessageBox.Show(
                "Enter a valid whole-number channel from 0 to 65535.",
                "Invalid Channel",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        SelectedChannel = channel;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
