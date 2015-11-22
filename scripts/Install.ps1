﻿param([string]$InstallDirectory)

$rootDir = Split-Path (Split-Path $MyInvocation.MyCommand.Path)

if ('' -eq $InstallDirectory)
{
    $personalModules = Join-Path -Path ([Environment]::GetFolderPath('MyDocuments')) -ChildPath WindowsPowerShell\Modules
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

if (!(Test-Path $InstallDirectory))
{
    $null = mkdir $InstallDirectory    
}


$moduleFileList = @(
    'PSParallel.psd1'	
	'en-US\PSParallel.dll-Help.xml'
	'en-US\about_PSParallel.Help.txt'
    
)
$binaryFileList = 'src\PsParallel\bin\Release\PSParallel.*'



$binaryFileList | foreach { Copy-Item "$rootDir\$_" -Destination $InstallDirectory }
$moduleFileList  | foreach {Copy-Item "$rootdir\module\$_" -Destination $InstallDirectory\$_ }

Get-ChildItem -Recurse -Path $InstallDirectory


    
    