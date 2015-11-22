$p = @{
    Name = "PSParallel"
    NuGetApiKey = $NuGetApiKey
    LicenseUri = "https://github.com/powercode/PSParallel/blob/master/LICENSE"
    Tag = "Parallel","Runspace","Invoke","Foreach"
    ReleaseNote = "Improving concurrency implementation to allow 128 concurrent runspaces. Improving accuracy in progress calculations."
    ProjectUri = "https://github.com/powercode/PSParallel"
}

Publish-Module @p
