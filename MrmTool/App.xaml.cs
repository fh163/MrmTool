using Windows.UI.Xaml;
using Windows.Globalization;
using MrmTool.Common;

namespace MrmTool
{
    public partial class App : Application
    {
        public App()
        {
            try
            {
                ApplicationLanguages.PrimaryLanguageOverride = LocalizationService.ResolveStartupLanguage();
            }
            catch { }

            this.UnhandledException += (_, e) =>
            {
                e.Handled = true;
                CrashLogger.ShowErrorDialog(LocalizationService.GetString("App.Error.ProgramException"), e.Exception, "Application.UnhandledException");
            };

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                try
                {
                    if (e.ExceptionObject is Exception ex)
                    {
                        CrashLogger.LogException(ex, "AppDomain.UnhandledException");
                    }
                }
                catch { }
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                try
                {
                    CrashLogger.LogException(e.Exception, "TaskScheduler.UnobservedTaskException");
                    e.SetObserved();
                }
                catch { }
            };

            InitializeComponent();
        }
    }
}
