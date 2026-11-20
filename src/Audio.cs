using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace UnitVoiceStudio {
    public sealed class InputDevice {
        public uint Id; public string Name;
        public override string ToString() { return Name; }
    }
    public sealed class Recorder : IDisposable {
        [StructLayout(LayoutKind.Sequential,Pack=2)] struct Format {public ushort Tag,Channels;public uint Rate,BytesPerSecond;public ushort Align,Bits,Extra;}
        [StructLayout(LayoutKind.Sequential)] struct Header {public IntPtr Data;public uint Length,Recorded;public IntPtr User;public uint Flags,Loops;public IntPtr Next,Reserved;}
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Caps {public ushort Mid,Pid;public uint Version;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Name;public uint Formats;public ushort Channels,Reserved;}
        delegate void WaveCallback(IntPtr handle,uint message,IntPtr instance,IntPtr header,IntPtr extra);
        [DllImport("winmm.dll")]static extern uint waveInGetNumDevs();
        [DllImport("winmm.dll",CharSet=CharSet.Unicode,EntryPoint="waveInGetDevCapsW")]static extern uint waveInGetDevCaps(UIntPtr id,out Caps caps,uint size);
        [DllImport("winmm.dll")]static extern uint waveInOpen(out IntPtr handle,uint device,ref Format format,WaveCallback callback,IntPtr instance,uint flags);
        [DllImport("winmm.dll")]static extern uint waveInPrepareHeader(IntPtr handle,IntPtr header,uint size);
        [DllImport("winmm.dll")]static extern uint waveInUnprepareHeader(IntPtr handle,IntPtr header,uint size);
        [DllImport("winmm.dll")]static extern uint waveInAddBuffer(IntPtr handle,IntPtr header,uint size);
        [DllImport("winmm.dll")]static extern uint waveInStart(IntPtr handle);
        [DllImport("winmm.dll")]static extern uint waveInReset(IntPtr handle);
        [DllImport("winmm.dll")]static extern uint waveInClose(IntPtr handle);
        public static List<InputDevice> Devices() {
            var list=new List<InputDevice>();
            for(uint i=0;i<waveInGetNumDevs();i++){Caps c;if(waveInGetDevCaps(new UIntPtr(i),out c,(uint)Marshal.SizeOf(typeof(Caps)))==0)list.Add(new InputDevice{Id=i,Name=c.Name});}
            return list;
        }
        readonly object sync=new object();
        readonly MemoryStream pcm=new MemoryStream();
        readonly List<IntPtr> headers=new List<IntPtr>();
        readonly List<IntPtr> buffers=new List<IntPtr>();
        readonly WaveCallback callback;
        IntPtr handle;
        volatile bool running;
        int level,callbackError;
        readonly uint headerSize=(uint)Marshal.SizeOf(typeof(Header));
        public int Level {get{return Volatile.Read(ref level);}}
        public string Error {get{int err=Volatile.Read(ref callbackError);return err==0?null:"録音バッファーエラー: "+err;}}
        public double Seconds {get{lock(sync)return pcm.Length/88200.0;}}
        static void Check(uint result) {if(result!=0)throw new InvalidOperationException("マイクを開けません（音声エラー "+result+"）。Windowsのマイク許可と入力機器を確認してください。");}
        public Recorder(uint device) {
            callback=OnData;
            var f=new Format{Tag=1,Channels=1,Rate=44100,BytesPerSecond=88200,Align=2,Bits=16};
            Check(waveInOpen(out handle,device,ref f,callback,IntPtr.Zero,0x30000));
            try {
                for(int i=0;i<4;i++) {
                    IntPtr data=Marshal.AllocHGlobal(8192);buffers.Add(data);
                    IntPtr hp=Marshal.AllocHGlobal((int)headerSize);headers.Add(hp);
                    Marshal.StructureToPtr(new Header{Data=data,Length=8192},hp,false);
                    Check(waveInPrepareHeader(handle,hp,headerSize));Check(waveInAddBuffer(handle,hp,headerSize));
                }
                running=true;Check(waveInStart(handle));
            }catch{Dispose();throw;}
        }
        void OnData(IntPtr h,uint message,IntPtr instance,IntPtr hp,IntPtr extra) {
            if(message!=0x3C0)return;
            try {
                lock(sync) {
                    var header=(Header)Marshal.PtrToStructure(hp,typeof(Header));
                    if(header.Recorded>0) {
                        var bytes=new byte[(int)header.Recorded];Marshal.Copy(header.Data,bytes,0,bytes.Length);
                        // Hard limit also applies if the UI thread is busy.
                        int count=(int)Math.Min(bytes.Length,30*88200-pcm.Length);if(count>0)pcm.Write(bytes,0,count);
                        int peak=0;for(int i=0;i+1<bytes.Length;i+=2)peak=Math.Max(peak,Math.Abs((int)BitConverter.ToInt16(bytes,i)));
                        Volatile.Write(ref level,peak*100/32768);
                    }
                    if(running) {uint err=waveInAddBuffer(handle,hp,headerSize);if(err!=0)Volatile.Write(ref callbackError,(int)err);}
                }
            }catch{Volatile.Write(ref callbackError,-1);}
        }
        public byte[] Stop() {
            Dispose();lock(sync)return Wave.Create(pcm.ToArray());
        }
        public void Dispose() {
            if(handle==IntPtr.Zero)return;
            lock(sync)running=false;
            waveInReset(handle);
            // Reset returns every queued buffer. Unprepare succeeds only after callbacks finish.
            foreach(IntPtr hp in headers) {
                uint result=waveInUnprepareHeader(handle,hp,headerSize);
                for(int i=0;result==33 && i<100;i++){Thread.Sleep(5);result=waveInUnprepareHeader(handle,hp,headerSize);}
                if(result!=0) { Volatile.Write(ref callbackError,(int)result); return; }
            }
            uint close=waveInClose(handle);
            if(close!=0){Volatile.Write(ref callbackError,(int)close);return;}
            handle=IntPtr.Zero;
            foreach(IntPtr hp in headers)Marshal.FreeHGlobal(hp);
            foreach(IntPtr b in buffers)Marshal.FreeHGlobal(b);
            headers.Clear();buffers.Clear();
            GC.KeepAlive(callback);
        }
    }
}
