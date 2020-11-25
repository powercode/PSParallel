$manPath = Get-ChildItem -recurse $PSScriptRoot/../module -include *.psd1 | Select-Object -first 1
$man = Test-ModuleManifest $manPath

$name = $man.Name
[string]$version = $man.Version
$moduleSourceDir = "$PSScriptRoot/$name"
$moduleDir = "~/documents/WindowsPowerShell/Modules/$name/$version/"

[string]$rootDir = Resolve-Path $PSSCriptRoot/..

$InstallDirectory = $moduleDir

if ('' -eq $InstallDirectory)
{
    $personalModules = Join-Path -Path ([Environment]::GetFolderPath('MyDocuments')) -ChildPath WindowsPowerShell\Modules\
    if (($env:PSModulePath -split ';') -notcontains $personalModules)
    {
        Write-Warning "$personalModules is not in `$env:PSModulePath"
    }

    if (!(Test-Path $personalModules))
    {
        Write-Error "$personalModules does not exist"
    }

    $InstallDirectory = Join-Path -Path $personalModules -ChildPath PSParallel
}

if(-not (Test-Path $InstallDirectory))
{
    $null = mkdir $InstallDirectory
}

@(
    'module\PSParallel.psd1'		    
	'src\PsParallel\bin\Release\PSParallel.dll'
).Foreach{Copy-Item "$rootdir\$_" -Destination $InstallDirectory }

$lang = @('en-us')

$lang.Foreach{
	$lang = $_
	$langDir = "$InstallDirectory\$lang"
	if(-not (Test-Path $langDir))
	{
		$null = MkDir $langDir
	}

	@(
		'PSParallel.dll-Help.xml'
		'about_PSParallel.Help.txt'
	).Foreach{Copy-Item "$rootDir\module\$lang\$_" -Destination $langDir}
}

Get-ChildItem -Recurse -Path $InstallDirectory

$cert =Get-ChildItem cert:\CurrentUser\My -CodeSigningCert
if($cert)
{
    Get-ChildItem -File $InstallDirectory -Include *.dll,*.psd1 -Recurse | Set-AuthenticodeSignature -Certificate $cert -TimestampServer http://timestamp.verisign.com/scripts/timstamp.dll
}
