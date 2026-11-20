using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;

namespace UnitVoiceStudio {
    static class SelfTest {
        static readonly List<string> results=new List<string>();
        static void Assert(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);results.Add("PASS: "+name);}
        static void Reject(Action action,string name) {bool rejected=false;try{action();}catch{rejected=true;}Assert(rejected,name);}
        internal static void Run(string game,string output) {
            Directory.CreateDirectory(output);
            var parsed=Clausewitz.Parse("# { ignored\nsoundeffect={name=\"test # { }\" sounds={sound=a #sound=b\n sound=\"c\"} volume=0.5}");
            Assert(parsed.Count==1&&parsed[0].Get("name")=="test # { }"&&parsed[0].Child("sounds").Children.Count==2,"Parser: comments, quoted braces, one-line and nested blocks");
            Reject(()=>Clausewitz.Parse("sound={name=x"),"Parser rejects truncated blocks");
            Reject(()=>Paths.Inside(output,"../escape.wav"),"Path traversal rejected");
            var catalog=Catalog.Scan(game);
            Assert(catalog.Events.Any(e=>e.Name=="JAP_infantry_move_out"),"Real integrated DLC: Japanese movement found");
            Assert(catalog.Events.Any(e=>e.Name=="GER_infantry_move_out")&&catalog.Events.Any(e=>e.Name=="PHI_infantry_move_out"),"Real integrated and optional DLC catalogs found");
            Assert(catalog.Events.All(e=>e.Clips.Count>0&&e.Clips.All(c=>c.Available)),"Every selected event resolves to an existing original audio file");
            Assert(catalog.Warnings.Count==0,"No catalog warnings");
            results.AddRange(SetupTests.Run(game,output,catalog));
            TestPreviewConversion(catalog);
            catalog.SaveTo(output);var round=Json.Read<Catalog>(Paths.Inside(output,"data/catalog.json"));
            Assert(round.Events.Count==catalog.Events.Count&&round.Sources.Count==catalog.Sources.Count,"Catalog JSON roundtrip");
            Assert(round.Events.Where(e=>e.Name.EndsWith("_infantry_move_out")).SelectMany(e=>e.Clips).All(c=>File.Exists(Paths.Inside(output,"data/"+c.ReferencePath))),"Movement references extracted for offline preview");
            var project=new Project(output,catalog);project.Export();
            Assert(File.Exists(Paths.Inside(project.ModRoot,"descriptor.mod"))&&!Directory.GetFiles(project.ModRoot,"*.asset",SearchOption.AllDirectories).Any(),"Unrecorded Mod has descriptor and no voice overrides");
            var pcm=new byte[44100*2];for(int i=0;i<44100;i++){short s=(short)(Math.Sin(i*2*Math.PI*440/44100)*4000);byte[] b=BitConverter.GetBytes(s);pcm[i*2]=b[0];pcm[i*2+1]=b[1];}
            byte[] wave=Wave.Create(pcm);Wave.Validate(wave);
            Assert(wave.Length==88244,"Recorded WAV is mono 44.1kHz 16-bit PCM");
            Reject(()=>Wave.Validate(Wave.Create(new byte[100])),"Very short recording rejected");
            var e1=catalog.Events.Single(e=>e.Name=="JAP_infantry_move_out");var c1=e1.Clips.First();
            var original=Clausewitz.Parse(catalog.Sources.Single(s=>s.RelativePath==e1.SourcePath).Text);
            var shared=catalog.Events.First(e=>e.Name!=e1.Name&&e.Clips.Any(c=>c.Name==c1.Name));
            project.Apply(e1,c1,false,wave);
            var files=project.BuildFiles();string virtualPath=catalog.Sources.Single(s=>s.RelativePath==e1.SourcePath).VirtualPath;
            string edited=Encoding.UTF8.GetString(files[virtualPath]);var nodes=Clausewitz.Parse(edited);
            Assert(project.Replacements.Count==1&&files.Keys.Count(x=>x.EndsWith(".wav"))==1,"Apply one candidate emits one WAV and manifest entry");
            var ev=nodes.Single(n=>n.Key=="soundeffect"&&n.Get("name")==e1.Name);
            Assert(ev.Child("sounds").Children.Count==e1.Clips.Count&&ev.Child("sounds").Children[0].Value.StartsWith("uvr_"),"Selected event uses isolated recording and retains all candidate slots");
            Assert(ev.Child("sounds").Children.Skip(1).Select(n=>n.Value).SequenceEqual(e1.Clips.Skip(1).Select(c=>c.Name)),"Other candidates unchanged");
            var originalShared=original.Single(n=>n.Key=="soundeffect"&&n.Get("name")==shared.Name);var editedShared=nodes.Single(n=>n.Key=="soundeffect"&&n.Get("name")==shared.Name);
            Assert(edited.Substring(editedShared.Start,editedShared.End-editedShared.Start)==catalog.Sources.Single(s=>s.RelativePath==e1.SourcePath).Text.Substring(originalShared.Start,originalShared.End-originalShared.Start),"Shared combat event is byte-for-byte unchanged");
            foreach(var source in catalog.Sources) {
                var ve=catalog.Events.FirstOrDefault(x=>x.SourcePath==source.RelativePath);if(ve==null)continue;
                var p2=new Project(Paths.Inside(output,"per-source/"+Guid.NewGuid().ToString("N")),catalog);
                p2.Apply(ve,ve.Clips[0],false,wave);
                Assert(p2.BuildFiles().ContainsKey(source.VirtualPath),"Export works for "+source.RelativePath);
            }
            project.Apply(e1,c1,true,wave);
            Assert(project.Replacements.Count==e1.Clips.Count,"Apply all candidates");
            var p3=new Project(output,round);Assert(p3.Replacements.Count==e1.Clips.Count,"Saved replacements reload after restart");
            project.Restore(e1,c1,false);Assert(project.Replacements.Count==e1.Clips.Count-1,"Restore one candidate");
            project.Restore(e1,c1,true);Assert(project.Replacements.Count==0&&!Directory.GetFiles(project.ModRoot,"*.asset",SearchOption.AllDirectories).Any()&&!Directory.GetFiles(project.ModRoot,"*.wav",SearchOption.AllDirectories).Any(),"Restore all removes managed overrides and WAVs");
            Assert(Directory.GetFiles(Paths.Inside(output,"data/takes"),"*.wav").Length>=2,"Take history preserved after restore");
            Reject(()=>project.Apply(e1,c1,false,new byte[9]),"Invalid take cannot modify Mod");
            Assert(project.Replacements.Count==0,"Failed apply leaves manifest unchanged");
            // Force a filesystem error midway through export and prove existing bytes are restored.
            project.Apply(e1,c1,false,wave);var before=project.BuildFiles();
            var other=catalog.Events.First(x=>x.Name!=e1.Name&&x.SourcePath==e1.SourcePath&&x.Clips[0].Name!=c1.Name);
            string blocker=Paths.Inside(project.ModRoot,"sound/uvr/uvr_"+other.Name+"_"+other.Clips[0].Name+".wav");Directory.CreateDirectory(blocker);
            Reject(()=>project.Apply(other,other.Clips[0],false,wave),"Mid-export disk failure is reported");
            Assert(project.Replacements.Count==1&&before.All(f=>File.ReadAllBytes(Paths.Inside(project.ModRoot,f.Key)).SequenceEqual(f.Value)),"Failed export rolls back managed Mod files and in-memory replacements");
            Assert(Json.Read<List<Replacement>>(Paths.Inside(output,"data/replacements.json")).Count==1,"Failed export leaves saved manifest intact");
            var devices=Recorder.Devices();Assert(devices.All(d=>!string.IsNullOrEmpty(d.Name)),"Windows microphone enumeration completes ("+devices.Count+" inputs; capture not started)");
            File.WriteAllText(Paths.Inside(output,"test-results.txt"),string.Join("\r\n",results)+"\r\n\r\n"+Program.Summary(catalog),Encoding.UTF8);
        }
        static byte[] TestWave(ushort format,ushort bits,ushort channels,int rate,byte[] samples,bool metadata) {
            using(var m=new MemoryStream())using(var w=new BinaryWriter(m)) {
                w.Write(Encoding.ASCII.GetBytes("RIFF"));w.Write(0);w.Write(Encoding.ASCII.GetBytes("WAVE"));
                if(metadata){w.Write(Encoding.ASCII.GetBytes("JUNK"));w.Write(3);w.Write(new byte[]{1,2,3,0});}
                w.Write(Encoding.ASCII.GetBytes("fmt "));w.Write(16);w.Write(format);w.Write(channels);w.Write(rate);w.Write(rate*channels*bits/8);w.Write((ushort)(channels*bits/8));w.Write(bits);
                w.Write(Encoding.ASCII.GetBytes("data"));w.Write(samples.Length);w.Write(samples);if(samples.Length%2==1)w.Write((byte)0);
                m.Position=4;w.Write((int)m.Length-8);return m.ToArray();
            }
        }
        static void TestPreviewConversion(Catalog catalog) {
            byte[] samples24={0,0,128,0,255,255,0,0,0,0,1,0,255,255,127};
            byte[] original=TestWave(1,24,1,44100,samples24,true);byte[] copy=(byte[])original.Clone();
            byte[] converted=PreviewAudio.ConvertWave(original);
            Assert(new[]{-32768,-1,0,1,32767}.SequenceEqual(Enumerable.Range(0,5).Select(i=>(int)BitConverter.ToInt16(converted,44+2*i))),"Preview: signed 24-bit extremes convert correctly with odd metadata chunks");
            Assert(original.SequenceEqual(copy),"Preview conversion does not modify original bytes");
            byte[] stereo=TestWave(1,16,2,48000,new byte[]{0,128,255,127,0,0,1,0},false);
            converted=PreviewAudio.ConvertWave(stereo);
            Assert(BitConverter.ToUInt32(converted,24)==48000&&BitConverter.ToUInt16(converted,22)==2&&converted.Skip(44).SequenceEqual(stereo.Skip(44)),"Preview: 48kHz rate, stereo channels and 16-bit samples preserved");
            byte[] floats=BitConverter.GetBytes(0.999999f).Concat(BitConverter.GetBytes(float.NaN)).Concat(BitConverter.GetBytes(-2f)).ToArray();
            converted=PreviewAudio.ConvertWave(TestWave(3,32,1,44100,floats,false));
            Assert(BitConverter.ToInt16(converted,44)==32767&&BitConverter.ToInt16(converted,46)==0&&BitConverter.ToInt16(converted,48)==-32768,"Preview: floating-point peak cannot wrap negative; NaN and clipping handled");
            Reject(()=>PreviewAudio.ConvertWave(new byte[12]),"Preview rejects invalid WAV");
            byte[] truncated=(byte[])original.Clone();truncated[4]=255;truncated[5]=255;Reject(()=>PreviewAudio.ConvertWave(truncated),"Preview rejects truncated RIFF");
            int count=0,count24=0;
            foreach(var clip in catalog.Events.SelectMany(e=>e.Clips).GroupBy(c=>c.Name,StringComparer.Ordinal).Select(g=>g.First())) {
                byte[] data=File.ReadAllBytes(Paths.Inside(catalog.GamePath,clip.RelativePath));
                byte[] playable=PreviewAudio.ConvertWave(data);
                if(BitConverter.ToUInt16(playable,20)!=1||BitConverter.ToUInt16(playable,34)!=16||BitConverter.ToUInt32(playable,4)!=playable.Length-8||BitConverter.ToUInt32(playable,40)!=playable.Length-44)
                    throw new Exception("Invalid preview output: "+clip.RelativePath);
                for(int offset=12;offset+8<=data.Length;) {
                    uint size=BitConverter.ToUInt32(data,offset+4);
                    if(Encoding.ASCII.GetString(data,offset,4)=="fmt "){if(BitConverter.ToUInt16(data,offset+22)==24)count24++;break;}
                    offset=checked(offset+8+(int)size+(int)(size%2));
                }
                count++;
            }
            Assert(count>3000&&count24>500,"Preview: ALL "+count+" original clips convert to 16-bit PCM (including "+count24+" 24-bit clips)");
        }
        internal static void Playback(string game,string output) {
            Directory.CreateDirectory(output);var catalog=Catalog.Scan(game);var report=new List<string>();
            using(var preview=new PreviewAudio())foreach(string country in new[]{"AFG","IRQ","PHI","JAP"}) {
                var clip=catalog.Events.First(e=>e.Country==country&&e.Name.EndsWith("_infantry_move_out")).Clips[0];
                preview.Play(File.ReadAllBytes(Paths.Inside(catalog.GamePath,clip.RelativePath)));
                if(!preview.IsPlaying)throw new Exception("Playback did not start: "+country);
                var watch=Stopwatch.StartNew();bool completed=false;
                while(watch.ElapsedMilliseconds<15000){if(preview.CompleteIfFinished()){completed=true;break;}Thread.Sleep(20);}
                if(!completed||preview.IsPlaying||preview.CompleteIfFinished())throw new Exception("Playback completion state incorrect: "+country);
                report.Add("PASS: Windows playback started, completed and released output: "+country+" / "+clip.Name);
                preview.Play(File.ReadAllBytes(Paths.Inside(catalog.GamePath,clip.RelativePath)));preview.Dispose();
                if(preview.IsPlaying||preview.CompleteIfFinished())throw new Exception("Stopped preview still active: "+country);
                report.Add("PASS: Stop cancels preview immediately and suppresses stale completion: "+country);
            }
            File.WriteAllText(Paths.Inside(output,"playback-results.txt"),string.Join("\r\n",report),Encoding.UTF8);
        }
    }
}
