using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace UnitVoiceStudio {
    public sealed class AssetSource { public string RelativePath, VirtualPath, Text, Pack; }
    public sealed class Clip { public string Name, RelativePath, ReferencePath; public bool Available; public override string ToString() { return Name + (Available ? "" : "（元ファイルなし）"); } }
    public sealed class VoiceEvent {
        public string Name, Country, Usage, SourcePath, Pack;
        public List<Clip> Clips = new List<Clip>();
        public override string ToString() { return Usage + "  [" + Clips.Count + "候補]"; }
    }
    public sealed class Catalog {
        public string GamePath, Version;
        public List<AssetSource> Sources = new List<AssetSource>();
        public List<VoiceEvent> Events = new List<VoiceEvent>();
        public List<string> Warnings = new List<string>();
        public static readonly Dictionary<string, string> Countries = new Dictionary<string,string> {
            {"JAP","日本"},{"GER","ドイツ"},{"ENG","イギリス"},{"USA","アメリカ"},{"SOV","ソビエト連邦"},{"FRA","フランス"},{"ITA","イタリア"},
            {"CHI","中国"},{"SPR","スペイン"},{"POL","ポーランド"},{"CZE","チェコスロヴァキア"},{"HUN","ハンガリー"},{"ROM","ルーマニア"},
            {"AST","オーストラリア"},{"CAN","カナダ"},{"NZL","ニュージーランド"},{"RAJ","インド"},{"SAF","南アフリカ"},
            {"MEX","メキシコ"},{"HOL","オランダ"},{"POR","ポルトガル"},{"BUL","ブルガリア"},{"GRE","ギリシャ"},{"TUR","トルコ"},
            {"EST","エストニア"},{"LAT","ラトビア"},{"LIT","リトアニア"},{"ETH","エチオピア"},{"ARG","アルゼンチン"},{"BRA","ブラジル"},{"CHL","チリ"},
            {"BEL","ベルギー"},{"COG","コンゴ"},{"AUS","オーストリア"},{"IRQ","イラク"},{"PER","イラン"},{"AFG","アフガニスタン"},{"URD_F","ウルドゥー語（女性）"},
            {"PHI","フィリピン"},{"FIN","フィンランド"},{"SWE","スウェーデン"},{"NOR","ノルウェー"},{"DEN","デンマーク"},{"COMMON","共通（国別ではない効果音）"}
            ,{"PRC","中国共産党"},{"SIK","新疆"},{"YUN","雲南"},{"GXC","広西"},{"SHX","山西"},{"XSM","青海馬家"},{"MEN","蒙疆"},{"MAN","満州国"},
            {"KUM","クムル・ハン国"},{"GSM","甘粛馬家"},{"GDC","広東"},{"KHM","ホータン馬家"},{"NXM","寧夏馬家"},{"SND","山東"},{"SIC","四川"},{"XIC","西安（東北軍）"},{"RNG","中国（汪兆銘政権）"},{"HBC","河北チャハル"}
        };
        public static string CountryLabel(string tag) { return (Countries.ContainsKey(tag) ? Countries[tag] : tag) + " / " + tag; }
        public void SaveTo(string root) {
            // Extract only movement references for offline comparison; these never enter the Mod.
            foreach(var e in Events.Where(x=>x.Name.EndsWith("_infantry_move_out",StringComparison.Ordinal))) foreach(var clip in e.Clips.Where(x=>x.Available)) {
                string rel="reference_audio/"+Regex.Replace(clip.Name,@"[^A-Za-z0-9_.-]","_")+".wav";
                Atomic.Write(Paths.Inside(root,"data/"+rel),File.ReadAllBytes(Paths.Inside(GamePath,clip.RelativePath)));clip.ReferencePath=rel;
            }
            Json.Write(Paths.Inside(root,"data/catalog.json"),this);
            var csv=new StringBuilder("国タグ,国名,用途,イベントID,候補ID,元ファイル,提供元,元ファイル存在\r\n");
            foreach(var e in Events)foreach(var clip in e.Clips){var fields=new[]{e.Country,CountryLabel(e.Country),e.Usage,e.Name,clip.Name,clip.RelativePath,e.Pack,clip.Available?"yes":"no"};csv.AppendLine(string.Join(",",fields.Select(x=>"\""+(x??"").Replace("\"","\"\"")+"\"")));}
            Atomic.Write(Paths.Inside(root,"data/unit-voices.csv"),new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray());
        }
        static string Usage(string suffix) {
            switch(suffix) {
                case "move_out": return "移動・進軍命令";
                case "idle": return "待機・選択";
                case "neutral_combat": return "通常戦闘";
                case "positive_combat": return "優勢時の戦闘";
                case "retreat": return "撤退";
                default: return suffix;
            }
        }
        public static Catalog Scan(string game) {
            game = Path.GetFullPath(game);
            if (!File.Exists(Path.Combine(game,"hoi4.exe")) || !Directory.Exists(Path.Combine(game,"sound"))) throw new InvalidDataException("hoi4.exe と sound フォルダーのある本体フォルダーを選んでください。");
            var c = new Catalog { GamePath = game, Version = "1.20.*" };
            string settings = Path.Combine(game,"launcher-settings.json");
            if (File.Exists(settings)) {
                var m = Regex.Match(File.ReadAllText(settings), "\"rawVersion\"\\s*:\\s*\"(\\d+\\.\\d+)");
                if (m.Success) c.Version = m.Groups[1].Value + ".*";
            }
            var roots = new List<string> { Path.Combine(game,"sound") };
            foreach (string container in new[] {"integrated_dlc", "dlc"}) {
                string path = Path.Combine(game,container);
                if (Directory.Exists(path)) foreach (string dir in Directory.GetDirectories(path).OrderBy(x=>x,StringComparer.Ordinal)) if (Directory.Exists(Path.Combine(dir,"sound"))) roots.Add(Path.Combine(dir,"sound"));
            }
            var sounds = new Dictionary<string,Clip>(StringComparer.Ordinal);
            var parsed = new List<Tuple<AssetSource,List<Node>>>();
            foreach (string root in roots) foreach (string file in Directory.GetFiles(root,"*.asset",SearchOption.AllDirectories).OrderBy(x=>x,StringComparer.Ordinal)) {
                string rel = file.Substring(game.Length+1).Replace('\\','/');
                var source = new AssetSource { RelativePath = rel, VirtualPath = "sound/" + file.Substring(root.Length+1).Replace('\\','/'), Text = File.ReadAllText(file), Pack = root == roots[0] ? "本体" : new DirectoryInfo(root).Parent.Name };
                var nodes = Clausewitz.Parse(source.Text);
                foreach (Node n in nodes.Where(n=>n.Key == "sound" && n.Value == null && n.Get("file") != null)) {
                    string name = n.Get("name"), audio = n.Get("file").Replace('\\','/');
                    if (string.IsNullOrEmpty(name)) continue;
                    string path = Paths.Inside(root,audio);
                    sounds[name] = new Clip { Name = name, RelativePath = path.Substring(game.Length+1).Replace('\\','/'), Available = File.Exists(path) };
                }
                parsed.Add(Tuple.Create(source,nodes));
            }
            var effects = new Dictionary<string,VoiceEvent>(StringComparer.Ordinal);
            foreach (var p in parsed) foreach (Node n in p.Item2.Where(n=>n.Key == "soundeffect" && n.Value == null)) {
                string name = n.Get("name") ?? "";
                var match = Regex.Match(name,@"^([A-Z][A-Z0-9_]+)_infantry_(idle|neutral_combat|positive_combat|retreat|move_out)$");
                string usage = null, country = "COMMON";
                if (match.Success) { country = match.Groups[1].Value; usage = Usage(match.Groups[2].Value); }
                else {
                    var common = new Dictionary<string,string> {{"infantry_move","陸軍の移動命令・効果音"},{"order_fleet_effect","海軍の移動命令・効果音"},{"order_airwing_effect","航空隊の命令・効果音"},{"order_convoy_transport","輸送船の移動命令・効果音"},{"select_infantry","歩兵の選択・効果音"},{"select_tank","戦車の選択・効果音"},{"select_vehicle","車両の選択・効果音"},{"select_fleet","艦隊の選択・効果音"},{"select_aircraft","航空隊の選択・効果音"}};
                    if (common.ContainsKey(name)) usage = common[name];
                }
                if (usage == null || n.Child("sounds") == null) continue;
                var e = new VoiceEvent {Name=name,Country=country,Usage=usage,SourcePath=p.Item1.RelativePath,Pack=p.Item1.Pack};
                foreach (Node s in n.Child("sounds").Children.Where(x=>x.Key == "sound" && x.Value != null)) {
                    Clip clip;
                    if (!sounds.TryGetValue(s.Value,out clip)) { clip = new Clip {Name=s.Value,Available=false}; c.Warnings.Add(name+": 定義が見つかりません: "+s.Value); }
                    e.Clips.Add(clip);
                }
                if(e.Clips.Count > 0) effects[name] = e;
            }
            c.Events = effects.Values.OrderBy(e=>e.Country == "JAP" ? "" : e.Country).ThenBy(e=>e.Usage == "移動・進軍命令" ? "" : e.Usage).ToList();
            var used = new HashSet<string>(c.Events.Select(e=>e.SourcePath));
            c.Sources = parsed.Where(p=>used.Contains(p.Item1.RelativePath)).Select(p=>p.Item1).ToList();
            if(c.Events.Count == 0) throw new InvalidDataException("ユニット音声の定義が見つかりません。");
            foreach (var e in c.Events) foreach(var clip in e.Clips.Where(x=>!x.Available)) c.Warnings.Add(e.Name+": 音声ファイルなし: "+clip.Name);
            return c;
        }
    }
    public static class Paths {
        public static string Inside(string root, string relative) {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(prefix,relative.Replace('/',Path.DirectorySeparatorChar)));
            if (!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("保存先がフォルダーの外を指しています。");
            return path;
        }
    }
    public static class Json {
        static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 24000000, RecursionLimit = 150 }; }
        public static T Read<T>(string path) { return Serializer().Deserialize<T>(File.ReadAllText(path,Encoding.UTF8)); }
        public static void Write(string path, object value) { Atomic.Write(path,Encoding.UTF8.GetBytes(Serializer().Serialize(value))); }
    }
    public static class Atomic {
        public static void Write(string path, byte[] bytes) {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try { File.WriteAllBytes(tmp,bytes); if(File.Exists(path)) File.Replace(tmp,path,null); else File.Move(tmp,path); }
            finally { if(File.Exists(tmp)) File.Delete(tmp); }
        }
    }
    public sealed class Replacement { public string EventName, ClipName, Take; }
    public sealed class Project {
        public string Root;
        public Catalog Catalog;
        public List<Replacement> Replacements;
        public Project(string root, Catalog catalog) { Root = root; Catalog = catalog; string p=Paths.Inside(root,"data/replacements.json"); Replacements=File.Exists(p)?Json.Read<List<Replacement>>(p):new List<Replacement>(); }
        public string ModRoot { get { return Paths.Inside(Root,"UnitVoiceMod"); } }
        public Replacement Find(VoiceEvent e, Clip c) { return Replacements.FirstOrDefault(r=>r.EventName==e.Name && r.ClipName==c.Name); }
        public void Apply(VoiceEvent e, Clip clip, bool all, byte[] wave) {
            Wave.Validate(wave);
            string take="takes/"+Guid.NewGuid().ToString("N")+".wav";
            Atomic.Write(Paths.Inside(Root,"data/"+take),wave);
            var previous=Replacements.ToList();
            foreach(var target in all?e.Clips:new List<Clip>{clip}) {
                Replacements.RemoveAll(r=>r.EventName==e.Name && r.ClipName==target.Name);
                Replacements.Add(new Replacement { EventName=e.Name,ClipName=target.Name,Take=take });
            }
            try { Export(); } catch { Replacements=previous; throw; }
        }
        public void Restore(VoiceEvent e, Clip clip, bool all) {
            var previous=Replacements.ToList(); Replacements.RemoveAll(r=>r.EventName==e.Name && (all||r.ClipName==clip.Name));
            try { Export(); } catch { Replacements=previous; throw; }
        }
        public Dictionary<string,byte[]> BuildFiles() {
            var files=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
            var changes=new Dictionary<string,List<Tuple<int,int,string>>>();
            var additions=new Dictionary<string,StringBuilder>();
            foreach(var group in Replacements.GroupBy(r=>r.EventName)) {
                var e=Catalog.Events.FirstOrDefault(x=>x.Name==group.Key);
                if(e==null) throw new InvalidDataException("音声定義が変わりました。未解決の録音: "+group.Key);
                var source=Catalog.Sources.Single(s=>s.RelativePath==e.SourcePath);
                var block=Clausewitz.Parse(source.Text).Single(n=>n.Key=="soundeffect"&&n.Get("name")==e.Name).Child("sounds");
                if(!changes.ContainsKey(source.VirtualPath)) { changes[source.VirtualPath]=new List<Tuple<int,int,string>>(); additions[source.VirtualPath]=new StringBuilder(); }
                var names=new Dictionary<string,string>();
                foreach(var r in group) {
                    if(!e.Clips.Any(c=>c.Name==r.ClipName)) throw new InvalidDataException("候補が見つかりません: "+r.ClipName);
                    string id="uvr_"+Regex.Replace(e.Name+"_"+r.ClipName,@"[^A-Za-z0-9_]","_");
                    string audio="uvr/"+id+".wav";
                    byte[] data=File.ReadAllBytes(Paths.Inside(Root,"data/"+r.Take)); Wave.Validate(data);
                    files["sound/"+audio]=data; names[r.ClipName]=id;
                    additions[source.VirtualPath].Append("\nsound = { name = \"").Append(id).Append("\" file = \"").Append(audio).Append("\" }\n");
                }
                var text=new StringBuilder("{\n");
                foreach(Node child in block.Children) {
                    if(child.Key=="sound" && names.ContainsKey(child.Value??"")) text.Append("        sound = \"").Append(names[child.Value]).Append("\"\n");
                    else text.Append("        ").Append(source.Text.Substring(child.Start,child.End-child.Start)).Append('\n');
                }
                text.Append("    }");
                changes[source.VirtualPath].Add(Tuple.Create(block.ValueStart,block.ValueEnd,text.ToString()));
            }
            foreach(var item in changes) {
                var sources=Catalog.Sources.Where(s=>s.VirtualPath==item.Key).ToList();
                if(sources.Count!=1) throw new InvalidDataException("同名の音声定義が複数あります: "+item.Key);
                string text=sources[0].Text;
                foreach(var edit in item.Value.OrderByDescending(x=>x.Item1)) text=text.Substring(0,edit.Item1)+edit.Item3+text.Substring(edit.Item2);
                text+="\n# HoI4 Unit Voice Studio recordings\n"+additions[item.Key];
                Clausewitz.Parse(text);
                files[item.Key]=new UTF8Encoding(false).GetBytes(text);
            }
            string descriptor="name=\"HoI4 Unit Voice Studio\"\nversion=\"1.0.0\"\nsupported_version=\""+Catalog.Version+"\"\ntags={\"Sound\"}\n";
            files["descriptor.mod"]=Encoding.UTF8.GetBytes(descriptor);
            return files;
        }
        public void Export() {
            var files=BuildFiles();
            string inventory=Paths.Inside(Root,"data/exported-files.json");
            var old=File.Exists(inventory)?Json.Read<List<string>>(inventory):new List<string>();
            var paths=old.Concat(files.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var backup=new Dictionary<string,byte[]>();
            foreach(string rel in paths) { string path=Paths.Inside(ModRoot,rel); backup[path]=File.Exists(path)?File.ReadAllBytes(path):null; }
            string manifest=Paths.Inside(Root,"data/replacements.json");
            backup[manifest]=File.Exists(manifest)?File.ReadAllBytes(manifest):null;
            backup[inventory]=File.Exists(inventory)?File.ReadAllBytes(inventory):null;
            try {
                foreach(var f in files) Atomic.Write(Paths.Inside(ModRoot,f.Key),f.Value);
                foreach(string rel in old.Where(x=>!files.ContainsKey(x))) {string p=Paths.Inside(ModRoot,rel);if(File.Exists(p))File.Delete(p);}
                Json.Write(manifest,Replacements); Json.Write(inventory,files.Keys.ToList());
                string launcher=Registration.Descriptor(ModRoot,Catalog.Version);
                Atomic.Write(Paths.Inside(Root,"UnitVoiceStudio.mod"),Encoding.UTF8.GetBytes(launcher));
            } catch {
                foreach(var b in backup) {if(b.Value!=null) Atomic.Write(b.Key,b.Value); else if(File.Exists(b.Key))File.Delete(b.Key);}
                throw;
            }
        }
    }
    public static class Wave {
        public static byte[] Create(byte[] pcm) {
            using(var m=new MemoryStream()) using(var w=new BinaryWriter(m)) {
                w.Write(Encoding.ASCII.GetBytes("RIFF"));w.Write(36+pcm.Length);w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(44100);w.Write(88200);w.Write((short)2);w.Write((short)16);w.Write(Encoding.ASCII.GetBytes("data"));w.Write(pcm.Length);w.Write(pcm);return m.ToArray();
            }
        }
        public static void Validate(byte[] data) {
            if(data.Length<44)throw new InvalidDataException("録音が短すぎます。");
            using(var m=new MemoryStream(data))using(var r=new BinaryReader(m)) {
                if(Encoding.ASCII.GetString(r.ReadBytes(4))!="RIFF" || r.ReadUInt32()!=data.Length-8 || Encoding.ASCII.GetString(r.ReadBytes(4))!="WAVE")throw new InvalidDataException("WAV形式ではありません。");
                bool fmt=false,pcm=false;
                while(m.Position+8<=m.Length) {
                    string id=Encoding.ASCII.GetString(r.ReadBytes(4)); uint size=r.ReadUInt32();long end=m.Position+size;
                    if(end>m.Length)throw new InvalidDataException("WAVが破損しています。");
                    if(id=="fmt ") {if(size<16)throw new InvalidDataException("不正なWAV形式");fmt=r.ReadUInt16()==1 && r.ReadUInt16()==1 && r.ReadUInt32()==44100 && r.ReadUInt32()==88200 && r.ReadUInt16()==2 && r.ReadUInt16()==16;}
                    if(id=="data")pcm=size>=8820&&size%2==0;
                    m.Position=end+(size%2);
                }
                if(!fmt||!pcm)throw new InvalidDataException("44.1kHz / 16bit / モノラルPCM WAV（0.1秒以上）が必要です。");
            }
        }
    }
}
