using System;
using System.Threading;
using System.Windows.Forms;

namespace SmartCamLight
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--write-icon")
            {
                IconArt.WriteIco(args[1]);
                return 0;
            }

            bool created;
            using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "SmartCamLight_ShowSettings_v1", out created))
            {
                if (!created)
                {
                    // Already running: ask that instance to open its settings window.
                    showEvent.Set();
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Ui.Init();
                Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e) { Log.Write("Unhandled: " + e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { Log.Write("Fatal: " + e.ExceptionObject); };

                bool tray = Array.Exists(args, a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
                Log.Write("Starting" + (tray ? " (tray)" : ""));
                Application.Run(new TrayApp(showEvent, !tray));
            }
            return 0;
        }
    }
}
