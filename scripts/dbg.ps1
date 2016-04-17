param(
	[int] $ThrottleLimit = 3,
	[int] $Milliseconds = 500
)

Import-Module PSParallel -RequiredVersion 2.2.1

function new-philosopher {
	param($name, [string[]] $treats)
	[PSCustomObject] @{		
		Name = $name
		Treats = $treats
	}
}


$philosopherData = @(
	    new-philosopher 'Immanuel Kant' 'was a real pissant','who where very rarely stable'
	    new-philosopher 'Heidegger' 'was a boozy beggar', 'Who could think you under the table'
	    new-philosopher 'David Hume' 'could out-consume Schopenhauer and Hegel'	
	    new-philosopher 'Wittgenstein' 'was a beery swine', 'Who was just as sloshed as Schlegel'
	    new-philosopher 'John Stuart Mill' 'of his own free will', 'On half a pint of shandy was particularly ill'
	    new-philosopher 'Nietzsche' 'There''s nothing Nietzsche couldn''t teach ya', 'Bout the raising of the wrist.'	
	    new-philosopher 'Plato' 'they say, could stick it away', 'Half a crate of whiskey every day'
	    new-philosopher 'Aristotle' 'was a bugger for the bottle'
	    new-philosopher 'Hobbes' 'was fond of his dram'
	    new-philosopher 'Rene Descartes' 'was a drunken fart:', 'I drink, therefore I am'
 	    new-philosopher 'Socrates' 'is particularly missed','A lovely little thinker but a bugger when he''s pissed!'
    )


1..100 | invoke-parallel -Throttle $ThrottleLimit  {



	$pd = $philosopherData[($_ -1)% $philosopherData.Count]
	
	1..100 | foreach {
		$op = switch($_ % 8)
		{
			0 { 'sleeping' }
            1 { 'drinking' }
            2 { 'drinking' }
			3 { 'thinking' }
            4 { 'drinking' }
            5 { 'drinking' }
			6 { 'eating' }
            7 { 'drinking' }
		}

        $status = $pd.Treats[$_ % $pd.Treats.Length]		

        $name = $pd.Name
        $currentOperation = "$name is currently $op"
		Write-Progress -id $PSParallelProgressId -percent $_ -activity $pd.Name -Status $status  -CurrentOperation $currentOperation
		Start-Sleep -milliseconds ($Milliseconds + 100 * (Get-Random -Minimum 3 -Maximum 7))				
	}
}