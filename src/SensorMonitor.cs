using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SmartCamLight
{
    // Uses Media Foundation's sensor activity monitor (Windows 10 2004+ / Windows 11).
    // Windows pushes a report whenever any process starts or stops streaming from a camera.
    static class SensorMonitor
    {
        static IMFSensorActivityMonitor monitor;
        static SensorCallback callback;
        static readonly object sync = new object();
        // symbolic link -> (friendly name, streaming PIDs)
        static readonly Dictionary<string, KeyValuePair<string, List<int>>> devices = new Dictionary<string, KeyValuePair<string, List<int>>>(StringComparer.OrdinalIgnoreCase);

        public static bool Running { get; private set; }
        public static bool Debug = false;

        public static bool Start()
        {
            try
            {
                MFStartup(0x20070, 0);
                callback = new SensorCallback();
                int hr = MFCreateSensorActivityMonitor(callback, out monitor);
                if (hr != 0 || monitor == null) { Log.Write("Sensor monitor unavailable, hr=0x" + hr.ToString("X8")); return false; }
                hr = monitor.Start();
                Running = hr == 0;
                Log.Write(Running ? "Sensor activity monitor started" : "Sensor monitor start failed, hr=0x" + hr.ToString("X8"));
                return Running;
            }
            catch (Exception ex)
            {
                Log.Write("Sensor monitor error: " + ex.Message);
                return false;
            }
        }

        public static void Stop()
        {
            try { if (monitor != null) monitor.Stop(); } catch { }
            Running = false;
        }

        // Process names currently streaming from any camera.
        public static List<string> ActiveApps()
        {
            var result = new List<string>();
            lock (sync)
            {
                foreach (var kv in devices)
                {
                    foreach (int pid in kv.Value.Value)
                    {
                        string name = ProcessName(pid);
                        if (name != null && !result.Contains(name)) result.Add(name);
                    }
                }
            }
            return result;
        }

        static string ProcessName(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                    return p.HasExited ? null : p.ProcessName;
            }
            catch { return null; }   // process is gone
        }

        internal static void OnReport(IMFSensorActivitiesReport report)
        {
            uint count;
            int hrc = report.GetCount(out count);
            if (Debug) Log.Write("Sensor report: hr=0x" + hrc.ToString("X") + " count=" + count);
            if (hrc != 0) return;
            lock (sync)
            {
                for (uint i = 0; i < count; i++)
                {
                    IMFSensorActivityReport r;
                    if (report.GetActivityReport(i, out r) != 0 || r == null) continue;
                    try
                    {
                        var sb = new StringBuilder(1024);
                        uint written;
                        r.GetSymbolicLink(sb, (uint)sb.Capacity, out written);
                        string link = sb.ToString();
                        sb = new StringBuilder(512);
                        r.GetFriendlyName(sb, (uint)sb.Capacity, out written);
                        string name = sb.ToString();

                        var pids = new List<int>();
                        uint pc;
                        if (r.GetProcessCount(out pc) == 0)
                        {
                            for (uint j = 0; j < pc; j++)
                            {
                                IMFSensorProcessActivity a;
                                if (r.GetProcessActivity(j, out a) != 0 || a == null) continue;
                                try
                                {
                                    uint pid; bool streaming;
                                    if (a.GetProcessId(out pid) == 0 && a.GetStreamingState(out streaming) == 0 && streaming)
                                        pids.Add((int)pid);
                                }
                                finally { Marshal.ReleaseComObject(a); }
                            }
                        }
                        if (Debug) Log.Write("  device '" + name + "' procs=" + pc + " streaming=[" + string.Join(",", pids) + "] link=" + link);
                        devices[link] = new KeyValuePair<string, List<int>>(name, pids);
                    }
                    finally { Marshal.ReleaseComObject(r); }
                }
            }
        }

        [DllImport("mfplat.dll")] static extern int MFStartup(int version, int flags);
        [DllImport("mfsensorgroup.dll")]
        static extern int MFCreateSensorActivityMonitor(IMFSensorActivitiesReportCallback cb, out IMFSensorActivityMonitor monitor);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public class SensorCallback : IMFSensorActivitiesReportCallback
    {
        public int OnActivitiesReport(IMFSensorActivitiesReport report)
        {
            try { SensorMonitor.OnReport(report); }
            catch (Exception ex) { Log.Write("Sensor report error: " + ex.Message); }
            finally { if (report != null) Marshal.ReleaseComObject(report); }
            return 0;
        }
    }

    [ComImport, ComVisible(true), Guid("DE5072EE-DBE3-46DC-8A87-B6F631194751"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSensorActivitiesReportCallback
    {
        [PreserveSig] int OnActivitiesReport(IMFSensorActivitiesReport report);
    }

    [ComImport, Guid("D0CEF145-B3F4-4340-A2E5-7A5080CA05CB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSensorActivityMonitor
    {
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
    }

    [ComImport, Guid("683F7A5E-4A19-43CD-B1A9-DBF4AB3F7777"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSensorActivitiesReport
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetActivityReport(uint index, out IMFSensorActivityReport report);
        [PreserveSig] int GetActivityReportByDeviceName([MarshalAs(UnmanagedType.LPWStr)] string name, out IMFSensorActivityReport report);
    }

    [ComImport, Guid("3E8C4BE1-A8C2-4528-90DE-2851BDE5FEAD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSensorActivityReport
    {
        [PreserveSig] int GetFriendlyName([MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, uint cch, out uint written);
        [PreserveSig] int GetSymbolicLink([MarshalAs(UnmanagedType.LPWStr)] StringBuilder link, uint cch, out uint written);
        [PreserveSig] int GetProcessCount(out uint count);
        [PreserveSig] int GetProcessActivity(uint index, out IMFSensorProcessActivity activity);
    }

    [ComImport, Guid("39DC7F4A-B141-4719-813C-A7F46162A2B8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSensorProcessActivity
    {
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int GetStreamingState([MarshalAs(UnmanagedType.Bool)] out bool streaming);
        [PreserveSig] int GetStreamingMode(out int mode);
        [PreserveSig] int GetReportTime(out long fileTime);
    }
}
