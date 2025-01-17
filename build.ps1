param (
    [Parameter(Mandatory=$true)][string]$mode,
    [string]$keyId,
    [string]$keySecret,
    [string]$bucket = "clowd-releases",
    [string]$channel = "experimental",
    [switch]$noDelta = $false,
    [switch]$skipSquirrel = $false,
    [switch]$skipObs = $false
)

function Global:Get-ByteHash ($file) {
    $hasher = [System.Security.Cryptography.MD5]::Create()
    $inputStream = New-Object System.IO.StreamReader ($file)
    $hashBytes = $hasher.ComputeHash($inputStream.BaseStream)
    $inputStream.Close()
    return [System.Convert]::ToBase64String($hashBytes)
}


# Basic Setup
$PSVersionTable.PSVersion
Set-Location $PSScriptRoot
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue" # progress bar in powershell is slow af
$LocalProjectMode = Test-Path -Path "$PSScriptRoot\.usingproj" -PathType Leaf


# locate s3 credentials
if (Test-Path '.\clowd_secrets.json') {
    $secrets = Get-Content '.\clowd_secrets.json' | Out-String | ConvertFrom-Json
    $keyId = $secrets.keyId;
    $keySecret = $secrets.keySecret;
} elseif (Test-Path '..\clowd_secrets.json') {
    $secrets = Get-Content '..\clowd_secrets.json' | Out-String | ConvertFrom-Json
    $keyId = $secrets.keyId;
    $keySecret = $secrets.keySecret;
}

if (($noDelta -eq $false) -And ([string]::IsNullOrEmpty($keyId) -Or [string]::IsNullOrEmpty($keySecret))) {
    throw "Unable to find clowd_secrets.json, cannot create release / upload. Specify -noDelta option if you intend to skip this step."
}


# Get msbuild location
$MSBuildPath = (&"${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe) | Out-String
$MSBuildPath = $MSBuildPath.Trim();


# calculate Clowd version
Write-Host "Clowd Version (nbgv)" -ForegroundColor Magenta
$gitVerJson = (&nbgv get-version -f json) | ConvertFrom-Json
$version = $gitVerJson.SimpleVersion
Write-Host $version


if ($mode -eq "compile") {

    # Ensure a clean state by removing build/package folders
    $Folders = @("$PSScriptRoot\publish", "$PSScriptRoot\bin", "$PSScriptRoot\releases")
    foreach ($Folder in $Folders) {
        if (Test-Path $Folder) {
            Remove-Item -path "$Folder" -Recurse -Force
        }
    }


    # publish clowd main project
    Write-Host "Build Clowd.csproj" -ForegroundColor Magenta
    dotnet publish "$PSScriptRoot\src\Clowd\Clowd.csproj" -c Release -r win-x64 --no-self-contained -o "$PSScriptRoot\publish"


    # build clowd native and copy to publish directory
    Write-Host "Build Clowd.WinNative.vcxproj" -ForegroundColor Magenta
    &$MSBuildPath "$PSScriptRoot\src\Clowd.WinNative\Clowd.WinNative.vcxproj" `
    /v:minimal `
    /p:Configuration=Release `
    /p:Platform=x64 `
    /p:SolutionDir=$PSScriptRoot\
    Copy-Item ".\bin\Release\Clowd.WinNative.dll" -Destination "$PSScriptRoot\publish"


    # build obs-express only if we are in local mode, otherwise download
    $HasObsBuildFolder = Test-Path -Path "$PSScriptRoot\..\obs-express"
    if ($LocalProjectMode -And $HasObsBuildFolder -And ($skipObs -eq $false)) {
        Write-Host "Build obs-express (local mode)" -ForegroundColor Magenta
        Set-Location "$PSScriptRoot\..\obs-express"
        &npm install
        &npm run build
        Copy-Item "bin" -Destination "$PSScriptRoot\publish\obs-express" -Recurse
    } else {

        $obsUrl = "https://github.com/clowd/obs-express/releases/latest/download/obs-express.zip"
        $obsLocalPath = "$PSScriptRoot/.cache/obs-express.zip"

        md "$PSScriptRoot/.cache" -Force

        if (Test-Path $obsLocalPath) {
            Write-Host "obs-express exists in cache; checking..." -ForegroundColor Magenta
            $obsRemoteMd5 = (Invoke-WebRequest $obsUrl -Method Head -UseBasicParsing).Headers.'content-md5'
            $obsLocalMd5 = Get-ByteHash $obsLocalPath

            if ($obsLocalMd5 -eq $obsRemoteMd5) {
                Write-Host "obs-express is up to date." -ForegroundColor Magenta
            } else {
                Write-Host "obs-express is no longer valid, deleting cache..." -ForegroundColor Magenta
                Remove-Item $obsLocalPath
            }
        }

        if (-Not (Test-Path $obsLocalPath)) {
            Write-Host "Downloading obs-express from GitHub" -ForegroundColor Magenta
            Invoke-WebRequest $obsUrl -OutFile $obsLocalPath
        }

        Write-Host "Extracting obs-express archive to build dir" -ForegroundColor Magenta
        Expand-Archive $obsLocalPath -DestinationPath "$PSScriptRoot\publish\obs-express"
    }
    Set-Location $PSScriptRoot

}


# build packaging tools only if in local mode
if ($LocalProjectMode) {
    Write-Host "Build Squirrel (local mode)" -ForegroundColor Magenta
    if ($skipSquirrel -eq $false) {
        Set-Location "$PSScriptRoot\..\Clowd.Squirrel"
        & ".\build.cmd"
    }
    Set-Alias Squirrel ("$PSScriptRoot\..\Clowd.Squirrel\build\publish\Squirrel.exe");
} else {
    $projdeps = dotnet list "$PSScriptRoot\src\Clowd\Clowd.csproj" package
    $projstring = $projdeps -join "`r`n"
    $squirrelVersion = $projstring -match "(?m)Clowd\.Squirrel.*\s(\d{1,3}\.\d{1,3}\.\d.*?)$"
    $squirrelVersion = $Matches[1].Trim()
    Write-Host "Using Squirrel '$squirrelVersion' from local NuGet cache" -ForegroundColor Magenta
    Set-Alias Squirrel ($env:USERPROFILE + "\.nuget\packages\clowd.squirrel\$squirrelVersion\tools\Squirrel.exe");
}
Set-Location $PSScriptRoot

if ($mode -eq "compile" -Or $mode -eq "pack") {

    if ($noDelta -eq $false) {
        # download recent packages
        New-Item -ItemType "directory" -Path "$PSScriptRoot\releases"
        Write-Host "Download latest release" -ForegroundColor Magenta
        Squirrel s3-down `
        -r "$PSScriptRoot\releases" `
        --bucket $bucket `
        --keyId $keyId `
        --secret $keySecret `
        --endpoint "https://s3.eu-central-003.backblazeb2.com" `
        --pathPrefix $channel
    }


    # releasify
    Write-Host "Create Nuget & Releasify Package" -ForegroundColor Magenta
    Squirrel pack `
    -f net6 `
    -r "$PSScriptRoot\releases" `
    -i="$PSScriptRoot\artwork\default-setup.ico" `
    --appIcon="$PSScriptRoot\artwork\default.ico" `
    --splashImage="$PSScriptRoot\artwork\splash.gif" `
    --packId=Clowd `
    --packVersion=$version `
    --packAuthors="Caelan Sayler" `
    --packDirectory="$PSScriptRoot\publish"

    Write-Host "Done. Run .\upload.cmd to release" -ForegroundColor Magenta

}

if ($mode -eq "upload") {

    # upload local releases to s3/b2
    Write-Host "Upload latest releases" -ForegroundColor Magenta
    Squirrel s3-up `
    -r "$PSScriptRoot\releases" `
    --bucket $bucket `
    --keyId $keyId `
    --secret $keySecret `
    --endpoint "https://s3.eu-central-003.backblazeb2.com" `
    --pathPrefix $channel `
    --keepMaxReleases 20

}