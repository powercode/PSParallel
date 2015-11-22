# PSParallel
Invoke scriptblocks in parallel runspaces

##Installation
```PowerShell
Install-Module PSParallel
```

```PowerShell
# ping all machines in a subnet
1..256 | Invoke-Parallel {
    $ip = "192.168.0.$_" 
    $res = ping.exe -4 -a -w 20 $ip
    [PSCustomObject] @{IP=$ip;Result=$res}
  }
```

Variables are captured from the parent session but functions are not.

##Throttling
To control the degree of parallelism, i.e. the number of concurrent runspaces, use the -ThrottleLimit parameter

```PowerShell
# process lots of crash dumps
Get-ChildItem -recurce *.dmp | Invoke-Parallel -ThrottleLimit 32 -ProgressActivity "Processing dumps" {
   [PSCustomObject] @{ Dump=$_; Analysis = cdb.exe -z $_.fullname -c '"!analyze -v;q"'
  }
```

The overhead of spinning up new PowerShell classes is non-zero. Invoke-Parallel is useful when you have items with high latency or that is long running.


##Contributions
Pull requests and/or suggestions are more than welcome.

###Acknowlegementes
The idea and the basis for the implementation comes from [RamblingCookieMonster](https://github.com/RamblingCookieMonster).
