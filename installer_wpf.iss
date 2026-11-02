#define MyAppName "Print Studio"
#define MyAppVersion "2.0"
#define MyAppPublisher "Md Hasan Al Mamun"
#define MyAppURL "https://github.com/hasanjucse29/print-studio"
#define MyAppExeName "PrintStudio.exe"

[Setup]
AppId={{9F78A1D3-7845-42E1-9876-1234567890AB}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\PrintStudio
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=dist
OutputBaseFilename=PrintStudio_Setup_v2.0
SetupIconFile=app_icon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "C:\Users\Md Hasan Al Mamun\AppData\Local\PrintStudioWpf\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\PrintStudio.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "app_icon.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app_icon.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app_icon.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DownloadPage: TDownloadWizardPage;

function HasDotNet8InDir(const BaseDir: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if DirExists(BaseDir) then
  begin
    if FindFirst(BaseDir + '\8.*', FindRec) then
    begin
      try
        Result := True;
      finally
        FindClose(FindRec);
      end;
    end;
  end;
end;

function IsDotNet8DesktopInstalled(): Boolean;
begin
  Result := HasDotNet8InDir(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App'))
         or HasDotNet8InDir('C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App')
         or HasDotNet8InDir(ExpandConstant('{localappdata}\dotnet\shared\Microsoft.WindowsDesktop.App'));
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage('প্রয়োজনীয় সিস্টেম ফাইল ডাউনলোড', 'প্রয়োজনীয় কম্পোনেন্ট স্বয়ংক্রিয়ভাবে ডাউনলোড ও কনফিগার করা হচ্ছে...', nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if CurPageID = wpReady then
  begin
    // If .NET 8 Desktop Runtime is not found, download and install silently with zero prompts
    if not IsDotNet8DesktopInstalled() then
    begin
      DownloadPage.Clear;
      DownloadPage.Add('https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe', 'dotnet8_desktop_runtime.exe', '');
      DownloadPage.Show;
      try
        try
          DownloadPage.Download;
          Result := True;
        except
          if DownloadPage.AbortedByUser then
            Log('User aborted download')
          else
            SuppressibleMsgBox('ইন্টারনেট সংযোগ পরীক্ষা করুন। প্রয়োজনীয় ফাইল ডাউনলোড করা যায়নি।', mbCriticalError, MB_OK, IDOK);
          Result := False;
        end;
      finally
        DownloadPage.Hide;
      end;

      if Result then
      begin
        // Silently install in background with no windows or popups
        if Exec(ExpandConstant('{tmp}\dotnet8_desktop_runtime.exe'), '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode) then
        begin
          Result := (ErrorCode = 0) or (ErrorCode = 3010);
        end
        else
        begin
          Result := False;
        end;
      end;
    end;
  end;
end;
