using BooksCrawler.ViewModels;
using System.Windows;

namespace BooksCrawler;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}