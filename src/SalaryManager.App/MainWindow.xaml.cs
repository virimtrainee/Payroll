using System;
using System.Windows;

namespace SalaryManager.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = $"Salary Manager — {DateTime.Now:MMMM yyyy}";
    }
}
