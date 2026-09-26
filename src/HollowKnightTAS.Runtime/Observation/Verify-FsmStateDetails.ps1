#requires -PSEdition Desktop
# Use powershell.exe: the synthetic action derives from the game's .NET Framework PlayMaker assembly.
param(
    [string]$RuntimePath = (Join-Path $PSScriptRoot '..\bin\Release\HollowKnightTAS.dll'),
    [string]$ManagedPath = 'D:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight_Data\Managed',
    [string]$HeroDetailsPath = ''
)
$ErrorActionPreference = 'Stop'
# Standalone managed values only: never launches Unity, creates a UnityEngine.Object,
# or invokes the mutation-prone FsmState.Actions / PlayMakerFSM.Fsm getters.
foreach ($name in @('UnityEngine.CoreModule','PlayMaker','Assembly-CSharp','Newtonsoft.Json')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $ManagedPath ($name + '.dll')))
}
$runtime = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $RuntimePath).Path)
$flags = [Reflection.BindingFlags]'Instance,NonPublic,Public'
$checks = [Collections.Generic.List[string]]::new()
function Assert-Details([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "FAIL: $Name" }
    $checks.Add($Name)
}
Add-Type -ReferencedAssemblies @((Join-Path $ManagedPath 'PlayMaker.dll'), (Join-Path $ManagedPath 'UnityEngine.CoreModule.dll')) -TypeDefinition @'
using System;
using HutongGames.PlayMaker;
public sealed class ObservationFutureAction : FsmStateAction {
    public FsmFloat speed;
    private float elapsedTime;
    public static int GetterReads;
    public float UnsafeGetter { get { GetterReads++; throw new InvalidOperationException("getter called"); } }
    public float Touch() { return elapsedTime; }
    public void SetForTest(float value) { elapsedTime = value; }
}
'@
$reference = [Func[UnityEngine.Object,object]] { param($value) throw 'Unexpected Unity object reference in standalone test.' }
$valueType = $runtime.GetType('HollowKnightTAS.Runtime.Observation.ObservationValues', $true)
$values = [Activator]::CreateInstance($valueType, $flags, $null, [object[]]@($reference), $null)
$observerType = $runtime.GetType('HollowKnightTAS.Runtime.Observation.ObservationFsm', $true)
$observer = [Activator]::CreateInstance($observerType, $flags, $null, [object[]]@($values), $null)
$captureState = $observerType.GetMethod('State', $flags)
$action = [Runtime.Serialization.FormatterServices]::GetUninitializedObject([ObservationFutureAction])
$action.SetForTest([float]3.25)
$speed = [HutongGames.PlayMaker.FsmFloat]::new()
[HutongGames.PlayMaker.FsmFloat].GetField('value', $flags).SetValue($speed, [float]12.5)
[ObservationFutureAction].GetField('speed', $flags).SetValue($action, $speed)
[HutongGames.PlayMaker.FsmStateAction].GetField('enabled', $flags).SetValue($action, $true)
$state = [Runtime.Serialization.FormatterServices]::GetUninitializedObject([HutongGames.PlayMaker.FsmState])
[HutongGames.PlayMaker.FsmState].GetField('name', $flags).SetValue($state, 'Future Attack')
$actions = [HutongGames.PlayMaker.FsmStateAction[]]@($action)
$actionsField = [HutongGames.PlayMaker.FsmState].GetField('actions', $flags)
$actionsField.SetValue($state, $actions)
$result = $captureState.Invoke($observer, [object[]]@($state))
Assert-Details ($result['name'] -eq 'Future Attack' -and $result['actionsLoaded']) 'non-current loaded state is inspectable'
$captured = $result['actions'][0]
Assert-Details ($captured['type'] -eq 'ObservationFutureAction' -and $captured['index'] -eq 0 -and $captured['enabled']) 'action identity and enabled field'
$fields = $captured['data']['fields']
$elapsed = $fields | Where-Object { $_['name'] -eq 'elapsedTime' }
$speedField = $fields | Where-Object { $_['name'] -eq 'speed' }
Assert-Details ($elapsed['value'] -eq 3.25) 'private elapsed timer is retained'
Assert-Details ($speedField['value']['raw']['value'] -eq 12.5) 'future attack configured speed is retained'
Assert-Details ([ObservationFutureAction]::GetterReads -eq 0 -and [Object]::ReferenceEquals($actionsField.GetValue($state), $actions)) 'no arbitrary getter or action-array replacement'
$unloaded = [Runtime.Serialization.FormatterServices]::GetUninitializedObject([HutongGames.PlayMaker.FsmState])
$result = $captureState.Invoke($observer, [object[]]@($unloaded))
Assert-Details (-not $result['actionsLoaded'] -and $result['actions']['omitted'] -and $null -eq $actionsField.GetValue($unloaded)) 'unloaded state is explicitly omitted without loading'
if ($HeroDetailsPath) {
    $details = [Newtonsoft.Json.Linq.JObject]::Parse([IO.File]::ReadAllText((Resolve-Path -LiteralPath $HeroDetailsPath).Path))
    $hero = $details['object']
    $json = $hero.ToString([Newtonsoft.Json.Formatting]::None)
    $cache = [HollowKnightTAS.Runtime.FullRun.WorldObservationCache]::new()
    $objects = [Collections.Generic.List[Tuple[string,string,string]]]::new()
    $objects.Add([Tuple]::Create([string]$hero['id'], [string]$hero['kind'], [string]$json))
    $id = $cache.AddSnapshot(1972,1500,'{}',$objects,'world')
    $page = $cache.ReadSnapshot($id,0,1)
    $reduced = [Newtonsoft.Json.Linq.JObject]::Parse($page['snapshotJson'])['objects'][0]
    Assert-Details ([bool]$reduced['detailsRequired'] -and [string]$reduced['name'] -eq 'Knight') 'live hero produces a named core summary'
    Assert-Details ([Newtonsoft.Json.Linq.JToken]::DeepEquals($hero['transform'], $reduced['transform']) -and [Newtonsoft.Json.Linq.JToken]::DeepEquals($hero['hero']['resources'], $reduced['hero']['resources'])) 'live hero transform and resources retained exactly'
    Assert-Details ($reduced.ToString([Newtonsoft.Json.Formatting]::None).Length -le 80000) 'live core summary respects character budget'
    Write-Output ('Live hero overview: {0} -> {1} characters' -f $json.Length, $reduced.ToString([Newtonsoft.Json.Formatting]::None).Length)
}
Write-Output ('PASS: {0}/{0} checks' -f $checks.Count)
$checks | ForEach-Object { Write-Output ('  ' + $_) }
