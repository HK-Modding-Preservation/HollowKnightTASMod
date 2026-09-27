#requires -PSEdition Desktop
param(
    [string]$RuntimePath = (Join-Path $PSScriptRoot '..\bin\Release\HollowKnightTAS.dll'),
    [string]$ManagedPath = 'D:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight_Data\Managed'
)
$ErrorActionPreference = 'Stop'
foreach ($name in @('UnityEngine.CoreModule','PlayMaker','Assembly-CSharp','Newtonsoft.Json')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $ManagedPath ($name + '.dll')))
}
$runtime = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $RuntimePath).Path)
$flags = [Reflection.BindingFlags]'Instance,NonPublic,Public'
function New-Raw([type]$Type) { [Runtime.Serialization.FormatterServices]::GetUninitializedObject($Type) }
function Set-Raw($Object, [string]$Name, $Value) { $Object.GetType().GetField($Name, $flags).SetValue($Object, $Value) }
function Assert-Graph([bool]$Value, [string]$Name) { if (-not $Value) { throw "FAIL $Name" }; Write-Output "PASS $Name" }
$observerType = $runtime.GetType('HollowKnightTAS.Runtime.Observation.RuntimeWorldObserver', $true)
$observer = [Activator]::CreateInstance($observerType)
$graphMethod = $observerType.GetMethod('Graph', $flags)
$fsm = New-Raw ([HutongGames.PlayMaker.Fsm])
$idle = New-Raw ([HutongGames.PlayMaker.FsmState]); Set-Raw $idle 'name' 'Idle'
$attack = New-Raw ([HutongGames.PlayMaker.FsmState]); Set-Raw $attack 'name' 'Attack'
$event = New-Raw ([HutongGames.PlayMaker.FsmEvent]); Set-Raw $event 'name' 'ATTACK'
$edge = New-Raw ([HutongGames.PlayMaker.FsmTransition]); Set-Raw $edge 'fsmEvent' $event; Set-Raw $edge 'toState' 'Attack'
Set-Raw $idle 'transitions' ([HutongGames.PlayMaker.FsmTransition[]]@($edge))
Set-Raw $fsm 'states' ([HutongGames.PlayMaker.FsmState[]]@($idle,$attack)); Set-Raw $fsm 'startState' 'Idle'
Set-Raw $fsm 'globalTransitions' ([HutongGames.PlayMaker.FsmTransition[]]@($edge))
$graph = $graphMethod.Invoke($observer, [object[]]@($fsm))
Assert-Graph ($graph['states'].Count -eq 2 -and $graph['startState'] -eq 'Idle') 'all states and start'
Assert-Graph ($graph['states'][0]['transitions'][0]['event'] -eq 'ATTACK' -and $graph['globalTransitions'][0]['toState'] -eq 'Attack') 'local and global edges'
Assert-Graph (-not $graph['states'][0]['actionsLoaded'] -and $null -eq $idle.GetType().GetField('actions',$flags).GetValue($idle)) 'no action initialization'
$before = [Newtonsoft.Json.JsonConvert]::SerializeObject($graph)
Set-Raw $fsm 'activeStateName' 'Attack'
$after = [Newtonsoft.Json.JsonConvert]::SerializeObject($graphMethod.Invoke($observer, [object[]]@($fsm)))
Assert-Graph ($before -eq $after) 'state change preserves topology version input'
Set-Raw $edge 'toState' 'Idle'
$changed = [Newtonsoft.Json.JsonConvert]::SerializeObject($graphMethod.Invoke($observer, [object[]]@($fsm)))
Assert-Graph ($before -ne $changed) 'runtime topology edits invalidate cached graph'
Set-Raw $fsm 'states' ([HutongGames.PlayMaker.FsmState[]]::new(513))
$rejected = $false
try { [void]$graphMethod.Invoke($observer, [object[]]@($fsm)) } catch { $rejected = $_.Exception.ToString().Contains('512') }
Assert-Graph $rejected 'oversized graph explicitly rejected'
