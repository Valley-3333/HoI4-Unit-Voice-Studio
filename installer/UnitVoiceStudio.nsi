Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

Name "HOI4 Unit Voice Studio"
OutFile "..\dist\UnitVoiceStudio-Setup-1.1.1.exe"
InstallDir "$LOCALAPPDATA\Programs\UnitVoiceStudio"
InstallDirRegKey HKCU "Software\UnitVoiceStudio" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
VIProductVersion "1.1.1.0"
VIAddVersionKey "ProductName" "HOI4 Unit Voice Studio"
VIAddVersionKey "FileDescription" "ユニット音声レコーダー セットアップ"
VIAddVersionKey "FileVersion" "1.1.1"
VIAddVersionKey "ProductVersion" "1.1.1"
VIAddVersionKey "LegalCopyright" "Valley"
VIAddVersionKey "CompanyName" "Valley"
BrandingText "Unit Voice Studio"
Var TestMode

!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TEXT "HOI4のユニット音声を録音・差し替えするアプリをインストールします。$\r$\n$\r$\n初回起動時にHOI4本体と保存先を検出し、ランチャー用のMod登録ファイルを配置します。$\r$\n$\r$\n更新でも、すでに保存した録音とModは保持されます。"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\HOI4VoiceStudio.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Unit Voice Studioを起動し、Modの登録状態を確認する"
!define MUI_FINISHPAGE_TEXT "アプリをインストールしました。$\r$\n$\r$\n起動後にModが登録されたら、HOI4ランチャーで Unit Voice Studio をプレイセットへ追加してください。"
!insertmacro MUI_PAGE_FINISH
!define MUI_UNCONFIRMPAGE_TEXT_TOP "アプリと、この設置場所を参照するMod登録を削除します。$\r$\n録音履歴（data）とMod本体（UnitVoiceMod）は削除しません。"
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Japanese"

Function .onInit
  StrCpy $TestMode 0
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/TESTMODE" $1
  ${IfNot} ${Errors}
    StrCpy $TestMode 1
  ${EndIf}
  ${If} $TestMode != 1
    FindWindow $0 "" "HOI4 Unit Voice Studio — ユニット音声レコーダー"
    ${If} $0 != 0
      MessageBox MB_OK|MB_ICONINFORMATION "録音を保存し、起動中のUnit Voice Studioを閉じてからセットアップを実行してください。" /SD IDOK
      SetErrorLevel 3
      Abort
    ${EndIf}
  ${EndIf}
FunctionEnd

Function .onVerifyInstDir
  IfFileExists "$INSTDIR\hoi4.exe" 0 done
    MessageBox MB_OK|MB_ICONEXCLAMATION "HOI4本体のフォルダーにはインストールできません。初期設定の保存先を使用してください。" /SD IDOK
    Abort
  done:
FunctionEnd

Section "アプリ"
  ReadRegDWORD $0 HKLM "SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" "Release"
  ${If} $0 < 528040
    MessageBox MB_OK|MB_ICONEXCLAMATION ".NET Framework 4.8以上が必要です。Windowsを更新してから、もう一度実行してください。" /SD IDOK
    SetErrorLevel 4
    Abort
  ${EndIf}
  SetShellVarContext current
  SetOutPath "$INSTDIR"
  SetOverwrite on
  File "..\dist\payload\HOI4VoiceStudio.exe"
  File "..\dist\payload\使い方.txt"
  File "..\dist\payload\配布案内.txt"
  File "..\dist\payload\THIRD_PARTY_NOTICES.txt"
  File "..\dist\payload\installed.flag"
  SetOutPath "$INSTDIR\licenses"
  File "..\dist\payload\licenses\NSIS-COPYING.txt"
  SetOutPath "$INSTDIR"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  ${If} $TestMode == 1
    FileOpen $0 "$INSTDIR\installer-testmode" w
    FileClose $0
  ${Else}
    WriteRegStr HKCU "Software\UnitVoiceStudio" "InstallDir" "$INSTDIR"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\UnitVoiceStudio" "DisplayName" "HOI4 Unit Voice Studio"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\UnitVoiceStudio" "DisplayVersion" "1.1.1"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\UnitVoiceStudio" "Publisher" "Valley"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\UnitVoiceStudio" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\UnitVoiceStudio" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\UnitVoiceStudio" "DisplayIcon" "$INSTDIR\HOI4VoiceStudio.exe"
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\UnitVoiceStudio" "NoModify" 1
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\UnitVoiceStudio" "NoRepair" 1
    CreateDirectory "$SMPROGRAMS\Unit Voice Studio"
    CreateShortCut "$SMPROGRAMS\Unit Voice Studio\Unit Voice Studio.lnk" "$INSTDIR\HOI4VoiceStudio.exe"
    CreateShortCut "$SMPROGRAMS\Unit Voice Studio\アンインストール.lnk" "$INSTDIR\Uninstall.exe"
  ${EndIf}
SectionEnd

Function un.onInit
  IfFileExists "$INSTDIR\installer-testmode" done
  FindWindow $0 "" "HOI4 Unit Voice Studio — ユニット音声レコーダー"
  ${If} $0 != 0
    MessageBox MB_OK|MB_ICONINFORMATION "録音を保存し、Unit Voice Studioを閉じてからアンインストールしてください。" /SD IDOK
    SetErrorLevel 3
    Abort
  ${EndIf}
  done:
FunctionEnd

Section "Uninstall"
  SetShellVarContext current
  IfFileExists "$INSTDIR\HOI4VoiceStudio.exe" 0 unregister_done
    ExecWait '$\"$INSTDIR\HOI4VoiceStudio.exe$\" --unregister' $0
    ${If} $0 != 0
      MessageBox MB_OK|MB_ICONEXCLAMATION "Mod登録を解除できませんでした。登録を保護するためアンインストールを中止します。アプリの導入設定で保存先を確認してください。" /SD IDOK
      SetErrorLevel 5
      Abort
    ${EndIf}
  unregister_done:
  ReadRegStr $0 HKCU "Software\UnitVoiceStudio" "InstallDir"
  ${If} $0 == $INSTDIR
    DeleteRegKey HKCU "Software\UnitVoiceStudio"
    DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\UnitVoiceStudio"
    Delete "$SMPROGRAMS\Unit Voice Studio\Unit Voice Studio.lnk"
    Delete "$SMPROGRAMS\Unit Voice Studio\アンインストール.lnk"
    RMDir "$SMPROGRAMS\Unit Voice Studio"
  ${EndIf}
  Delete "$INSTDIR\HOI4VoiceStudio.exe"
  Delete "$INSTDIR\使い方.txt"
  Delete "$INSTDIR\配布案内.txt"
  Delete "$INSTDIR\THIRD_PARTY_NOTICES.txt"
  Delete "$INSTDIR\licenses\NSIS-COPYING.txt"
  RMDir "$INSTDIR\licenses"
  Delete "$INSTDIR\installed.flag"
  Delete "$INSTDIR\installer-testmode"
  Delete "$INSTDIR\Uninstall.exe"
  ; No recursive removal: recordings, configuration and generated Mod files remain.
  RMDir "$INSTDIR"
SectionEnd
