using System.Windows;

namespace GitSimpleRewriteHistory;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        System.Windows.Forms.Application.EnableVisualStyles();
    }
}
