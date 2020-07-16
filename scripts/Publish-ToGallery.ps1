$manPath = Get-ChildItem -recurse $PSScriptRoot/../module -include *.psd1 | Select-Object -first 1
$man = Test-ModuleManifest $manPath

$name = $man.Name
[string]$version = $man.Version

$p = @{
    Name = $name
    NuGetApiKey = $NuGetApiKey
    RequiredVersion = $version
}

Publish-Module @p
