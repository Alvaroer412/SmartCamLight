using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace SmartCamLight
{
    class CamDevice
    {
        public string Name;
        public string Id;
        public override string ToString() { return Name; }
    }

    class CamProp
    {
        public string Group;   // "VPA" (image) or "CC" (camera control)
        public int Id;
        public string Name;
        public int Min, Max, Step, Default, Caps;
        public bool CanAuto { get { return (Caps & 1) != 0; } }
        public bool CanManual { get { return (Caps & 2) != 0 || Caps == 0; } }
        public string Key { get { return Group + "." + Id; } }
    }

    // Standard UVC controls through DirectShow (works for the OBSBOT Meet 2 and any other webcam).
    class UvcCamera : IDisposable
    {
        static readonly Guid CLSID_SystemDeviceEnum = new Guid("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
        static readonly Guid CLSID_VideoInputDeviceCategory = new Guid("860BB310-5D01-11d0-BD3B-00A0C911CE86");
        static readonly Guid IID_IBaseFilter = new Guid("56a86895-0ad4-11ce-b03a-0020af0ba770");
        static readonly Guid IID_IPropertyBag = new Guid("55272A00-42CB-11CE-8135-00AA004BB851");

        static readonly string[] VpaNames = { "Brightness", "Contrast", "Hue", "Saturation", "Sharpness", "Gamma",
            "Color enable", "White balance", "Backlight comp.", "Gain" };
        static readonly string[] CcNames = { "Pan", "Tilt", "Roll", "Zoom", "Exposure", "Iris", "Focus" };

        public string Name;
        object filter;
        IAMVideoProcAmp vpa;
        IAMCameraControl cc;

        public static List<CamDevice> ListDevices()
        {
            var list = new List<CamDevice>();
            Enumerate(delegate (IMoniker m, string name, string id)
            {
                list.Add(new CamDevice { Name = name, Id = id });
                return false;
            });
            return list;
        }

        public static CamDevice Pick(List<CamDevice> devices, string preferredId)
        {
            foreach (var d in devices) if (d.Id == preferredId) return d;
            foreach (var d in devices) if (d.Name.IndexOf("OBSBOT", StringComparison.OrdinalIgnoreCase) >= 0) return d;
            return devices.Count > 0 ? devices[0] : null;
        }

        public static UvcCamera Open(CamDevice dev)
        {
            UvcCamera cam = null;
            Enumerate(delegate (IMoniker m, string name, string id)
            {
                if (id != dev.Id) return false;
                object f;
                Guid iid = IID_IBaseFilter;
                m.BindToObject(null, null, ref iid, out f);
                if (f != null)
                {
                    cam = new UvcCamera { Name = name, filter = f, vpa = f as IAMVideoProcAmp, cc = f as IAMCameraControl };
                }
                return true;
            });
            return cam;
        }

        delegate bool MonikerVisitor(IMoniker m, string name, string id);

        static void Enumerate(MonikerVisitor visit)
        {
            var devEnum = (ICreateDevEnum)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_SystemDeviceEnum));
            IEnumMoniker em = null;
            try
            {
                Guid cat = CLSID_VideoInputDeviceCategory;
                if (devEnum.CreateClassEnumerator(ref cat, out em, 0) != 0 || em == null) return;
                var ms = new IMoniker[1];
                while (em.Next(1, ms, IntPtr.Zero) == 0)
                {
                    IMoniker m = ms[0];
                    try
                    {
                        string name = ReadProp(m, "FriendlyName") ?? "Camera";
                        string id = ReadProp(m, "DevicePath") ?? ReadProp(m, "CLSID") ?? name;
                        if (visit(m, name, id)) return;
                    }
                    finally { Marshal.ReleaseComObject(m); }
                }
            }
            finally
            {
                if (em != null) Marshal.ReleaseComObject(em);
                Marshal.ReleaseComObject(devEnum);
            }
        }

        static string ReadProp(IMoniker m, string prop)
        {
            object bagObj = null;
            try
            {
                Guid iid = IID_IPropertyBag;
                m.BindToStorage(null, null, ref iid, out bagObj);
                var bag = bagObj as IPropertyBag;
                if (bag == null) return null;
                object val;
                if (bag.Read(prop, out val, IntPtr.Zero) != 0) return null;
                return val as string;
            }
            catch { return null; }
            finally { if (bagObj != null) Marshal.ReleaseComObject(bagObj); }
        }

        public List<CamProp> GetProps()
        {
            var list = new List<CamProp>();
            int min, max, step, def, caps;
            if (vpa != null)
                for (int i = 0; i < VpaNames.Length; i++)
                    if (i != 6 && vpa.GetRange(i, out min, out max, out step, out def, out caps) == 0 && max > min)
                        list.Add(new CamProp { Group = "VPA", Id = i, Name = VpaNames[i], Min = min, Max = max, Step = Math.Max(1, step), Default = def, Caps = caps });
            if (cc != null)
                for (int i = 0; i < CcNames.Length; i++)
                    if (cc.GetRange(i, out min, out max, out step, out def, out caps) == 0 && max > min)
                        list.Add(new CamProp { Group = "CC", Id = i, Name = CcNames[i], Min = min, Max = max, Step = Math.Max(1, step), Default = def, Caps = caps });
            return list;
        }

        public bool Get(CamProp p, out int value, out bool auto)
        {
            int flags = 0; value = p.Default;
            int hr = -1;
            if (p.Group == "VPA" && vpa != null) hr = vpa.Get(p.Id, out value, out flags);
            else if (p.Group == "CC" && cc != null) hr = cc.Get(p.Id, out value, out flags);
            auto = (flags & 1) != 0;
            return hr == 0;
        }

        public bool Set(CamProp p, int value, bool auto)
        {
            int flags = auto ? 1 : 2;
            if (p.Group == "VPA" && vpa != null) return vpa.Set(p.Id, value, flags) == 0;
            if (p.Group == "CC" && cc != null) return cc.Set(p.Id, value, flags) == 0;
            return false;
        }

        // Applies saved values ("VPA.0" -> "128,0"). Returns number of values applied.
        public int Apply(Dictionary<string, string> values)
        {
            int n = 0;
            foreach (var p in GetProps())
            {
                string v;
                if (!values.TryGetValue(p.Key, out v)) continue;
                string[] parts = v.Split(',');
                int val;
                if (!int.TryParse(parts[0], out val)) continue;
                bool auto = parts.Length > 1 && parts[1] == "1";
                if (Set(p, Math.Max(p.Min, Math.Min(p.Max, val)), auto && p.CanAuto)) n++;
            }
            return n;
        }

        public void ShowDriverDialog(IntPtr owner)
        {
            var spp = filter as ISpecifyPropertyPages;
            if (spp == null) return;
            DsCAUUID pages;
            if (spp.GetPages(out pages) != 0) return;
            try
            {
                object o = filter;
                OleCreatePropertyFrame(owner, 0, 0, Name, 1, ref o, pages.cElems, pages.pElems, 0, 0, IntPtr.Zero);
            }
            finally { if (pages.pElems != IntPtr.Zero) Marshal.FreeCoTaskMem(pages.pElems); }
        }

        public void Dispose()
        {
            if (filter != null) { Marshal.FinalReleaseComObject(filter); filter = null; vpa = null; cc = null; }
        }

        [DllImport("oleaut32.dll", CharSet = CharSet.Unicode)]
        static extern int OleCreatePropertyFrame(IntPtr hwnd, int x, int y, string caption, int cObjects,
            [MarshalAs(UnmanagedType.Interface)] ref object ppUnk, int cPages, IntPtr pPageClsID, int lcid, int reserved, IntPtr pvReserved);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DsCAUUID { public int cElems; public IntPtr pElems; }

    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICreateDevEnum
    {
        [PreserveSig] int CreateClassEnumerator([In] ref Guid clsid, out IEnumMoniker e, int flags);
    }

    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyBag
    {
        [PreserveSig] int Read([MarshalAs(UnmanagedType.LPWStr)] string name, out object val, IntPtr errorLog);
        [PreserveSig] int Write([MarshalAs(UnmanagedType.LPWStr)] string name, ref object val);
    }

    [ComImport, Guid("C6E13360-30AC-11d0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAMVideoProcAmp
    {
        [PreserveSig] int GetRange(int prop, out int min, out int max, out int step, out int def, out int caps);
        [PreserveSig] int Set(int prop, int value, int flags);
        [PreserveSig] int Get(int prop, out int value, out int flags);
    }

    [ComImport, Guid("C6E13370-30AC-11d0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAMCameraControl
    {
        [PreserveSig] int GetRange(int prop, out int min, out int max, out int step, out int def, out int caps);
        [PreserveSig] int Set(int prop, int value, int flags);
        [PreserveSig] int Get(int prop, out int value, out int flags);
    }

    [ComImport, Guid("B196B28B-BAB4-101A-B69C-00AA00341D07"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpecifyPropertyPages
    {
        [PreserveSig] int GetPages(out DsCAUUID pages);
    }
}
