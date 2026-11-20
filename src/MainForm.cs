using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace UnitVoiceStudio {
    public sealed class MainForm : Form {
        readonly string root=AppDomain.CurrentDomain.BaseDirectory;
        Catalog catalog; Project project;
        SetupSettings setup;
        readonly Label registrationStatus=new Label();
        readonly ComboBox country=new ComboBox(), usage=new ComboBox(), clip=new ComboBox(), microphone=new ComboBox();
        readonly CheckBox confirm=new CheckBox(), all=new CheckBox();
        readonly Label details=new Label(), status=new Label(), time=new Label(), progress=new Label();
        readonly Button record=new Button(), stop=new Button(), apply=new Button(), listen=new Button(), original=new Button(), restore=new Button(), scan=new Button(), register=new Button();
        readonly Button discard=new Button();
        readonly ProgressBar meter=new ProgressBar();
        readonly Timer timer=new Timer();
        readonly List<Control> locked=new List<Control>();
        Recorder recorder; byte[] take;
        PreviewAudio player;
        bool previewOriginal;
        bool dirty,loading;
        sealed class CountryChoice {public string Tag;public override string ToString(){return Catalog.CountryLabel(Tag);}}
        VoiceEvent SelectedEvent {get{return usage.SelectedItem as VoiceEvent;}}
        Clip SelectedClip {get{return clip.SelectedItem as Clip;}}
        public MainForm() {
            Text="HOI4 Unit Voice Studio — ユニット音声レコーダー";
            Font=new Font("Yu Gothic UI",10);AutoScaleMode=AutoScaleMode.Dpi;
            ClientSize=new Size(1050,790);MinimumSize=new Size(950,810);StartPosition=FormStartPosition.CenterScreen;
            BackColor=Color.FromArgb(241,244,248);
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=7};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,74));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,80));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,172));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,134));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));Controls.Add(layout);
            var title=new Label{Text="UNIT VOICE STUDIO\n国と用途を確認して、あなたの声をユニットへ。",Dock=DockStyle.Fill,Font=new Font("Yu Gothic UI",16,FontStyle.Bold),ForeColor=Color.FromArgb(26,44,67)};layout.Controls.Add(title);
            var tools=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};
            AddButton(tools,scan,"本体を選択・再読込",ScanGame,180);
            AddButton(tools,register,"導入・登録設定",Register,165);
            AddButton(tools,new Button(),"Modを開く",delegate{Directory.CreateDirectory(project==null?Paths.Inside(root,"UnitVoiceMod"):project.ModRoot);Process.Start("explorer.exe",Paths.Inside(root,"UnitVoiceMod"));},115);
            progress.Text="読込待ち";progress.AutoSize=true;progress.Margin=new Padding(18,10,0,0);tools.Controls.Add(progress);
            var toolBox=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};toolBox.RowStyles.Add(new RowStyle(SizeType.Absolute,42));toolBox.RowStyles.Add(new RowStyle(SizeType.Absolute,32));toolBox.Controls.Add(tools);registrationStatus.Text="登録状態を確認しています…";registrationStatus.Dock=DockStyle.Fill;registrationStatus.AutoEllipsis=true;registrationStatus.Font=new Font(Font.FontFamily,9);toolBox.Controls.Add(registrationStatus);layout.Controls.Add(toolBox);
            var selectors=new TableLayoutPanel{Dock=DockStyle.Fill,BackColor=Color.White,Padding=new Padding(14),ColumnCount=2,RowCount=3};selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,155));selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            Selector(selectors,"1   国・音声セット",country,0);Selector(selectors,"2   音声の用途",usage,1);Selector(selectors,"3   音声候補",clip,2);layout.Controls.Add(selectors);
            details.Dock=DockStyle.Fill;details.Padding=new Padding(16,12,10,4);details.BackColor=Color.White;details.ForeColor=Color.FromArgb(35,48,65);details.AutoEllipsis=true;layout.Controls.Add(details);
            var checkRow=new FlowLayoutPanel{Dock=DockStyle.Fill};
            confirm.Text="国・用途・対象を確認しました";confirm.AutoSize=true;confirm.Margin=new Padding(5,10,20,0);confirm.CheckedChanged+=delegate{UpdateButtons();if(recorder==null&&(player==null||!player.IsPlaying))status.Text=RecordingHint();};checkRow.Controls.Add(confirm);
            all.Text="この用途の全候補を同じ録音にする";all.AutoSize=true;all.Margin=new Padding(0,10,0,0);all.CheckedChanged+=delegate{SelectionChanged();};checkRow.Controls.Add(all);layout.Controls.Add(checkRow);
            AddButton(checkRow,discard,"未反映の録音を破棄",delegate{if(DiscardTake())SelectionChanged();},175);discard.Margin=new Padding(16,4,0,0);
            var audio=new TableLayoutPanel{Dock=DockStyle.Fill,BackColor=Color.White,Padding=new Padding(12),RowCount=3,ColumnCount=1};
            audio.RowStyles.Add(new RowStyle(SizeType.Absolute,42));audio.RowStyles.Add(new RowStyle(SizeType.Absolute,42));audio.RowStyles.Add(new RowStyle(SizeType.Absolute,26));
            var inputs=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};inputs.Controls.Add(new Label{Text="マイク",AutoSize=true,Margin=new Padding(0,7,10,0)});microphone.Width=430;microphone.DropDownStyle=ComboBoxStyle.DropDownList;inputs.Controls.Add(microphone);locked.Add(microphone);
            AddButton(inputs,new Button(),"入力を再検索",RefreshDevices,130);audio.Controls.Add(inputs,0,0);
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};
            AddButton(buttons,original,"元音声を聴く",PlayOriginal,130);AddButton(buttons,record,"● 録音開始",StartRecording,130);record.BackColor=Color.FromArgb(194,49,66);record.ForeColor=Color.White;
            AddButton(buttons,stop,"■ 停止",StopAudio,95);AddButton(buttons,listen,"録音を聴く",PlayTake,120);AddButton(buttons,apply,"Modに反映",ApplyTake,120);apply.BackColor=Color.FromArgb(31,94,156);apply.ForeColor=Color.White;
            AddButton(buttons,restore,"元に戻す",Restore,110);audio.Controls.Add(buttons,0,1);
            var levels=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};meter.Width=380;meter.Height=16;meter.Margin=new Padding(0,5,16,0);levels.Controls.Add(meter);time.Text="00.0 / 30秒";time.AutoSize=true;levels.Controls.Add(time);audio.Controls.Add(levels,0,2);layout.Controls.Add(audio);
            status.Text="本体を読み込んでください。";status.Dock=DockStyle.Fill;status.Padding=new Padding(0,12,0,0);status.AutoEllipsis=true;layout.Controls.Add(status);
            country.SelectedIndexChanged+=delegate{if(loading)return;LoadUsages();};
            usage.SelectedIndexChanged+=delegate{if(loading)return;LoadClips();};
            clip.SelectedIndexChanged+=delegate{if(!loading)SelectionChanged();};
            microphone.SelectedIndexChanged+=delegate{UpdateButtons();};
            timer.Interval=70;timer.Tick+=delegate{
                if(player!=null&&!player.IsPlaying)Run(delegate{if(player.CompleteIfFinished()){player=null;status.Text=previewOriginal?"元音声の再生が終了しました。"+RecordingHint():"録音の再生が終了しました。"+(dirty?"「Modに反映」で保存できます。":RecordingHint());}});
                if(recorder==null)return;meter.Value=Math.Min(100,recorder.Level);time.Text=recorder.Seconds.ToString("00.0")+" / 30秒";
                if(recorder.Error!=null){string error=recorder.Error;Run(StopRecording);status.Text=error;}else if(recorder.Seconds>=30)Run(StopRecording);
            };
            timer.Start();
            Shown+=delegate{Run(delegate{setup=SetupSettings.Load(root);RefreshDevices();LoadInitial();RefreshRegistration(true);});};
            FormClosing+=delegate(object sender,FormClosingEventArgs e){
                if((dirty||recorder!=null) && MessageBox.Show(this,"未反映の録音があります。閉じるとこの録音は失われます。閉じますか？","録音の確認",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes){e.Cancel=true;return;}
                timer.Stop();if(recorder!=null){recorder.Dispose();recorder=null;}StopPlayer();
            };
        }
        void AddButton(FlowLayoutPanel panel,Button b,string text,Action action,int width) {
            b.Text=text;b.Width=width;b.Height=34;b.FlatStyle=FlatStyle.Flat;b.Margin=new Padding(0,0,8,0);b.Click+=delegate{Run(action);};panel.Controls.Add(b);
            if(b!=stop&&b!=listen)locked.Add(b);
        }
        void Selector(TableLayoutPanel p,string text,ComboBox box,int row) {
            p.Controls.Add(new Label{Text=text,AutoSize=true,Anchor=AnchorStyles.Left},0,row);box.Dock=DockStyle.Fill;box.DropDownStyle=ComboBoxStyle.DropDownList;box.Margin=new Padding(0,6,0,6);p.Controls.Add(box,1,row);locked.Add(box);
        }
        void Run(Action action) {try{action();}catch(Exception e){status.Text=e.Message;MessageBox.Show(this,e.Message,"操作を完了できませんでした",MessageBoxButtons.OK,MessageBoxIcon.Warning);}finally{UpdateButtons();}}
        void LoadInitial() {
            string path=Paths.Inside(root,"data/catalog.json");
            if(File.Exists(path)){var cached=Json.Read<Catalog>(path);if(GameLocator.IsGame(cached.GamePath)){SetCatalog(cached);return;}}
            string game=GameLocator.Find(setup.GamePath);if(game!=null)ReadGame(game);else status.Text="「導入・登録設定」からHOI4本体を選択してください。";
        }
        public static string FindGame() {
            return GameLocator.Find(null);
        }
        void SetCatalog(Catalog c) {
            catalog=c;project=new Project(root,catalog);project.Export();loading=true;country.Items.Clear();
            if(setup!=null){setup.GamePath=c.GamePath;setup.Save(root);}
            foreach(string tag in c.Events.Select(e=>e.Country).Distinct())country.Items.Add(new CountryChoice{Tag=tag});
            loading=false;if(country.Items.Count>0)country.SelectedIndex=0;
            progress.Text=c.Events.Where(e=>e.Country!="COMMON").Select(e=>e.Country).Distinct().Count()+" 音声セット / "+c.Events.Count+" 用途";
            status.Text="読込完了。元音声を聴き、国と用途を確認してください。";
            if(c.Warnings.Count>0)status.Text="読込完了（元ファイルなし等 "+c.Warnings.Count+" 件）。詳細は data/catalog.json を確認できます。";
        }
        void ReadGame(string path) {
            UseWaitCursor=true;try{var c=Catalog.Scan(path);var p=new Project(root,c);p.BuildFiles();c.SaveTo(root);SetCatalog(c);}finally{UseWaitCursor=false;}
        }
        bool DiscardTake() {
            if(dirty&&MessageBox.Show(this,"未反映の録音があります。破棄して続けますか？","録音の確認",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return false;
            dirty=false;take=null;return true;
        }
        void ScanGame() {
            if(!DiscardTake())return;
            using(var d=new FolderBrowserDialog{Description="hoi4.exe のある Hearts of Iron IV フォルダーを選択",SelectedPath=catalog==null?(FindGame()??""):catalog.GamePath})if(d.ShowDialog(this)==DialogResult.OK)ReadGame(d.SelectedPath);
        }
        void LoadUsages() {
            loading=true;usage.Items.Clear();var c=country.SelectedItem as CountryChoice;
            if(c!=null)foreach(var e in catalog.Events.Where(e=>e.Country==c.Tag))usage.Items.Add(e);
            loading=false;if(usage.Items.Count>0)usage.SelectedIndex=0;else LoadClips();
        }
        void LoadClips() {
            loading=true;clip.Items.Clear();if(SelectedEvent!=null)foreach(var c in SelectedEvent.Clips)clip.Items.Add(c);
            loading=false;if(clip.Items.Count>0)clip.SelectedIndex=0;else SelectionChanged();
        }
        void SelectionChanged() {
            StopPlayer();confirm.Checked=false;take=null;dirty=false;meter.Value=0;time.Text="00.0 / 30秒";
            var e=SelectedEvent;var c=SelectedClip;
            if(e==null||c==null){details.Text="国と用途を選択してください。";UpdateButtons();return;}
            var r=project.Find(e,c);
            if(r!=null){string p=Paths.Inside(root,"data/"+r.Take);if(File.Exists(p))take=File.ReadAllBytes(p);}
            string scope=all.Checked?"この用途の全 "+e.Clips.Count+" 候補を同じ録音へ置換":"選択した候補だけを置換（ほかの用途には影響しません）";
            details.Text="国："+Catalog.CountryLabel(e.Country)+"     用途："+e.Usage+"\n"+
                "対象："+scope+"\n"+"音声ID："+e.Name+" / "+c.Name+"\n"+
                "提供元："+e.Pack+"\n"+"元ファイル："+(c.RelativePath??"未解決")+"\n"+
                "状態："+(r==null?"未収録（元音声）":"録音を反映済み")+"   |   この用途の反映数："+e.Clips.Count(x=>project.Find(e,x)!=null)+" / "+e.Clips.Count;
            status.Text=dirty?"録音をModに反映してください。":"元音声を聴いてから確認欄にチェックし、録音を開始します。";
            UpdateButtons();
        }
        void UpdateButtons() {
            bool active=recorder!=null,selected=SelectedEvent!=null&&SelectedClip!=null;
            foreach(var c in locked)c.Enabled=!active;
            confirm.Enabled=all.Enabled=!active&&selected;
            record.Enabled=!active&&selected&&confirm.Checked&&microphone.SelectedItem!=null;
            stop.Enabled=active||(player!=null&&player.IsPlaying);listen.Enabled=!active&&take!=null;
            apply.Enabled=!active&&take!=null&&dirty&&selected;
            original.Enabled=!active&&selected&&OriginalPath()!=null;
            restore.Enabled=!active&&selected&&(all.Checked?project.Replacements.Any(r=>r.EventName==SelectedEvent.Name):project.Find(SelectedEvent,SelectedClip)!=null);
            // Keep an uncommitted take attached to its exact selection until applied or rerecorded.
            if(dirty&&!active){country.Enabled=usage.Enabled=clip.Enabled=all.Enabled=false;}
            register.Enabled=!active;
            discard.Enabled=!active&&dirty;
        }
        void RefreshDevices() {microphone.Items.Clear();foreach(var d in Recorder.Devices())microphone.Items.Add(d);if(microphone.Items.Count>0)microphone.SelectedIndex=0;else status.Text="マイクが見つかりません。接続とWindowsのマイク設定を確認してください。";}
        void StopPlayer() {if(player!=null){player.Dispose();player=null;}}
        string RecordingHint() {
            if(SelectedEvent==null||SelectedClip==null)return "国・用途・音声候補を選択してください。";
            if(microphone.SelectedItem==null)return "マイクを接続し、「入力を再検索」を押してください。";
            return confirm.Checked?"「録音開始」を押すと録音できます。":"「国・用途・対象を確認しました」にチェックすると録音できます。";
        }
        void StopAudio() {
            if(recorder!=null){StopRecording();return;}
            if(player!=null){StopPlayer();status.Text="再生を停止しました。"+RecordingHint();}
        }
        string OriginalPath() {
            var c=SelectedClip;if(c==null)return null;
            if(c.ReferencePath!=null){string p=Paths.Inside(root,"data/"+c.ReferencePath);if(File.Exists(p))return p;}
            if(c.RelativePath!=null){string p=Paths.Inside(catalog.GamePath,c.RelativePath);if(File.Exists(p))return p;}
            return null;
        }
        void PlayOriginal() {
            StopPlayer();player=new PreviewAudio();player.Play(File.ReadAllBytes(OriginalPath()));previewOriginal=true;status.Text="元音声を再生中："+SelectedClip.Name+"（「停止」または「録音開始」で再生を停止）。";
        }
        void PlayTake() {StopPlayer();player=new PreviewAudio();player.Play(take);previewOriginal=false;status.Text="録音を再生中。「停止」で止められます。";}
        void StartRecording() {
            if(dirty&&MessageBox.Show(this,"未反映の録音を破棄して録り直しますか？","録り直し",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
            StopPlayer();recorder=new Recorder(((InputDevice)microphone.SelectedItem).Id);take=null;dirty=false;UpdateButtons();status.Text="録音中："+Catalog.CountryLabel(SelectedEvent.Country)+" / "+SelectedEvent.Usage+"。最大30秒。";
        }
        void StopRecording() {
            if(recorder==null)return;var current=recorder;recorder=null;take=current.Stop();string error=current.Error;meter.Value=0;UpdateButtons();
            if(error!=null){take=null;throw new InvalidOperationException(error);}
            try{Wave.Validate(take);}catch{take=null;throw;}
            dirty=true;UpdateButtons();status.Text="録音を停止しました。「録音を聴く」で確認し、「Modに反映」で保存してください。";
        }
        void ApplyTake() {
            project.Apply(SelectedEvent,SelectedClip,all.Checked,take);dirty=false;SelectionChanged();status.Text="Modへ反映しました。HOI4を再起動して反映を確認できます。";
        }
        void Restore() {
            if(!DiscardTake())return;
            if(MessageBox.Show(this,all.Checked?"この用途の全候補を元音声に戻しますか？":"この候補を元音声に戻しますか？","元音声へ戻す",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            project.Restore(SelectedEvent,SelectedClip,all.Checked);SelectionChanged();status.Text="元音声へ戻しました。録音履歴は data/takes に残ります。";
        }
        void Register() {
            if(setup==null)setup=SetupSettings.Load(root);
            StopPlayer();
            using(var dialog=new SetupForm(root,setup,delegate(string game,string userData,bool manual){
                if(!GameLocator.IsGame(game))throw new InvalidDataException("HOI4本体を選択してください。");
                if(catalog==null||!UserDataLocator.Same(catalog.GamePath,game)) {if(!DiscardTake())throw new InvalidOperationException("録音を反映または破棄してから本体を変更してください。");ReadGame(game);}
                project.Export();Registration.Register(root,userData,true);setup.GamePath=game;setup.UserDataPath=userData;setup.UserDataManual=manual;setup.InitialSetupComplete=true;setup.Save(root);
            },delegate(string source){
                if(!DiscardTake())return;
                if(catalog==null)throw new InvalidOperationException("先に本体を選び、「登録・修復」を完了してから引き継いでください。");
                int count=Migration.Import(source,root,catalog);project=new Project(root,catalog);SelectionChanged();status.Text=count+" 件の録音履歴を引き継ぎました。";
            }))dialog.ShowDialog(this);
            RefreshRegistration(false);
        }
        void RefreshRegistration(bool initial) {
            string error=null;
            if(catalog!=null) {
                var detection=UserDataLocator.Detect(catalog.GamePath,setup);
                if(detection.Automatic!=null) {
                    try {Registration.Register(root,detection.Automatic.Path,false);bool keepManual=setup.UserDataManual&&UserDataLocator.Same(setup.UserDataPath,detection.Automatic.Path);setup.UserDataPath=detection.Automatic.Path;setup.UserDataManual=keepManual;setup.InitialSetupComplete=true;setup.Save(root);}
                    catch(Exception e){error=e.Message;}
                } else error="保存先を自動で確定できません。「導入・登録設定」で選択してください。";
            } else error="HOI4本体を確認できません。「導入・登録設定」を開いてください。";
            registrationStatus.Text="登録："+(error??Registration.Status(root,setup.UserDataPath));
            if(initial&&!setup.InitialSetupComplete&&error!=null)Register();
        }
    }
}
