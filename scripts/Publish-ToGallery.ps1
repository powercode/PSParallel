$p = @{
    Name = "PSParallel"
    NuGetApiKey = $NuGetApiKey
}

Publish-Module @p
