using System.Windows;
using WpfCSharp.ViewModels;
namespace WpfCSharp.Views;
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
