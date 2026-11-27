using System;
using System.Threading;
using System.Windows.Forms;

namespace SmartCamLight
{
    static partial class Program
    {
        // --preview: a read-only run for trying the interface. Nothing is sent to the light or
        // the camera, and no settings, log or autostart entry are written.
        public static bool Preview;

        // Hook for development tooling (for example a screenshot harness compiled in alongside).
        // Without an implementation the call below compiles away.
        static partial void DevStarted(TrayApp app, string[] args);

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--write-icon")
            {
                IconArt.WriteIco(args[1]);
                return 0;
            }

            Preview = Has(args, "--preview");
            if (Has(args, "--light")) Ui.ThemeOverride = 1;      // otherwise the theme follows Windows
            if (Has(args, "--dark")) Ui.ThemeOverride = 2;
            if (Has(args, "--solid")) Ui.NoBackdrop = true;      // plain background instead of Mica

            bool created;
            string eventName = "SmartCamLight_ShowSettings_v1" + (Preview ? "_preview" : "");
            using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName, out created))
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

                bool tray = Has(args, "--tray");
                Log.Write("Starting" + (tray ? " (tray)" : "") + (Preview ? " (preview)" : ""));
                var app = new TrayApp(showEvent, !tray);
                DevStarted(app, args);
                Application.Run(app);
            }
            return 0;
        }

        static bool Has(string[] args, string flag)
        {
            return Array.Exists(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
        }
    }
}
