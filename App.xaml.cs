using System.Windows;
using Forms = System.Windows.Forms;

namespace SatelliteEyesWin;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Forms.Application.EnableVisualStyles();
        Forms.Application.SetHighDpiMode(Forms.HighDpiMode.SystemAware);
        base.OnStartup(e);
    }
}
