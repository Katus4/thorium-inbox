#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PayloadDir
  #define PayloadDir "..\artifacts\app"
#endif
#ifndef InstallerName
  #define InstallerName "ThoriumInbox-Setup-x64-Unsigned-Test"
#endif
#define ThoriumUrl "https://github.com/edrlab/thorium-reader/releases/download/v3.5.1/Thorium-3.5.1-x64.exe"
#define ThoriumSHA256 "ad33003c0648b8f8f91cb9368b457701f23b1ed9443b01c94934f756f7ed98fd"

[Setup]
AppId={{0BFB458D-10CB-44DF-83E8-2D95A3B9DEB5}
AppName=Thorium Inbox
AppVersion={#AppVersion}
DefaultDirName={autopf}\Thorium Inbox
DefaultGroupName=Thorium Inbox
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
AppMutex=Local\ThoriumInbox-64FA94BA
UninstallDisplayIcon={app}\ThoriumInbox.exe
SetupIconFile=..\src\ThoriumInbox\Assets\inbox.ico
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
OutputBaseFilename={#InstallerName}
WizardStyle=modern
#ifdef RequireSigning
SignTool=thoriumSign
SignedUninstaller=yes
#endif

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{code:WatchedDirectory}"; Flags: uninsneveruninstall
Name: "{userdocs}\Thorium Inbox\EPUBs"; Flags: uninsneveruninstall

[Icons]
Name: "{autoprograms}\Thorium Inbox"; Filename: "{app}\ThoriumInbox.exe"

[Registry]
Root: HKCU; Subkey: "Software\Thorium Inbox"; ValueType: string; ValueName: "InboxFolder"; ValueData: "{code:WatchedDirectory}"
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Thorium Inbox"; ValueData: """{app}\ThoriumInbox.exe"" --background"; Flags: uninsdeletevalue; Check: ShouldStartAtLogin

[Run]
Filename: "{app}\ThoriumInbox.exe"; Description: "Open Thorium Inbox"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
var
  InboxPage: TInputDirWizardPage;

function ShouldStartAtLogin: Boolean;
var
  Preference: AnsiString;
begin
  Result := True;
  if LoadStringFromFile(ExpandConstant('{localappdata}\Thorium Inbox\login-startup.txt'), Preference) then
    Result := Preference <> 'disabled';
end;

procedure InitializeWizard;
var
  SavedInbox: String;
begin
  InboxPage := CreateInputDirPage(wpSelectDir, 'Watched folder',
    'Pick the folder that will receive PDFs and EPUBs.',
    'Save or move the files you want in Thorium into this folder.',
    False, '');
  InboxPage.Add('Watched folder');
  if not RegQueryStringValue(HKCU, 'Software\Thorium Inbox', 'InboxFolder', SavedInbox) then
    SavedInbox := ExpandConstant('{userdocs}\Thorium Inbox\Inbox');
  InboxPage.Values[0] := SavedInbox;
end;

function WatchedDirectory(Param: String): String;
begin
  Result := RemoveBackslashUnlessRoot(InboxPage.Values[0]);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = InboxPage.ID then
  begin
    if CompareText(WatchedDirectory(''), ExpandConstant('{userdocs}\Thorium Inbox\EPUBs')) = 0 then
    begin
      MsgBox('Choose a folder separate from the EPUB output folder.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function RegistryHasThorium(RootKey: Integer): Boolean;
var
  UninstallKey, Location, DisplayName, AppPath: String;
  Subkeys: TArrayOfString;
  Index: Integer;
begin
  Result := False;
  if RegQueryStringValue(RootKey, 'Software\Microsoft\Windows\CurrentVersion\App Paths\Thorium.exe', '', AppPath) then
  begin
    Result := FileExists(RemoveQuotes(AppPath));
    if Result then Exit;
  end;
  UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall';
  if RegGetSubkeyNames(RootKey, UninstallKey, Subkeys) then
    for Index := 0 to GetArrayLength(Subkeys) - 1 do
      if RegQueryStringValue(RootKey, UninstallKey + '\' + Subkeys[Index], 'DisplayName', DisplayName) then
        if (CompareText(DisplayName, 'Thorium') = 0) or (CompareText(DisplayName, 'Thorium Reader') = 0) then
          if RegQueryStringValue(RootKey, UninstallKey + '\' + Subkeys[Index], 'InstallLocation', Location) then
          begin
            Result := FileExists(AddBackslash(Location) + 'Thorium.exe');
            if Result then Exit;
          end;
end;

function HasThorium: Boolean;
var
  Code: Integer;
begin
  Result := FileExists(ExpandConstant('{userpf}\Thorium\Thorium.exe')) or
    FileExists(ExpandConstant('{commonpf64}\Thorium\Thorium.exe')) or
    FileExists(ExpandConstant('{commonpf32}\Thorium\Thorium.exe')) or
    RegistryHasThorium(HKCU) or RegistryHasThorium(HKLM);
  if Result then Exit;
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -Command "if (Get-StartApps | Where-Object { $_.AppID -like ''EDRLab.ThoriumReader_*!*'' } | Select-Object -First 1) { exit 0 } else { exit 1 }"',
    '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
  Downloaded: String;
  Page: TDownloadWizardPage;
begin
  Result := '';
  if HasThorium then Exit;

  Page := CreateDownloadPage('Install Thorium Reader',
    'Setup is downloading and checking the Thorium Reader installer.', nil);
  try
    Page.Add('{#ThoriumUrl}', 'Thorium-3.5.1-x64.exe', '{#ThoriumSHA256}');
    Page.Show;
    try
      Page.Download;
    finally
      Page.Hide;
    end;

    Downloaded := ExpandConstant('{tmp}\Thorium-3.5.1-x64.exe');
    if not Exec(Downloaded, '/S /currentuser', '', SW_HIDE, ewWaitUntilTerminated, Code) then
      Result := 'Could not start the Thorium Reader installer.'
    else if Code <> 0 then
      Result := 'The Thorium Reader installer reported an error. Code ' + IntToStr(Code) + '.'
    else if not HasThorium then
      Result := 'Setup could not find Thorium Reader after installation.';
  except
    Result := 'Could not install Thorium Reader. ' + GetExceptionMessage;
  end;
end;
