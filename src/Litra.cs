using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SmartCamLight
{
    // Talks to Logitech Litra Glow / Litra Beam over raw HID (no Logi Tune needed).
    class LitraDevice : IDisposable
    {
        public string Path;
        public string Model;
        public SafeFileHandle Handle;
        public int OutputLength;

        public bool Write(byte[] cmd)
        {
            byte[] buf = new byte[Math.Max(20, OutputLength)];
            Array.Copy(cmd, buf, cmd.Length);
            int written;
            if (HidNative.WriteFile(Handle, buf, buf.Length, out written, IntPtr.Zero) && written > 0) return true;
            return HidNative.HidD_SetOutputReport(Handle, buf, buf.Length);
        }

        public void Dispose()
        {
            if (Handle != null) Handle.Dispose();
        }
    }

    class LitraManager : IDisposable
    {
        public const int MinLumen = 20, MaxLumen = 250;
        public const int MinTemp = 2700, MaxTemp = 6500;

        const ushort LogitechVid = 0x046D;
        static readonly Dictionary<ushort, string> Models = new Dictionary<ushort, string>
        {
            { 0xC900, "Litra Glow" },
            { 0xC901, "Litra Beam" },
        };

        readonly List<LitraDevice> devices = new List<LitraDevice>();
        List<string> knownPaths = new List<string>();

        public int Count { get { return devices.Count; } }

        public string Description
        {
            get
            {
                if (devices.Count == 0) return "No Litra light found";
                if (devices.Count == 1) return devices[0].Model + " connected";
                return devices.Count + " Litra lights connected";
            }
        }

        // Re-scans for lights. Returns true when the set of connected lights changed.
        public bool Refresh(bool force)
        {
            List<string> paths = FindPaths();
            // Each light exposes several HID collections; only one opens as the control
            // interface, so compare against every path we saw last time, not just the opened ones.
            if (!force && string.Join("|", paths) == string.Join("|", knownPaths)) return false;
            knownPaths = paths;

            foreach (var d in devices) d.Dispose();
            devices.Clear();

            foreach (string p in paths)
            {
                LitraDevice dev = Open(p);
                if (dev != null) devices.Add(dev);
            }
            return true;
        }

        public void SetPower(bool on) { Send(new byte[] { 0x11, 0xFF, 0x04, 0x1C, (byte)(on ? 1 : 0) }); }

        public void SetBrightnessPercent(int pct)
        {
            pct = Math.Max(0, Math.Min(100, pct));
            int lumen = MinLumen + (int)Math.Round((MaxLumen - MinLumen) * pct / 100.0);
            Send(new byte[] { 0x11, 0xFF, 0x04, 0x4C, (byte)(lumen >> 8), (byte)(lumen & 0xFF) });
        }

        public void SetTemperature(int kelvin)
        {
            kelvin = Math.Max(MinTemp, Math.Min(MaxTemp, (kelvin / 100) * 100));
            Send(new byte[] { 0x11, 0xFF, 0x04, 0x9C, (byte)(kelvin >> 8), (byte)(kelvin & 0xFF) });
        }

        void Send(byte[] cmd)
        {
            if (devices.Count == 0) Refresh(false);
            bool failed = false;
            foreach (var d in devices)
                if (!d.Write(cmd)) failed = true;

            if (failed)
            {
                // Handle went stale (unplugged/sleep). Re-open once and retry.
                Refresh(true);
                foreach (var d in devices) d.Write(cmd);
            }
        }

        static List<string> FindPaths()
        {
            var result = new List<string>();
            Guid hidGuid;
            HidNative.HidD_GetHidGuid(out hidGuid);
            IntPtr set = HidNative.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, HidNative.DIGCF_PRESENT | HidNative.DIGCF_DEVICEINTERFACE);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return result;
            try
            {
                for (int i = 0; ; i++)
                {
                    var data = new HidNative.SP_DEVICE_INTERFACE_DATA();
                    data.cbSize = Marshal.SizeOf(typeof(HidNative.SP_DEVICE_INTERFACE_DATA));
                    if (!HidNative.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref data)) break;

                    int required;
                    HidNative.SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out required, IntPtr.Zero);
                    if (required <= 0) continue;
                    IntPtr detail = Marshal.AllocHGlobal(required);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!HidNative.SetupDiGetDeviceInterfaceDetail(set, ref data, detail, required, out required, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
                        string lower = path.ToLowerInvariant();
                        foreach (var pid in Models.Keys)
                        {
                            if (lower.Contains(string.Format("vid_{0:x4}&pid_{1:x4}", LogitechVid, pid)))
                            {
                                result.Add(path);
                                break;
                            }
                        }
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { HidNative.SetupDiDestroyDeviceInfoList(set); }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        static LitraDevice Open(string path)
        {
            SafeFileHandle h = HidNative.CreateFile(path, HidNative.GENERIC_READ | HidNative.GENERIC_WRITE,
                HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE, IntPtr.Zero, HidNative.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid) { h.Dispose(); return null; }

            var attr = new HidNative.HIDD_ATTRIBUTES();
            attr.Size = Marshal.SizeOf(typeof(HidNative.HIDD_ATTRIBUTES));
            string model;
            if (!HidNative.HidD_GetAttributes(h, ref attr) || attr.VendorID != LogitechVid || !Models.TryGetValue(attr.ProductID, out model))
            {
                h.Dispose(); return null;
            }

            IntPtr pd;
            if (!HidNative.HidD_GetPreparsedData(h, out pd)) { h.Dispose(); return null; }
            HidNative.HIDP_CAPS caps;
            HidNative.HidP_GetCaps(pd, out caps);
            HidNative.HidD_FreePreparsedData(pd);

            // The control interface is the vendor-defined collection (usage page 0xFF43).
            if (caps.UsagePage != 0xFF43 || caps.OutputReportByteLength == 0) { h.Dispose(); return null; }

            return new LitraDevice { Path = path, Model = model, Handle = h, OutputLength = caps.OutputReportByteLength };
        }

        public void Dispose()
        {
            foreach (var d in devices) d.Dispose();
            devices.Clear();
        }
    }

    static class HidNative
    {
        public const int DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10;
        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
        [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr data, out HIDP_CAPS caps);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] buf, int len);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, int index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr devInfo);
        [DllImport("setupapi.dll")] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(SafeFileHandle h, byte[] buf, int len, out int written, IntPtr overlapped);
    }
}
