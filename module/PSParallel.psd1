@{

# Script module or binary module file associated with this manifest.
RootModule = '.\PSParallel.dll'

# Version number of this module.
ModuleVersion = '1.5'

# ID used to uniquely identify this module
GUID = '79e69e01-f25c-4745-9a57-846bfe194855'

# Author of this module
Author = 'PowerCode'

# Copyright statement for this module
Copyright = '(c) 2015 PowerCode. All rights reserved.'

# Description of the functionality provided by this module
Description = 'Provides Invoke-Parallel <scriptblock> that runs the scriptblock in parallel in separate runspaces' 

# Minimum version of the Windows PowerShell engine required by this module
PowerShellVersion = '3.0'

# Minimum version of Microsoft .NET Framework required by this module
DotNetFrameworkVersion = '4.5'

# Cmdlets to export from this module
CmdletsToExport = 'Invoke-Parallel'

# Aliases to export from this module
AliasesToExport = 'ipa'

# List of all files packaged with this module
FileList = @('.\PSParallel.psd1', '.\PSParallel.dll')

}

