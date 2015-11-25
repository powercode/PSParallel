param([string]$InstallDirectory)

$rootDir = Split-Path (Split-Path $MyInvocation.MyCommand.Path)

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

$cert = Get-Item Cert:\CurrentUser\My\98D6087848D1213F20149ADFE698473429A9B15D
Get-ChildItem -File $InstallDirectory -Include *.dll,*.psd1 | Set-AuthenticodeSignature -Certificate $cert
