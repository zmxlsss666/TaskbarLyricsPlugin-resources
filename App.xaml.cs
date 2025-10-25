using System;
using System.Windows;

namespace TaskbarLyrics
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                MessageBox.Show($"Error: {args.ExceptionObject}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            };
        }
    }
}