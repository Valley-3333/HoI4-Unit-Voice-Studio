using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace UnitVoiceStudio {
    public sealed class SetupForm : Form {
        readonly string root;
        readonly SetupSettings settings;
        readonly Action<string,string,bool> apply;
        readonly TextBox game=new TextBox(),destination=new TextBox();
        readonly ComboBox candidates=new ComboBox();
        readonly Label result=new Label();
        bool manual,loading;
        public SetupForm(string root,SetupSettings settings,Action<string,string,bool> apply,Action<string> import) {
            this.root=root;this.settings=settings;this.apply=apply;
            Text="導入・ランチャー登録の設定";Font=new Font("Yu Gothic UI",10);AutoScaleMode=AutoScaleMode.Dpi;
            ClientSize=new Size(860,510);MinimumSize=new Size(820,540);StartPosition=FormStartPosition.CenterParent;
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=9};Controls.Add(layout);
            foreach(int height in new[]{56,26,42,26,42,36,66,52})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layout.Controls.Add(new Label{Text="Modの登録ファイルはアプリが配置します。\n登録後は、ランチャーで Unit Voice Studio をプレイセットに追加してください。",Dock=DockStyle.Fill});
            layout.Controls.Add(new Label{Text="HOI4本体の場所",Dock=DockStyle.Fill});
            layout.Controls.Add(PathRow(game,"本体を選択",delegate{
                using(var d=new FolderBrowserDialog{Description="hoi4.exe と sound のあるフォルダー",SelectedPath=game.Text})if(d.ShowDialog(this)==DialogResult.OK){if(!GameLocator.IsGame(d.SelectedPath))throw new InvalidDataException("hoi4.exe と sound のある本体フォルダーを選択してください。");game.Text=d.SelectedPath;Detect();}
            }));
            layout.Controls.Add(new Label{Text="HOI4の保存先（OneDriveなどへ移動したドキュメントも検出します）",Dock=DockStyle.Fill});
            candidates.Dock=DockStyle.Fill;candidates.DropDownStyle=ComboBoxStyle.DropDownList;candidates.SelectedIndexChanged+=delegate{if(!loading&&candidates.SelectedItem is UserDataCandidate){destination.Text=((UserDataCandidate)candidates.SelectedItem).Path;manual=true;ShowStatus();}};layout.Controls.Add(candidates);
            layout.Controls.Add(PathRow(destination,"保存先を選択",delegate{
                using(var d=new FolderBrowserDialog{Description="settings.txt や launcher-v2.sqlite のあるHOI4の保存先",SelectedPath=destination.Text})if(d.ShowDialog(this)==DialogResult.OK){if(!UserDataLocator.IsUserData(d.SelectedPath))throw new InvalidDataException("HOI4の保存先を確認できません。初めての場合はHOI4を一度起動してください。");destination.Text=d.SelectedPath;manual=true;ShowStatus();}
            }));
            result.Dock=DockStyle.Fill;result.AutoEllipsis=true;result.Padding=new Padding(0,8,0,0);layout.Controls.Add(result);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};
            Button(actions,"登録・修復",delegate{apply(game.Text,destination.Text,manual);ShowStatus();MessageBox.Show(this,"登録を確認しました。ランチャーを再起動し、Unit Voice Studio をプレイセットへ追加してください。","登録完了",MessageBoxButtons.OK,MessageBoxIcon.Information);DialogResult=DialogResult.OK;Close();},145);
            Button(actions,"再検出",Detect,115);
            Button(actions,"旧版の録音を引き継ぐ",delegate{if(!GameLocator.IsGame(game.Text))throw new InvalidDataException("先に本体を選択してください。");using(var d=new FolderBrowserDialog{Description="旧版のexeとdataフォルダーが入ったフォルダー"})if(d.ShowDialog(this)==DialogResult.OK){import(d.SelectedPath);result.Text="録音の履歴と割り当てを引き継ぎました。元のフォルダーも残しています。";}},220);
            Button(actions,"閉じる",Close,100);layout.Controls.Add(actions);
            layout.Controls.Add(new Label{Text="自動で判別できない場合だけ保存先を選択してください。登録ファイルを修復する際は、既存ファイルを日時付きでバックアップします。\n更新・アンインストールでも data（録音履歴）と UnitVoiceMod は保持します。",Dock=DockStyle.Fill,ForeColor=Color.DimGray});
            game.Text=GameLocator.Find(settings.GamePath)??"";manual=settings.UserDataManual;Detect();
        }
        Control PathRow(TextBox box,string label,Action click) {
            var row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,145));box.ReadOnly=true;box.Dock=DockStyle.Fill;row.Controls.Add(box);var b=new Button{Text=label,Dock=DockStyle.Fill,Margin=new Padding(8,0,0,0)};b.Click+=delegate{Run(click);};row.Controls.Add(b);return row;
        }
        void Button(FlowLayoutPanel panel,string text,Action click,int width){var b=new Button{Text=text,Width=width,Height=36};b.Click+=delegate{Run(click);};panel.Controls.Add(b);}
        void Run(Action action){try{UseWaitCursor=true;action();}catch(Exception e){result.Text=e.Message;MessageBox.Show(this,e.Message,"導入設定",MessageBoxButtons.OK,MessageBoxIcon.Warning);}finally{UseWaitCursor=false;}}
        void Detect(){
            var detection=UserDataLocator.Detect(game.Text,settings);loading=true;candidates.Items.Clear();foreach(var c in detection.Candidates)candidates.Items.Add(c);
            if(detection.Automatic!=null){candidates.SelectedItem=detection.Automatic;destination.Text=detection.Automatic.Path;manual=settings.UserDataManual&&UserDataLocator.Same(settings.UserDataPath,destination.Text);}
            else {candidates.SelectedIndex=-1;destination.Text="";manual=false;}
            loading=false;ShowStatus();
        }
        void ShowStatus(){
            result.Text=string.IsNullOrEmpty(destination.Text)?"保存先を自動で確定できませんでした。候補から選ぶか、「保存先を選択」を押してください。":Registration.Status(root,destination.Text)+"\n登録先："+Registration.Target(destination.Text);
        }
    }
}
