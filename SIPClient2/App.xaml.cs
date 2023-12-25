using log4net.Config;
using log4net;
using System.Configuration;
using System.Data;
using System.Windows;

namespace SIPClient2
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(App));

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Configure log4net
            XmlConfigurator.Configure(new System.IO.FileInfo("log4net.config"));

            log.Info("WPF Application Started");

            // Rest of your application startup code
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
            log.Info("WPF Application Exiting");
        }
    }

}
