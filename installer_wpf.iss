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
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), 'প্রয়োজনীয় কম্পোনেন্ট ডাউনলোড করা হচ্ছে...', nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if CurPageID = wpReady then
  begin
    if not IsDotNet8DesktopInstalled() then
    begin
      if MsgBox('প্রিন্ট স্টুডিও চালানোর জন্য এই কম্পিউটারে মাইক্রোসফট .NET 8 Desktop Runtime প্রয়োজন।' + #13#10#13#10 +
                'ইনস্টলার কি এটি সরাসরি মাইক্রোসফট থেকে ডাউনলোড ও ইনস্টল করবে?', mbConfirmation, MB_YESNO) = IDYES then
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
              SuppressibleMsgBox(AddPeriod(GetExceptionMessage), mbCriticalError, MB_OK, IDOK);
            Result := False;
          end;
        finally
          DownloadPage.Hide;
        end;

        if Result then
        begin
          if Exec(ExpandConstant('{tmp}\dotnet8_desktop_runtime.exe'), '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ErrorCode) then
          begin
            Result := (ErrorCode = 0) or (ErrorCode = 3010);
            if not Result then
            begin
              MsgBox('.NET 8 ইনস্টলেশনে ত্রুটি হয়েছে (Exit code: ' + IntToStr(ErrorCode) + ')।', mbError, MB_OK);
            end;
          end
          else
          begin
            MsgBox('.NET 8 ইনস্টলার রান করা যায়নি।', mbError, MB_OK);
            Result := False;
          end;
        end;
      end
      else
      begin
        MsgBox('.NET 8 ছাড়া প্রিন্ট স্টুডিও রান হতে পারবে না।' + #13#10 + 'আপনি চাইলে পরবর্তীতে মাইক্রোসফটের ওয়েবসাইট থেকে .NET 8 Desktop Runtime ইনস্টল করে নিতে পারবেন।', mbInformation, MB_OK);
        Result := False;
      end;
    end;
  end;
end;
