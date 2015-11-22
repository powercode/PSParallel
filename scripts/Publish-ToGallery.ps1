$p = @{
    Name = "PSParallel"
    NuGetApiKey = $NuGetApiKey
    LicenseUri = "https://github.com/powercode/PSParallel/blob/master/LICENSE"
    Tag = "Parallel","Runspace","Invoke","Foreach"
    ReleaseNote = "Limiting throttlelimit to 63"
    ProjectUri = "https://github.com/powercode/PSParallel"
}

Publish-Module @p
