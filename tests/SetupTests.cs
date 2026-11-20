using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UnitVoiceStudio {
    static class SetupTests {
        internal static List<string> Run(string game,string output,Catalog catalog) {
            var report=new List<string>();
            Action<bool,string> check=delegate(bool ok,string label){if(!ok)throw new Exception("FAIL: Setup: "+label);report.Add("PASS: Setup: "+label);};
            Action<Action,string> reject=delegate(Action action,string label){bool caught=false;try{action();}catch{caught=true;}check(caught,label);};
            string root=Paths.Inside(output,"setup-fixtures");Directory.CreateDirectory(root);
            Func<string,string> user=delegate(string path){Directory.CreateDirectory(path);Atomic.Write(Paths.Inside(path,"settings.txt"),Encoding.UTF8.GetBytes("# test fixture"));return path;};
            string redirected=Paths.Inside(root,"OneDrive - テスト組織/ドキュメント");
            string redirectedGame=user(Paths.Inside(redirected,"Paradox Interactive/Hearts of Iron IV"));
            string previousDocs=Paths.Inside(root,"Old Documents");string previousGame=user(Paths.Inside(previousDocs,"Paradox Interactive/Hearts of Iron IV"));
            var r=UserDataLocator.Detect(null,new SetupSettings(),redirected,new[]{previousDocs});
            check(r.Automatic!=null&&UserDataLocator.Same(r.Automatic.Path,redirectedGame),"Redirected OneDrive Documents with Japanese name preferred over old Documents");
            r=UserDataLocator.Detect(null,new SetupSettings{UserDataPath=previousGame},redirected,new[]{previousDocs});
            check(UserDataLocator.Same(r.Automatic.Path,redirectedGame),"Moved Documents supersedes stale automatic saved path");
            r=UserDataLocator.Detect(null,new SetupSettings{UserDataPath=previousGame,UserDataManual=true},redirected,new[]{previousDocs});
            check(UserDataLocator.Same(r.Automatic.Path,previousGame),"Explicit saved folder is respected");
            r=UserDataLocator.Detect(null,new SetupSettings(),null,new[]{previousDocs,redirected});
            check(r.Automatic==null&&r.Candidates.Count==2,"Multiple equally valid fallback folders require a choice");
            string missing=Paths.Inside(root,"No game data yet");r=UserDataLocator.Detect(null,new SetupSettings(),missing,new string[0]);
            check(r.Automatic==null&&!Directory.Exists(missing),"Detection does not create guessed empty folders");
            string fakeGame=Paths.Inside(root,"Steam Library/common/Hearts of Iron IV");Directory.CreateDirectory(fakeGame);
            Atomic.Write(Paths.Inside(fakeGame,"userdir.txt"),Encoding.UTF8.GetBytes("\""+redirectedGame+"\""));
            r=UserDataLocator.Detect(fakeGame,new SetupSettings(),previousDocs,new string[0]);
            check(UserDataLocator.Same(r.Automatic.Path,redirectedGame),"Explicit userdir.txt is honored");
            File.Delete(Paths.Inside(fakeGame,"userdir.txt"));
            Json.Write(Paths.Inside(fakeGame,"launcher-settings.json"),new Dictionary<string,string>{{"gameDataPath","%USER_DOCUMENTS%/Paradox Interactive/Hearts of Iron IV"}});
            r=UserDataLocator.Detect(fakeGame,new SetupSettings(),redirected,new[]{previousDocs});
            check(UserDataLocator.Same(r.Automatic.Path,redirectedGame),"Launcher USER_DOCUMENTS resolves to redirected Documents");
            check(UserDataLocator.Same(GameLocator.Find(game),game),"Existing game path accepted");
            string projectRoot=Paths.Inside(root,"Installed app");Directory.CreateDirectory(projectRoot);new Project(projectRoot,catalog).Export();
            var settings=new SetupSettings{GamePath=game,UserDataPath=redirectedGame,UserDataManual=true,InitialSetupComplete=true};settings.Save(projectRoot);
            check(SetupSettings.Load(projectRoot).UserDataPath==redirectedGame,"Setup configuration persists Unicode and spaces");
            Registration.Register(projectRoot,redirectedGame,false);
            check(Registration.Status(projectRoot,redirectedGame).StartsWith("登録済み"),"Automatic registration verifies Mod path");
            string descriptor=Registration.Target(redirectedGame);string modDir=Path.GetDirectoryName(descriptor);
            Registration.Register(projectRoot,redirectedGame,false);
            check(Directory.GetFiles(modDir,"*.backup-*").Length==0,"Repeated registration is idempotent");
            string other=Paths.Inside(root,"Other copy/UnitVoiceMod");Atomic.Write(descriptor,Encoding.UTF8.GetBytes(Registration.Descriptor(other,"1.20.*")));
            byte[] old=File.ReadAllBytes(descriptor);
            reject(()=>Registration.Register(projectRoot,redirectedGame,false),"Automatic registration protects another installed copy");
            check(File.ReadAllBytes(descriptor).SequenceEqual(old),"Protected registration remains unchanged");
            Registration.Register(projectRoot,redirectedGame,true);
            check(Registration.Status(projectRoot,redirectedGame).StartsWith("登録済み")&&Directory.GetFiles(modDir,"*.backup-*").Any(p=>File.ReadAllBytes(p).SequenceEqual(old)),"Repair backs up old descriptor and fixes moved Mod path");
            File.Delete(descriptor);Registration.Register(projectRoot,redirectedGame,false);
            check(File.Exists(descriptor),"Missing registration repairs automatically");
            string neighbor=Paths.Inside(redirectedGame,"mod/OtherMod.mod");Atomic.Write(neighbor,Encoding.UTF8.GetBytes("other mod"));
            check(Registration.Unregister(projectRoot,redirectedGame)&&!File.Exists(descriptor)&&File.Exists(neighbor),"Unregister removes only its own descriptor");
            Atomic.Write(descriptor,old);
            check(!Registration.Unregister(projectRoot,redirectedGame)&&File.ReadAllBytes(descriptor).SequenceEqual(old),"Unregister preserves descriptor referencing another copy");
            reject(()=>Registration.Register(projectRoot,missing,true),"Registration refuses an unverified destination");
            string oldRoot=Paths.Inside(root,"Portable old app");Directory.CreateDirectory(oldRoot);
            var e=catalog.Events.First(x=>x.Name=="JAP_infantry_move_out");byte[] sound=Wave.Create(new byte[8820]);
            var oldProject=new Project(oldRoot,catalog);oldProject.Apply(e,e.Clips[0],false,sound);
            Atomic.Write(Paths.Inside(oldRoot,"data/takes/history-only.wav"),sound);
            byte[] sourceManifest=File.ReadAllBytes(Paths.Inside(oldRoot,"data/replacements.json"));
            check(Migration.Import(oldRoot,projectRoot,catalog)==2,"Portable migration imports assigned take and unassigned history");
            var migrated=new Project(projectRoot,catalog);
            check(migrated.Replacements.Count==1&&File.Exists(Paths.Inside(projectRoot,"data/"+migrated.Replacements[0].Take)),"Migrated assignment references copied take");
            check(File.ReadAllBytes(Paths.Inside(oldRoot,"data/replacements.json")).SequenceEqual(sourceManifest)&&File.Exists(Paths.Inside(oldRoot,"data/takes/history-only.wav")),"Migration preserves portable source files");
            string retained=migrated.Replacements[0].Take;Migration.Import(oldRoot,projectRoot,catalog);
            check(new Project(projectRoot,catalog).Replacements[0].Take==retained,"Migration preserves existing target assignments on conflicts");
            Json.Write(Paths.Inside(oldRoot,"data/replacements.json"),new[]{new Replacement{EventName="DOES_NOT_EXIST",ClipName="none",Take="takes/history-only.wav"}});
            byte[] before=File.ReadAllBytes(Paths.Inside(projectRoot,"data/replacements.json"));
            reject(()=>Migration.Import(oldRoot,projectRoot,catalog),"Migration rejects unresolved candidates");
            check(File.ReadAllBytes(Paths.Inside(projectRoot,"data/replacements.json")).SequenceEqual(before),"Failed migration leaves target manifest intact");
            return report;
        }
    }
}
