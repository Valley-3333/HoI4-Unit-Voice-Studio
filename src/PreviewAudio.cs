using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace UnitVoiceStudio {
    // Preview data is converted in memory. Original files and recording takes are untouched.
    public sealed class PreviewAudio : IDisposable {
        [StructLayout(LayoutKind.Sequential,Pack=2)] struct Format {public ushort Tag,Channels;public uint Rate,BytesPerSecond;public ushort Align,Bits,Extra;}
        [StructLayout(LayoutKind.Sequential)] struct Header {public IntPtr Data;public uint Length,Recorded;public IntPtr User;public uint Flags,Loops;public IntPtr Next,Reserved;}
        [DllImport("winmm.dll")]static extern uint waveOutOpen(out IntPtr handle,uint device,ref Format format,IntPtr callback,IntPtr instance,uint flags);
        [DllImport("winmm.dll")]static extern uint waveOutPrepareHeader(IntPtr handle,IntPtr header,uint size);
        [DllImport("winmm.dll")]static extern uint waveOutWrite(IntPtr handle,IntPtr header,uint size);
        [DllImport("winmm.dll")]static extern uint waveOutReset(IntPtr handle);
        [DllImport("winmm.dll")]static extern uint waveOutUnprepareHeader(IntPtr handle,IntPtr header,uint size);
        [DllImport("winmm.dll")]static extern uint waveOutClose(IntPtr handle);
        IntPtr handle,header,buffer;
        bool prepared;
        readonly uint headerSize=(uint)Marshal.SizeOf(typeof(Header));
        public bool IsPlaying {get{return handle!=IntPtr.Zero&&header!=IntPtr.Zero&&(((Header)Marshal.PtrToStructure(header,typeof(Header))).Flags&1)==0;}}
        static void Check(uint error) {
            if(error!=0)throw new InvalidOperationException("音声を再生できませんでした（音声エラー "+error+"）。Windowsの出力先・音量と、ほかのアプリによる音声機器の使用を確認してください。");
        }
        public void Play(byte[] wave) {
            Dispose();
            byte[] compatible=ConvertWave(wave);
            var format=new Format{Tag=1,Channels=BitConverter.ToUInt16(compatible,22),Rate=BitConverter.ToUInt32(compatible,24),BytesPerSecond=BitConverter.ToUInt32(compatible,28),Align=BitConverter.ToUInt16(compatible,32),Bits=16};
            try {
                Check(waveOutOpen(out handle,uint.MaxValue,ref format,IntPtr.Zero,IntPtr.Zero,0));
                int size=compatible.Length-44;buffer=Marshal.AllocHGlobal(size);Marshal.Copy(compatible,44,buffer,size);
                header=Marshal.AllocHGlobal((int)headerSize);Marshal.StructureToPtr(new Header{Data=buffer,Length=(uint)size},header,false);
                Check(waveOutPrepareHeader(handle,header,headerSize));prepared=true;
                Check(waveOutWrite(handle,header,headerSize));
            }catch{Dispose();throw;}
        }
        // WHDR_DONE is set by Windows after the final sample has played, not by a duration estimate.
        public bool CompleteIfFinished() {
            if(handle==IntPtr.Zero||IsPlaying)return false;
            Dispose();return true;
        }
        public void Dispose() {
            if(handle!=IntPtr.Zero) {
                Check(waveOutReset(handle));
                if(prepared){Check(waveOutUnprepareHeader(handle,header,headerSize));prepared=false;}
                Check(waveOutClose(handle));handle=IntPtr.Zero;
            }
            if(header!=IntPtr.Zero){Marshal.FreeHGlobal(header);header=IntPtr.Zero;}
            if(buffer!=IntPtr.Zero){Marshal.FreeHGlobal(buffer);buffer=IntPtr.Zero;}
        }
        public static byte[] ConvertWave(byte[] wave) {
            if(wave==null||wave.Length<12||Encoding.ASCII.GetString(wave,0,4)!="RIFF"||Encoding.ASCII.GetString(wave,8,4)!="WAVE")
                throw new InvalidDataException("元音声は対応するWAVファイルではありません。");
            long riffEnd=8L+BitConverter.ToUInt32(wave,4);
            if(riffEnd>wave.Length||riffEnd<12)throw new InvalidDataException("元音声のWAVヘッダーが破損しています。");
            int format=0,channels=0,bits=0,align=0,rate=0,dataStart=-1,dataSize=0;
            for(long offset=12;offset+8<=riffEnd;) {
                int start=(int)offset;
                string id=Encoding.ASCII.GetString(wave,start,4);
                uint size=BitConverter.ToUInt32(wave,start+4);
                long end=offset+8+size;
                if(end>riffEnd)throw new InvalidDataException("元音声のWAVデータが途中で切れています。");
                if(id=="fmt ") {
                    if(size<16)throw new InvalidDataException("元音声のWAV形式情報が不足しています。");
                    format=BitConverter.ToUInt16(wave,start+8);channels=BitConverter.ToUInt16(wave,start+10);
                    rate=checked((int)BitConverter.ToUInt32(wave,start+12));align=BitConverter.ToUInt16(wave,start+20);bits=BitConverter.ToUInt16(wave,start+22);
                    if(format==0xfffe) {
                        if(size<40||BitConverter.ToUInt16(wave,start+24)<22)throw new InvalidDataException("元音声の拡張WAV形式が不正です。");
                        // Accept only the standard PCM/IEEE-float subtype GUID, not compressed codecs.
                        byte[] tail={0,0,0,0,0x10,0,0x80,0,0,0xaa,0,0x38,0x9b,0x71};
                        for(int i=0;i<tail.Length;i++)if(wave[start+34+i]!=tail[i])throw new InvalidDataException("未対応の拡張WAV形式です。");
                        format=BitConverter.ToUInt16(wave,start+32);
                    }
                } else if(id=="data"&&dataStart<0) {dataStart=start+8;dataSize=checked((int)size);}
                offset=end+(size&1);
            }
            if((format!=1&&format!=3)||channels<1||channels>2||rate<8000||rate>192000||dataStart<0||dataSize==0)
                throw new InvalidDataException("元音声の形式を再生できません（形式 "+format+" / "+bits+"bit）。");
            if((format==1&&bits!=8&&bits!=16&&bits!=24&&bits!=32)||(format==3&&bits!=32&&bits!=64)||align!=channels*(bits/8)||dataSize%align!=0)
                throw new InvalidDataException("元音声のサンプル形式が不正、または未対応です。");
            int samples=dataSize/(bits/8);
            byte[] pcm=new byte[checked(samples*2)];
            for(int i=0,p=dataStart;i<samples;i++,p+=bits/8) {
                int value;
                if(format==3) {
                    double sample=bits==32?BitConverter.ToSingle(wave,p):BitConverter.ToDouble(wave,p);
                    if(double.IsNaN(sample))sample=0;
                    value=sample>=1?32767:sample<=-1?-32768:(int)Math.Round(sample*32768);
                } else if(bits==8)value=(wave[p]-128)<<8;
                else if(bits==16)value=BitConverter.ToInt16(wave,p);
                else if(bits==24)value=(short)(wave[p+1]|(wave[p+2]<<8));
                else value=BitConverter.ToInt32(wave,p)>>16;
                value=Math.Max(-32768,Math.Min(32767,value));
                pcm[2*i]=(byte)value;pcm[2*i+1]=(byte)(value>>8);
            }
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)) {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+pcm.Length);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);writer.Write((ushort)1);writer.Write((ushort)channels);writer.Write(rate);writer.Write(rate*channels*2);
                writer.Write((ushort)(channels*2));writer.Write((ushort)16);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(pcm.Length);writer.Write(pcm);
                return stream.ToArray();
            }
        }
    }
}
