$p = @{
    Name = "PSParallel"
    NuGetApiKey = $NuGetApiKey
    LicenseUri = "https://github.com/powercode/PSParallel/blob/master/LICENSE"
	IconUil = "https://github.com/powercode/PSParallel/blob/master/images/PSParallel_icon.png"
    Tag = "Parallel","Runspace","Invoke","Foreach"
    ReleaseNote = "Adding authenticode signature."
    ProjectUri = "https://github.com/powercode/PSParallel"
}

Publish-Module @p
