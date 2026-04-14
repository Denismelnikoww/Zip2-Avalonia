using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Zip2_Avalonia;

public partial class MessageBox : Window
{
    public string? Content { get; set; }

    public MessageBox()
    {
        InitializeComponent();
        DataContext = this;
    }

    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);
        MessageText.Text = Content ?? "";
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}