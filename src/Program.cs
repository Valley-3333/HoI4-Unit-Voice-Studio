using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace UnitVoiceStudio {
    static class Program {
        [STAThread] static int Main(string[] args) {
            try {
                if(args.Length>0 && args[0]=="--scan") {
                    if(args.Length!=3)throw new ArgumentException("--scan GAME_DIRECTORY OUTPUT_DIRECTORY");
                    var c=Catalog.Scan(args[1]);c.SaveTo(args[2]);new Project(args[2],c).Export();
                    File.WriteAllText(Paths.Inside(args[2],"data/scan-summary.txt"),Summary(c),Encoding.UTF8);return 0;
                }
                if(args.Length>0 && args[0]=="--self-test") {
                    if(args.Length!=3)throw new ArgumentException("--self-test GAME_DIRECTORY TEST_DIRECTORY");
                    SelfTest.Run(args[1],args[2]);return 0;
                }
                if(args.Length>0 && args[0]=="--preview-test") {
                    if(args.Length!=3)throw new ArgumentException("--preview-test GAME_DIRECTORY TEST_DIRECTORY");
                    SelfTest.Playback(args[1],args[2]);return 0;
                }
                if(args.Length==1&&args[0]=="--unregister") {
                    string root=AppDomain.CurrentDomain.BaseDirectory;var settings=SetupSettings.Load(root);Registration.Unregister(root,settings.UserDataPath);return 0;
                }
                bool first;
                using(var instance=new Mutex(true,"Local\\HOI4UnitVoiceStudio_"+StablePathHash(AppDomain.CurrentDomain.BaseDirectory),out first)) {
                    if(!first){MessageBox.Show("このフォルダーのHoI4 Unit Voice Studioはすでに起動しています。","HoI4 Unit Voice Studio");return 0;}
                    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new MainForm());return 0;
                }
            } catch(Exception e) {
                if(args.Length>0) { string log=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"command-error.txt");File.WriteAllText(log,e.ToString(),Encoding.UTF8);return 1; }
                MessageBox.Show(e.Message,"HoI4 Unit Voice Studio",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;
            }
        }
        static string StablePathHash(string path) {
            using(var hash=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()))).Replace("-","").Substring(0,24);
        }
        internal static string Summary(Catalog c) {
            return "HOI4 version: "+c.Version+"\r\nVoice sets: "+c.Events.Where(e=>e.Country!="COMMON").Select(e=>e.Country).Distinct().Count()+
                "\r\nEvents: "+c.Events.Count+"\r\nClip references: "+c.Events.Sum(e=>e.Clips.Count)+"\r\nUnique clips: "+c.Events.SelectMany(e=>e.Clips).Select(x=>x.Name).Distinct().Count()+
                "\r\nMovement events: "+c.Events.Count(e=>e.Name.EndsWith("_infantry_move_out"))+"\r\nMovement references: "+c.Events.Where(e=>e.Name.EndsWith("_infantry_move_out")).Sum(e=>e.Clips.Count)+
                "\r\nWarnings: "+c.Warnings.Count+"\r\n"+string.Join("\r\n",c.Warnings)+"\r\n";
        }
    }
}
