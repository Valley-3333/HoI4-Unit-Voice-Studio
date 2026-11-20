using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace UnitVoiceStudio {
    public sealed class SetupSettings {
        public string GamePath,UserDataPath;
        public bool UserDataManual,InitialSetupComplete;
        public static SetupSettings Load(string root) {string p=Paths.Inside(root,"data/setup.json");return File.Exists(p)?Json.Read<SetupSettings>(p):new SetupSettings();}
        public void Save(string root){Json.Write(Paths.Inside(root,"data/setup.json"),this);}
    }
    public static class GameLocator {
        public static bool IsGame(string path){return !string.IsNullOrEmpty(path)&&File.Exists(Path.Combine(path,"hoi4.exe"))&&Directory.Exists(Path.Combine(path,"sound"));}
        public static string Find(string saved) {
            if(IsGame(saved))return Path.GetFullPath(saved);
            var steamRoots=new List<string>();
            foreach(string key in new[]{@"HKEY_CURRENT_USER\Software\Valve\Steam",@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam"}) {
                string p=Registry.GetValue(key,"SteamPath",null) as string??Registry.GetValue(key,"InstallPath",null) as string;
                if(!string.IsNullOrEmpty(p))steamRoots.Add(p);
            }
            steamRoots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam"));
            var libraries=new List<string>(steamRoots);
            foreach(string steam in steamRoots.Distinct(StringComparer.OrdinalIgnoreCase)) {
                string file=Path.Combine(steam,"steamapps/libraryfolders.vdf");
                if(File.Exists(file))foreach(Match m in Regex.Matches(File.ReadAllText(file),"\"path\"\\s*\"([^\"]+)\""))libraries.Add(m.Groups[1].Value.Replace("\\\\","\\"));
            }
            foreach(string library in libraries.Distinct(StringComparer.OrdinalIgnoreCase)) {
                string candidate=Path.Combine(library,"steamapps/common/Hearts of Iron IV");if(IsGame(candidate))return Path.GetFullPath(candidate);
                string manifest=Path.Combine(library,"steamapps/appmanifest_394360.acf");
                if(File.Exists(manifest)){var m=Regex.Match(File.ReadAllText(manifest),"\"installdir\"\\s*\"([^\"]+)\"");if(m.Success){candidate=Path.Combine(library,"steamapps/common",m.Groups[1].Value);if(IsGame(candidate))return Path.GetFullPath(candidate);}}
            }
            return null;
        }
    }
    public sealed class UserDataCandidate {
        public string Path,Source;public int Priority;
        public override string ToString(){return Source+" — "+Path;}
    }
    public sealed class DetectionResult {
        public List<UserDataCandidate> Candidates=new List<UserDataCandidate>();
        public UserDataCandidate Automatic;
    }
    public static class UserDataLocator {
        public static bool Same(string a,string b) {
            if(string.IsNullOrEmpty(a)||string.IsNullOrEmpty(b))return false;
            return string.Equals(System.IO.Path.GetFullPath(a).TrimEnd('\\','/'),System.IO.Path.GetFullPath(b).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase);
        }
        public static bool IsUserData(string dir) {
            if(string.IsNullOrWhiteSpace(dir)||!Directory.Exists(dir))return false;
            return new[]{"settings.txt","pdx_settings.txt","pdx_settings.json","gameplaysettings.txt","launcher-v2.sqlite","dlc_load.json","game_data.json"}.Any(f=>File.Exists(System.IO.Path.Combine(dir,f)))||
                (Directory.Exists(System.IO.Path.Combine(dir,"save games"))&&Directory.Exists(System.IO.Path.Combine(dir,"logs")));
        }
        public static DetectionResult Detect(string game,SetupSettings settings) {
            string documents=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments,Environment.SpecialFolderOption.DoNotVerify);
            var otherDocs=new List<string>();
            foreach(string key in new[]{"OneDrive","OneDriveConsumer","OneDriveCommercial"}){string p=Environment.GetEnvironmentVariable(key);if(!string.IsNullOrEmpty(p))otherDocs.Add(System.IO.Path.Combine(p,"Documents"));}
            string profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);if(!string.IsNullOrEmpty(profile))otherDocs.Add(System.IO.Path.Combine(profile,"Documents"));
            return Detect(game,settings,documents,otherDocs);
        }
        // The injectable documents paths let tests simulate OneDrive and renamed/redirected folders.
        internal static DetectionResult Detect(string game,SetupSettings settings,string documents,IEnumerable<string> otherDocuments) {
            var r=new DetectionResult();
            Action<string,string,int> add=delegate(string path,string source,int priority){
                if(string.IsNullOrWhiteSpace(path))return;
                path=System.IO.Path.GetFullPath(path);
                if(!IsUserData(path))return;
                var existing=r.Candidates.FirstOrDefault(c=>Same(c.Path,path));
                if(existing!=null){if(existing.Priority<priority){existing.Priority=priority;existing.Source=source;}return;}
                r.Candidates.Add(new UserDataCandidate{Path=path,Source=source,Priority=priority});
            };
            if(settings!=null)add(settings.UserDataPath,settings.UserDataManual?"選択済みの保存先":"前回の保存先",settings.UserDataManual?110:40);
            if(!string.IsNullOrEmpty(game)) {
                string userdir=System.IO.Path.Combine(game,"userdir.txt");
                if(File.Exists(userdir)) {
                    string line=File.ReadAllLines(userdir).FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x)&&!x.TrimStart().StartsWith("#"));
                    if(line!=null){line=Environment.ExpandEnvironmentVariables(line.Trim().Trim('"'));if(!System.IO.Path.IsPathRooted(line))line=System.IO.Path.Combine(game,line);add(line,"本体の userdir.txt",100);}
                }
                string launcher=System.IO.Path.Combine(game,"launcher-settings.json");
                if(File.Exists(launcher)) {
                    var m=Regex.Match(File.ReadAllText(launcher),"\"gameDataPath\"\\s*:\\s*\"([^\"]+)\"");
                    if(m.Success){string raw=m.Groups[1].Value.Replace("\\\\","\\");if(!raw.Contains("%USER_DOCUMENTS%")||!string.IsNullOrEmpty(documents)){string path=raw.Replace("%USER_DOCUMENTS%",documents??"");path=Environment.ExpandEnvironmentVariables(path);if(System.IO.Path.IsPathRooted(path)&&!path.Contains("%"))add(path,"ランチャーの保存先",raw.Contains("%USER_DOCUMENTS%")?80:100);}}
                }
            }
            if(!string.IsNullOrEmpty(documents))add(System.IO.Path.Combine(documents,"Paradox Interactive/Hearts of Iron IV"),"Windowsのドキュメント",80);
            foreach(string docs in otherDocuments??Enumerable.Empty<string>())if(!string.IsNullOrEmpty(docs))add(System.IO.Path.Combine(docs,"Paradox Interactive/Hearts of Iron IV"),"別のドキュメント候補",20);
            r.Candidates=r.Candidates.OrderByDescending(c=>c.Priority).ThenBy(c=>c.Path,StringComparer.OrdinalIgnoreCase).ToList();
            if(r.Candidates.Count==1||(r.Candidates.Count>1&&r.Candidates[0].Priority>r.Candidates[1].Priority))r.Automatic=r.Candidates[0];
            return r;
        }
    }
    public static class Registration {
        public static string Target(string userData){return Paths.Inside(userData,"mod/UnitVoiceStudio.mod");}
        public static string Descriptor(string modRoot,string version) {
            if(modRoot.IndexOfAny(new[]{'"','\r','\n'})>=0)throw new InvalidDataException("保存先の文字をMod登録ファイルで扱えません。");
            return "# UnitVoiceStudio managed registration\nname=\"Unit Voice Studio\"\npath=\""+System.IO.Path.GetFullPath(modRoot).Replace('\\','/')+"\"\nsupported_version=\""+version+"\"\ntags={\"Sound\"}\n";
        }
        static string ReferencedPath(string file) {
            var nodes=Clausewitz.Parse(File.ReadAllText(file));var n=nodes.FirstOrDefault(x=>x.Key=="path");return n==null?null:n.Value;
        }
        static bool OwnName(string file){return Clausewitz.Parse(File.ReadAllText(file)).Any(x=>x.Key=="name"&&x.Value=="Unit Voice Studio");}
        public static string Status(string root,string userData) {
            if(!UserDataLocator.IsUserData(userData))return "保存先を確認できません。HOI4を一度起動するか、導入設定で保存先を選んでください。";
            string file=Target(userData);if(!File.Exists(file))return "未登録 — 自動登録または修復が必要です。";
            try {
                if(!OwnName(file))return "同名の登録ファイルが別のModに使われています。";
                if(!UserDataLocator.Same(ReferencedPath(file),Paths.Inside(root,"UnitVoiceMod")))return "別の設置場所を参照しています。登録の修復が必要です。";
                if(!File.Exists(Paths.Inside(root,"UnitVoiceMod/descriptor.mod")))return "Mod本体が見つかりません。登録の修復が必要です。";
                return "登録済み — ランチャーのプレイセットへ追加できます。";
            }catch{return "登録ファイルを読み取れません。登録の修復が必要です。";}
        }
        public static void Register(string root,string userData,bool explicitRepair) {
            if(!UserDataLocator.IsUserData(userData))throw new InvalidDataException("HOI4の保存先を確認できません。settings.txt や launcher-v2.sqlite のあるフォルダーを選択してください。初めての場合はHOI4を一度起動してください。");
            string source=Paths.Inside(root,"UnitVoiceStudio.mod"),destination=Target(userData);
            if(!File.Exists(source)||!File.Exists(Paths.Inside(root,"UnitVoiceMod/descriptor.mod")))throw new InvalidDataException("Mod本体が準備できていません。本体フォルダーを選択してください。");
            if(File.Exists(destination)) {
                if(File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(destination)))return;
                if(!explicitRepair) {
                    try{if(!OwnName(destination)||!UserDataLocator.Same(ReferencedPath(destination),Paths.Inside(root,"UnitVoiceMod")))throw new InvalidDataException("既存の登録を保護しました。導入設定で内容を確認し、「登録・修復」を押してください。");}
                    catch(InvalidDataException){throw;}catch{throw new InvalidDataException("既存の登録を自動で変更できません。導入設定から登録を修復してください。");}
                }
                File.Copy(destination,destination+".backup-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));
            }
            Atomic.Write(destination,File.ReadAllBytes(source));
            if(!Status(root,userData).StartsWith("登録済み"))throw new IOException("登録ファイルの確認に失敗しました。");
        }
        public static bool Unregister(string root,string userData) {
            if(string.IsNullOrEmpty(userData))return false;string path=Target(userData);if(!File.Exists(path))return false;
            try{if(!OwnName(path)||!UserDataLocator.Same(ReferencedPath(path),Paths.Inside(root,"UnitVoiceMod")))return false;}catch{return false;}
            File.Delete(path);return true;
        }
    }
    public static class Migration {
        public static int Import(string source,string target,Catalog catalog) {
            if(UserDataLocator.Same(source,target))throw new InvalidDataException("現在のフォルダーと同じです。");
            string manifest=Paths.Inside(source,"data/replacements.json");if(!File.Exists(manifest))throw new InvalidDataException("旧版のexeとdataフォルダーのある場所を選択してください。");
            var imported=Json.Read<List<Replacement>>(manifest);var project=new Project(target,catalog);
            foreach(var r in imported) {
                var e=catalog.Events.FirstOrDefault(x=>x.Name==r.EventName);if(e==null||!e.Clips.Any(c=>c.Name==r.ClipName))throw new InvalidDataException("現在の本体にない音声候補があるため、引き継ぎを中止しました: "+r.EventName+" / "+r.ClipName);
                string p=Paths.Inside(source,"data/"+r.Take);Wave.Validate(File.ReadAllBytes(p));
            }
            var copies=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            string history=Paths.Inside(source,"data/takes");
            var files=Directory.Exists(history)?Directory.GetFiles(history,"*.wav").ToList():new List<string>();
            files.AddRange(imported.Select(r=>Paths.Inside(source,"data/"+r.Take)));
            foreach(string file in files.Distinct(StringComparer.OrdinalIgnoreCase)) {
                string take="takes/"+Guid.NewGuid().ToString("N")+".wav";
                Atomic.Write(Paths.Inside(target,"data/"+take),File.ReadAllBytes(file));copies[System.IO.Path.GetFullPath(file)]=take;
            }
            foreach(var r in imported)if(!project.Replacements.Any(x=>x.EventName==r.EventName&&x.ClipName==r.ClipName))project.Replacements.Add(new Replacement{EventName=r.EventName,ClipName=r.ClipName,Take=copies[Paths.Inside(source,"data/"+r.Take)]});
            project.Export();return copies.Count;
        }
    }
}
