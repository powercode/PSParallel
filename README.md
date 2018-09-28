# PSParallel
Invoke scriptblocks in parallel runspaces

## Installation
```PowerShell
Install-Module PSParallel
```

```PowerShell
# ping all machines in a subnet
(1..255).Foreach{"192.168.0.$_"} | Invoke-Parallel { [PSCustomObject] @{IP=$_;Result=ping.exe -4 -a -w 20 $_}}
```

Variables and functions are captured from the parent session.

## Throttling
To control the degree of parallelism, i.e. the number of concurrent runspaces, use the -ThrottleLimit parameter

```PowerShell
# process lots of crash dumps
Get-ChildItem -recurce *.dmp | Invoke-Parallel -ThrottleLimit 64 -ProgressActivity "Processing dumps" {
   [PSCustomObject] @{ Dump=$_; Analysis = cdb.exe -z $_.fullname -c '"!analyze -v;q"'
  }
```

The overhead of spinning up new PowerShell classes is non-zero. Invoke-Parallel is useful when you have items with high latency or that is long running.

![image](https://github.com/powercode/PSParallel/raw/master/images/Invoke-Parallel.png)

## Contributions
Pull requests and/or suggestions are more than welcome.

### Acknowlegementes
The idea and the basis for the implementation comes from [RamblingCookieMonster](https://github.com/RamblingCookieMonster).
Cudos for that implementation also goes to Boe Prox(@proxb) and Sergei Vorobev(@xvorsx).
