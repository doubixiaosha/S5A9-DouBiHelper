using System.Windows;
using DnfHelper.UI.Models;
using DnfHelper.UI.Services;
using DnfHelper.UI.ViewModels;
using DnfHelper.UI.Views;

namespace DnfHelper.UI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var config = ConfigService.Load();
            ThemeService.Apply(config.Theme);

            var viewModel = new MainViewModel(config);
            string error;
            if (CoreBridge.Start(out error))
            {
                viewModel.InitializeCore();
                viewModel.AutoAttach();
            }

            var window = new MainWindow(viewModel);
            MainWindow = window;
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            CoreBridge.Shutdown();
            base.OnExit(e);
        }
    }
}
