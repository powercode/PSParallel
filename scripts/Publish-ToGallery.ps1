$p = @{
    Name = "PSParallel"
    NuGetApiKey = $NuGetApiKey
    LicenseUri = "https://github.com/powercode/PSParallel/blob/master/LICENSE"
    Tag = "Parallel","Runspace","Invoke","Foreach"
    ReleaseNote = "Initial take on Invoke-Parallel, a cmdlet to run script blocks in parallel"
    ProjectUri = "https://github.com/powercode/PSParallel"
}

Publish-Module @p
