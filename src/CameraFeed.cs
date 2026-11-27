using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

namespace SmartCamLight
{
    // Live frames from a camera, for the preview in the settings window.
    //
    // Uses Media Foundation's source reader, which turns whatever the camera delivers (MJPEG,
    // NV12, YUY2...) into plain RGB. Every Media Foundation call stays on one worker thread; the
    // window only reads the most recent frame.
    class CameraFeed : IDisposable
    {
        readonly string deviceId, deviceName;
        readonly int wantedWidth;
        readonly Thread thread;
        readonly object sync = new object();
        volatile bool stop;
        Bitmap frame;

        public volatile string Error;   // set when the camera could not be started, or was lost
        public Action Changed;          // called on the worker thread after a new frame or an error

        // wantedWidth: how many pixels wide the picture will be shown.
        public CameraFeed(CamDevice device, int wantedWidth)
        {
            deviceId = device.Id; deviceName = device.Name;
            this.wantedWidth = wantedWidth;
            thread = new Thread(Work) { IsBackground = true, Name = "Camera preview" };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }

        public Size FrameSize
        {
            get { lock (sync) return frame == null ? Size.Empty : frame.Size; }
        }

        // Draws the latest frame into a rounded rectangle, mirrored like the self-view in call apps.
        // Returns false while there is no frame yet.
        public bool Draw(Graphics g, RectangleF dest, float radius)
        {
            lock (sync)
            {
                if (frame == null) return false;
                using (var brush = new TextureBrush(frame, WrapMode.Clamp))
                using (var path = Ui.Round(dest, radius))
                using (var m = new Matrix())
                {
                    m.Translate(dest.Right, dest.Y);
                    m.Scale(-dest.Width / frame.Width, dest.Height / frame.Height);
                    brush.Transform = m;
                    g.InterpolationMode = InterpolationMode.Bilinear;
                    g.FillPath(brush, path);
                }
                return true;
            }
        }

        public void Dispose()
        {
            stop = true;
            Changed = null;
            if (thread.IsAlive) thread.Join(1500);   // a frame arrives every few milliseconds, so this is quick
            lock (sync)
            {
                if (frame != null) { frame.Dispose(); frame = null; }
            }
        }

        void Notify()
        {
            Action changed = Changed;
            if (changed != null) changed();
        }

        // ---- worker thread ----

        const int FirstVideo = unchecked((int)0xFFFFFFFC);   // MF_SOURCE_READER_FIRST_VIDEO_STREAM

        void Work()
        {
            IMFMediaSource source = null;
            IMFSourceReader reader = null;
            bool started = false;
            try
            {
                Check(MFStartup(0x20070, 0));
                started = true;

                string link = FindLink();
                if (link == null) throw new InvalidOperationException("the camera is not available to Media Foundation");

                IMFAttributes a;
                Check(MFCreateAttributes(out a, 2));
                Guid k = SourceType, v = VideoCapture;
                Check(a.SetGUID(ref k, ref v));
                k = SymbolicLink;
                Check(a.SetString(ref k, link));
                Check(MFCreateDeviceSource(a, out source));
                Marshal.ReleaseComObject(a);

                Check(MFCreateAttributes(out a, 1));
                k = AdvancedVideoProcessing;   // lets the reader convert to RGB
                Check(a.SetUINT32(ref k, 1));
                Check(MFCreateSourceReaderFromMediaSource(source, a, out reader));
                Marshal.ReleaseComObject(a);

                ChooseFormat(reader, wantedWidth);

                // Ask for RGB, scaled down to the shown size when the camera delivers more than that
                // (fewer pixels to copy and draw). Not every setup can scale: then take full size.
                int width, height, stride;
                ReadFormat(reader, out width, out height, out stride);
                bool scaled = false;
                if (width > wantedWidth * 5 / 4 && wantedWidth >= 160)
                {
                    int w = (wantedWidth + 1) & ~1, h = ((int)((long)w * height / width) + 1) & ~1;
                    scaled = SetRgbOutput(reader, ((long)w << 32) | (uint)h) >= 0;
                }
                if (!scaled) Check(SetRgbOutput(reader, 0));
                ReadFormat(reader, out width, out height, out stride);
                Log.Write("Camera preview started: " + deviceName + ", " + width + " x " + height);

                while (!stop)
                {
                    int actual, flags;
                    long time;
                    IMFSample sample;
                    int hr = reader.ReadSample(FirstVideo, 0, out actual, out flags, out time, out sample);
                    try
                    {
                        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                        if ((flags & 0x3) != 0) throw new InvalidOperationException("the camera stopped sending pictures");
                        if ((flags & 0x20) != 0) ReadFormat(reader, out width, out height, out stride);   // format changed
                        if (sample != null && !stop && Store(sample, width, height, stride)) Notify();
                    }
                    finally
                    {
                        if (sample != null) Marshal.ReleaseComObject(sample);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!stop)
                {
                    Log.Write("Camera preview: " + ex.Message);
                    Error = "The camera could not be started. Another app may be using it, or camera access may be turned off in Windows privacy settings.";
                }
            }
            finally
            {
                try { if (reader != null) Marshal.ReleaseComObject(reader); } catch { }
                try { if (source != null) { source.Shutdown(); Marshal.ReleaseComObject(source); } } catch { }
                if (started) MFShutdown();
                Notify();
            }
        }

        static int SetRgbOutput(IMFSourceReader reader, long frameSize)
        {
            IMFMediaType rgb;
            Check(MFCreateMediaType(out rgb));
            try
            {
                var attr = (IMFAttributes)rgb;
                Guid k = MajorType, v = Video;
                Check(attr.SetGUID(ref k, ref v));
                k = Subtype; v = Rgb32;
                Check(attr.SetGUID(ref k, ref v));
                k = FrameSizeKey;
                if (frameSize != 0) Check(attr.SetUINT64(ref k, frameSize));
                return reader.SetCurrentMediaType(FirstVideo, IntPtr.Zero, rgb);
            }
            finally { Marshal.ReleaseComObject(rgb); }
        }

        // Media Foundation names a device by a different interface path than DirectShow (which the
        // rest of the app uses). Both paths start with the same device instance, so match on that.
        string FindLink()
        {
            IMFAttributes a;
            Check(MFCreateAttributes(out a, 1));
            Guid k = SourceType, v = VideoCapture;
            Check(a.SetGUID(ref k, ref v));
            IntPtr array;
            int count;
            Check(MFEnumDeviceSources(a, out array, out count));
            Marshal.ReleaseComObject(a);

            string byId = null, byName = null, wanted = Instance(deviceId);
            for (int i = 0; i < count; i++)
            {
                IntPtr p = Marshal.ReadIntPtr(array, i * IntPtr.Size);
                object device = Marshal.GetObjectForIUnknown(p);
                Marshal.Release(p);
                try
                {
                    var attr = (IMFAttributes)device;
                    string link = GetString(attr, SymbolicLink);
                    if (link == null) continue;
                    if (byId == null && wanted != null && Instance(link) == wanted) byId = link;
                    if (byName == null && GetString(attr, FriendlyName) == deviceName) byName = link;
                }
                finally { Marshal.ReleaseComObject(device); }
            }
            Marshal.FreeCoTaskMem(array);
            return byId ?? byName;
        }

        // "\\?\usb#vid_046d&pid_085e&mi_00#7&1a2b3c&0&0000#{interface class}\global" -> the part before "#{".
        static string Instance(string path)
        {
            if (path == null) return null;
            int i = path.LastIndexOf("#{", StringComparison.Ordinal);
            return i > 0 ? path.Substring(0, i).ToLowerInvariant() : null;
        }

        static string GetString(IMFAttributes attr, Guid key)
        {
            IntPtr text;
            int length;
            if (attr.GetAllocatedString(ref key, out text, out length) != 0 || text == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(text, length); }
            finally { Marshal.FreeCoTaskMem(text); }
        }

        // Picks a modest capture size: enough for a sharp preview without decoding 4K for nothing.
        // Prefers the smallest 16:9 format that covers the shown size at 20 frames per second or more.
        static void ChooseFormat(IMFSourceReader reader, int wantedWidth)
        {
            int minW = Math.Max(640, wantedWidth), minH = minW * 9 / 16;
            var offered = new System.Collections.Generic.List<string>();
            int best = -1;
            double bestScore = double.MaxValue;
            for (int i = 0; i < 500; i++)
            {
                IMFMediaType type;
                if (reader.GetNativeMediaType(FirstVideo, i, out type) != 0 || type == null) break;
                try
                {
                    var attr = (IMFAttributes)type;
                    long size, rate;
                    Guid k = FrameSizeKey;
                    if (attr.GetUINT64(ref k, out size) != 0) continue;
                    int w = (int)(size >> 32), h = (int)(size & 0xFFFFFFFF);
                    if (w <= 0 || h <= 0) continue;
                    k = FrameRateKey;
                    double fps = 30;
                    if (attr.GetUINT64(ref k, out rate) == 0 && (rate & 0xFFFFFFFF) != 0) fps = (rate >> 32) / (double)(rate & 0xFFFFFFFF);

                    string label = w + "x" + h + "@" + Math.Round(fps);
                    if (!offered.Contains(label)) offered.Add(label);

                    double area = (double)w * h;
                    double score = w >= minW && h >= minH ? area : 1e12 - area;   // big enough: smaller is better
                    if (Math.Abs(w * 9 - h * 16) > w) score *= 1.5;                 // not 16:9
                    if (fps < 20) score += 1e13;
                    if (score < bestScore) { bestScore = score; best = i; }
                }
                finally { Marshal.ReleaseComObject(type); }
            }
            Log.Write("Camera preview: formats offered: " + string.Join(" ", offered.ToArray()));
            if (best < 0) return;
            IMFMediaType chosen;
            if (reader.GetNativeMediaType(FirstVideo, best, out chosen) != 0 || chosen == null) return;
            try { reader.SetCurrentMediaType(FirstVideo, IntPtr.Zero, chosen); }
            finally { Marshal.ReleaseComObject(chosen); }
        }

        static void ReadFormat(IMFSourceReader reader, out int width, out int height, out int stride)
        {
            IMFMediaType type;
            Check(reader.GetCurrentMediaType(FirstVideo, out type));
            try
            {
                var attr = (IMFAttributes)type;
                long size;
                Guid k = FrameSizeKey;
                Check(attr.GetUINT64(ref k, out size));
                width = (int)(size >> 32);
                height = (int)(size & 0xFFFFFFFF);
                k = DefaultStride;
                if (attr.GetUINT32(ref k, out stride) != 0) stride = width * 4;
            }
            finally { Marshal.ReleaseComObject(type); }
        }

        // Copies a sample's picture into the frame bitmap. Returns true when a frame was stored.
        bool Store(IMFSample sample, int width, int height, int stride)
        {
            IMFMediaBuffer buffer;
            if (sample.ConvertToContiguousBuffer(out buffer) != 0 || buffer == null) return false;
            try
            {
                var buffer2d = buffer as IMF2DBuffer;
                IntPtr top;
                int pitch;
                if (buffer2d != null && buffer2d.Lock2D(out top, out pitch) == 0)
                {
                    try { return Store(top, pitch, width, height); }
                    finally { buffer2d.Unlock2D(); }
                }

                IntPtr data;
                int max, length;
                if (buffer.Lock(out data, out max, out length) != 0) return false;
                try
                {
                    if (stride == 0 || length < (long)Math.Abs(stride) * height) return false;
                    // A negative stride means the picture is stored bottom-up: start at its last row.
                    if (stride < 0) data = new IntPtr(data.ToInt64() + (long)(height - 1) * -stride);
                    return Store(data, stride, width, height);
                }
                finally { buffer.Unlock(); }
            }
            finally { Marshal.ReleaseComObject(buffer); }
        }

        bool Store(IntPtr top, int pitch, int width, int height)
        {
            if (width <= 0 || height <= 0 || Math.Abs(pitch) < width * 4) return false;
            lock (sync)
            {
                if (stop) return false;
                if (frame == null || frame.Width != width || frame.Height != height)
                {
                    if (frame != null) frame.Dispose();
                    frame = new Bitmap(width, height, PixelFormat.Format32bppRgb);
                }
                BitmapData bits = frame.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                try
                {
                    if (pitch == bits.Stride) CopyMemory(bits.Scan0, top, new UIntPtr((ulong)((long)pitch * height)));
                    else
                        for (int y = 0; y < height; y++)
                            CopyMemory(new IntPtr(bits.Scan0.ToInt64() + (long)y * bits.Stride), new IntPtr(top.ToInt64() + (long)y * pitch), new UIntPtr((uint)(width * 4)));
                }
                finally { frame.UnlockBits(bits); }
            }
            return true;
        }

        static void Check(int hr)
        {
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        }

        static readonly Guid SourceType = new Guid("c60ac5fe-252a-478f-a0ef-bc8fa5f7cad3");             // MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE
        static readonly Guid VideoCapture = new Guid("8ac3587a-4ae7-42d8-99e0-0a6013eef90f");           // ..._SOURCE_TYPE_VIDCAP_GUID
        static readonly Guid SymbolicLink = new Guid("58f0aad8-22bf-4f8a-bb3d-d2c4978c6e2f");           // ..._VIDCAP_SYMBOLIC_LINK
        static readonly Guid FriendlyName = new Guid("60d0e559-52f8-4fa2-bbce-acdb34a8ec01");           // MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME
        static readonly Guid AdvancedVideoProcessing = new Guid("0f81da2c-b537-4672-a8b2-a681b17307a3"); // MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING
        static readonly Guid MajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");              // MF_MT_MAJOR_TYPE
        static readonly Guid Subtype = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");                // MF_MT_SUBTYPE
        static readonly Guid FrameSizeKey = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");           // MF_MT_FRAME_SIZE
        static readonly Guid FrameRateKey = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");           // MF_MT_FRAME_RATE
        static readonly Guid DefaultStride = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");          // MF_MT_DEFAULT_STRIDE
        static readonly Guid Video = new Guid("73646976-0000-0010-8000-00aa00389b71");                  // MFMediaType_Video
        static readonly Guid Rgb32 = new Guid("00000016-0000-0010-8000-00aa00389b71");                  // MFVideoFormat_RGB32

        [DllImport("mfplat.dll")] static extern int MFStartup(int version, int flags);
        [DllImport("mfplat.dll")] static extern int MFShutdown();
        [DllImport("mfplat.dll")] static extern int MFCreateAttributes(out IMFAttributes attributes, int initialSize);
        [DllImport("mfplat.dll")] static extern int MFCreateMediaType(out IMFMediaType type);
        [DllImport("mf.dll")] static extern int MFEnumDeviceSources(IMFAttributes attributes, out IntPtr devices, out int count);
        [DllImport("mf.dll")] static extern int MFCreateDeviceSource(IMFAttributes attributes, out IMFMediaSource source);
        [DllImport("mfreadwrite.dll")] static extern int MFCreateSourceReaderFromMediaSource(IMFMediaSource source, IMFAttributes attributes, out IMFSourceReader reader);
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")] static extern void CopyMemory(IntPtr destination, IntPtr source, UIntPtr length);
    }

    // Media Foundation interfaces, declared only as far as they are used. The "_n" members hold
    // the vtable slots of methods that are never called; their order must not change.

    [ComImport, Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFAttributes
    {
        void _1(); void _2(); void _3(); void _4();
        [PreserveSig] int GetUINT32([In] ref Guid key, out int value);
        [PreserveSig] int GetUINT64([In] ref Guid key, out long value);
        void _7();
        [PreserveSig] int GetGUID([In] ref Guid key, out Guid value);
        void _9(); void _10();
        [PreserveSig] int GetAllocatedString([In] ref Guid key, out IntPtr value, out int length);
        void _12(); void _13(); void _14(); void _15(); void _16(); void _17(); void _18();
        [PreserveSig] int SetUINT32([In] ref Guid key, int value);
        [PreserveSig] int SetUINT64([In] ref Guid key, long value);
        void _21();
        [PreserveSig] int SetGUID([In] ref Guid key, [In] ref Guid value);
        [PreserveSig] int SetString([In] ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
    }

    // A media type is used through IMFAttributes (its base interface); this declaration only
    // carries the interface identity for passing media types to the reader.
    [ComImport, Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaType
    {
    }

    [ComImport, Guid("279A808D-AEC7-40C8-9C6B-A6B492C78A66"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaSource
    {
        void _1(); void _2(); void _3(); void _4();            // IMFMediaEventGenerator
        void _5(); void _6(); void _7(); void _8(); void _9();
        [PreserveSig] int Shutdown();
    }

    [ComImport, Guid("70AE66F2-C809-4E4F-8915-BDCB406B7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSourceReader
    {
        void _1(); void _2();
        [PreserveSig] int GetNativeMediaType(int stream, int index, out IMFMediaType type);
        [PreserveSig] int GetCurrentMediaType(int stream, out IMFMediaType type);
        [PreserveSig] int SetCurrentMediaType(int stream, IntPtr reserved, IMFMediaType type);
        void _6();
        [PreserveSig] int ReadSample(int stream, int controlFlags, out int actualStream, out int streamFlags, out long timestamp, out IMFSample sample);
    }

    [ComImport, Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSample
    {
        // IMFAttributes (30 methods)
        void _1(); void _2(); void _3(); void _4(); void _5(); void _6(); void _7(); void _8(); void _9(); void _10();
        void _11(); void _12(); void _13(); void _14(); void _15(); void _16(); void _17(); void _18(); void _19(); void _20();
        void _21(); void _22(); void _23(); void _24(); void _25(); void _26(); void _27(); void _28(); void _29(); void _30();
        // IMFSample
        void _31(); void _32(); void _33(); void _34(); void _35(); void _36(); void _37(); void _38();
        [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
    }

    [ComImport, Guid("045FA593-8799-42B8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr data, out int maxLength, out int currentLength);
        [PreserveSig] int Unlock();
    }

    [ComImport, Guid("7DC9D5F9-9ED9-44EC-9BBF-0600BB589FBB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMF2DBuffer
    {
        [PreserveSig] int Lock2D(out IntPtr scanline0, out int pitch);
        [PreserveSig] int Unlock2D();
    }
}
