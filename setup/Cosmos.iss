; Cosmos OS Development Kit - Gen3 Installer
; Inno Setup Script for Windows
; Requires Inno Setup 6.x+

#define MyAppName "Cosmos OS Development Kit"
#define MyAppVersion GetEnv('COSMOS_VERSION')
#if MyAppVersion == ""
  #define MyAppVersion "3.0.38"
#endif
#define MyAppPublisher "Cosmos Project"
#define MyAppURL "https://github.com/CosmosOS/nativeaot-patcher"

[Setup]
AppId={{E5B3A550-47DB-4E3C-B714-C6D01F1E9F3C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
DefaultDirName={localappdata}\Cosmos
DefaultGroupName=Cosmos
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=CosmosSetup-{#MyAppVersion}-windows
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Uncomment these when image files are available:
; WizardImageFile=images\cosmos.bmp
; WizardSmallImageFile=images\cosmos_small.bmp
; SetupIconFile=images\cosmos.ico
; UninstallDisplayIcon={app}\cosmos.ico
LicenseFile=..\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; NuGet packages
Source: "bundle\packages\*.nupkg"; DestDir: "{app}\Packages"; Flags: ignoreversion

; Cross-compiler toolchains
Source: "bundle\tools\windows\x86_64-elf-tools\*"; DestDir: "{app}\Tools\x86_64-elf-tools"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "bundle\tools\windows\aarch64-elf-tools\*"; DestDir: "{app}\Tools\aarch64-elf-tools"; Flags: ignoreversion recursesubdirs createallsubdirs

; Build tools
Source: "bundle\tools\windows\yasm\*"; DestDir: "{app}\Tools\yasm"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "bundle\tools\windows\xorriso\*"; DestDir: "{app}\Tools\xorriso"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "bundle\tools\windows\lld\*"; DestDir: "{app}\Tools\lld"; Flags: ignoreversion recursesubdirs createallsubdirs

; VS Code extension
Source: "bundle\extensions\*.vsix"; DestDir: "{app}\Extensions"; Flags: ignoreversion skipifsourcedoesntexist

; dotnet tool packages
Source: "bundle\dotnet-tools\*.nupkg"; DestDir: "{app}\DotnetTools"; Flags: ignoreversion

[Registry]
Root: HKCU; Subkey: "Software\Cosmos"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey

[Run]
; Register local NuGet feed for offline package restore
StatusMsg: "Registering Cosmos NuGet feed..."; \
  Filename: "dotnet"; \
  Parameters: "nuget add source ""{app}\Packages"" --name ""Cosmos Local Feed"""; \
  Flags: runhidden waituntilterminated; \
  Check: DotNetInstalled

; Install Cosmos.Patcher global tool
StatusMsg: "Installing Cosmos Patcher..."; \
  Filename: "dotnet"; \
  Parameters: "tool install -g Cosmos.Patcher --add-source ""{app}\DotnetTools"""; \
  Flags: runhidden waituntilterminated; \
  Check: DotNetInstalled

; Install Cosmos.Tools global tool
StatusMsg: "Installing Cosmos Tools CLI..."; \
  Filename: "dotnet"; \
  Parameters: "tool install -g Cosmos.Tools --add-source ""{app}\DotnetTools"""; \
  Flags: runhidden waituntilterminated; \
  Check: DotNetInstalled

; Install project templates
StatusMsg: "Installing Cosmos project templates..."; \
  Filename: "dotnet"; \
  Parameters: "new install Cosmos.Build.Templates --add-source ""{app}\DotnetTools"""; \
  Flags: runhidden waituntilterminated; \
  Check: DotNetInstalled

; Install VS Code extension (optional, skip if VS Code not installed)
StatusMsg: "Installing VS Code extension..."; \
  Filename: "code"; \
  Parameters: "--install-extension ""{app}\Extensions\cosmos-vscode.vsix"" --force"; \
  Flags: runhidden waituntilterminated skipifdoesntexist; \
  Check: VSCodeInstalled

[UninstallRun]
Filename: "dotnet"; Parameters: "nuget remove source ""Cosmos Local Feed"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveNuGetFeed"
Filename: "dotnet"; Parameters: "tool uninstall -g Cosmos.Patcher"; Flags: runhidden waituntilterminated; RunOnceId: "UninstallPatcher"
Filename: "dotnet"; Parameters: "tool uninstall -g Cosmos.Tools"; Flags: runhidden waituntilterminated; RunOnceId: "UninstallTools"
Filename: "dotnet"; Parameters: "new uninstall Cosmos.Build.Templates"; Flags: runhidden waituntilterminated; RunOnceId: "UninstallTemplates"

[Code]
function DotNetInstalled: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec('dotnet', '--version', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function VSCodeInstalled: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec('code', '--version', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

procedure AddToUserPath(Dir: string);
var
  CurrentPath: string;
begin
  if RegQueryStringValue(HKCU, 'Environment', 'Path', CurrentPath) then
  begin
    if Pos(Uppercase(Dir), Uppercase(CurrentPath)) = 0 then
    begin
      if CurrentPath <> '' then
        CurrentPath := CurrentPath + ';';
      CurrentPath := CurrentPath + Dir;
      RegWriteStringValue(HKCU, 'Environment', 'Path', CurrentPath);
    end;
  end
  else
    RegWriteStringValue(HKCU, 'Environment', 'Path', Dir);
end;

procedure RemoveFromUserPath(Dir: string);
var
  CurrentPath, UpperDir, UpperPath: string;
  P: Integer;
begin
  if RegQueryStringValue(HKCU, 'Environment', 'Path', CurrentPath) then
  begin
    UpperDir := Uppercase(Dir);
    UpperPath := Uppercase(CurrentPath);
    P := Pos(UpperDir, UpperPath);
    if P > 0 then
    begin
      { Remove the directory and any trailing semicolon }
      Delete(CurrentPath, P, Length(Dir));
      if (P <= Length(CurrentPath)) and (CurrentPath[P] = ';') then
        Delete(CurrentPath, P, 1)
      else if (P > 1) and (CurrentPath[P - 1] = ';') then
        Delete(CurrentPath, P - 1, 1);
      RegWriteStringValue(HKCU, 'Environment', 'Path', CurrentPath);
    end;
  end;
end;

function InitializeSetup: Boolean;
begin
  if not DotNetInstalled then
  begin
    if MsgBox('.NET SDK is required but was not found.' + #13#10 + #13#10 +
              'Please install .NET 10.0 SDK from https://dot.net/download' + #13#10 + #13#10 +
              'Continue anyway?', mbConfirmation, MB_YESNO) = IDNO then
    begin
      Result := False;
      Exit;
    end;
  end;
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    { Add tool directories to user PATH }
    AddToUserPath(ExpandConstant('{app}\Tools\yasm'));
    AddToUserPath(ExpandConstant('{app}\Tools\xorriso'));
    AddToUserPath(ExpandConstant('{app}\Tools\lld'));
    AddToUserPath(ExpandConstant('{app}\Tools\x86_64-elf-tools\bin'));
    AddToUserPath(ExpandConstant('{app}\Tools\aarch64-elf-tools\bin'));
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { Remove tool directories from user PATH }
    RemoveFromUserPath(ExpandConstant('{app}\Tools\yasm'));
    RemoveFromUserPath(ExpandConstant('{app}\Tools\xorriso'));
    RemoveFromUserPath(ExpandConstant('{app}\Tools\lld'));
    RemoveFromUserPath(ExpandConstant('{app}\Tools\x86_64-elf-tools\bin'));
    RemoveFromUserPath(ExpandConstant('{app}\Tools\aarch64-elf-tools\bin'));
  end;
end;
