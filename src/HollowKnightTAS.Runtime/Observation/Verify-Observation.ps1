param(
    [string]$RuntimePath = (Join-Path $PSScriptRoot '..\bin\Release\HollowKnightTAS.dll'),
    [string]$ManagedPath = 'D:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight_Data\Managed'
)
$ErrorActionPreference = 'Stop'
# This script loads managed definitions in PowerShell only. It never launches Unity or creates
# any UnityEngine.Object. The tested curve builders use pure Vector3/math operations.
foreach ($name in @('UnityEngine.CoreModule','UnityEngine.Physics2DModule','PlayMaker','Assembly-CSharp','Mono.Cecil')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $ManagedPath ($name + '.dll')))
}
$runtimeFullPath = (Resolve-Path -LiteralPath $RuntimePath).Path
$runtime = [Reflection.Assembly]::LoadFrom($runtimeFullPath)
$instanceFlags = [Reflection.BindingFlags]'Instance,NonPublic,Public'
$staticFlags = [Reflection.BindingFlags]'Static,NonPublic,Public'
$checks = [Collections.Generic.List[string]]::new()
function Assert-Observation([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "FAIL: $Name" }
    $checks.Add($Name)
}
$valueType = $runtime.GetType('HollowKnightTAS.Runtime.Observation.ObservationValues', $true)
$reference = [Func[UnityEngine.Object,object]] { param($value) throw 'Test unexpectedly reached a Unity object reference.' }
$encoder = [Activator]::CreateInstance($valueType, $instanceFlags, $null, [object[]]@($reference), $null)
$encode = $valueType.GetMethods($instanceFlags) | Where-Object { $_.Name -eq 'Encode' -and $_.GetParameters().Count -eq 1 }
$captureFields = $valueType.GetMethod('CaptureFields', $instanceFlags)
Add-Type -TypeDefinition @'
using System;
using System.Collections;
public class ObservationReadTrap {
    private int hiddenTimer = 37;
    public int Visible = 9;
    public static int GetterReads;
    public int UnsafeGetter { get { GetterReads++; throw new InvalidOperationException("getter called"); } }
    public override string ToString() { throw new InvalidOperationException("ToString called"); }
    public int Touch() { return hiddenTimer; }
}
public class ObservationListTrap : IList {
    public int Count { get { throw new InvalidOperationException("custom Count called"); } }
    public object this[int i] { get { throw new Exception(); } set { throw new Exception(); } }
    public bool IsReadOnly { get { return true; } } public bool IsFixedSize { get { return true; } }
    public bool IsSynchronized { get { return false; } } public object SyncRoot { get { return this; } }
    public int Add(object v) { throw new Exception(); } public void Clear() { throw new Exception(); }
    public bool Contains(object v) { throw new Exception(); } public int IndexOf(object v) { throw new Exception(); }
    public void Insert(int i, object v) { throw new Exception(); } public void Remove(object v) { throw new Exception(); }
    public void RemoveAt(int i) { throw new Exception(); } public void CopyTo(Array a, int i) { throw new Exception(); }
    public IEnumerator GetEnumerator() { throw new Exception(); }
}
'@
Assert-Observation ($encode.Invoke($encoder,[object[]]@(123)) -eq 123) 'primitive value'
$trap = [ObservationReadTrap]::new()
$data = $captureFields.Invoke($encoder,[object[]]@($trap,$null))
$hidden = $data['fields'] | Where-Object { $_['name'] -eq 'hiddenTimer' }
Assert-Observation ($hidden['value'] -eq 37 -and [ObservationReadTrap]::GetterReads -eq 0) 'private field without getter or ToString'
$unsupported = $encode.Invoke($encoder,[object[]]@($trap))
Assert-Observation ($unsupported['reason'] -eq 'unsupportedObjectGraph') 'arbitrary object graph omitted'
$unsupported = $encode.Invoke($encoder,[object[]]@(,[ObservationListTrap]::new()))
Assert-Observation ($unsupported['reason'] -eq 'unsupportedObjectGraph') 'custom IList never evaluated'
$text = $encode.Invoke($encoder,[object[]]@(('x' * 17000)))
Assert-Observation ($text['originalLength'] -eq 17000 -and $text['omittedCount'] -eq 616) 'string truncation is explicit'
$array = $encode.Invoke($encoder,[object[]]@(,[int[]](1..600)))
Assert-Observation ($array['items'].Count -eq 512 -and $array['omittedCount'] -eq 88) 'array bound and omission count'
$cycle = [Collections.Generic.List[object]]::new(); $cycle.Add($cycle)
$cycleData = $encode.Invoke($encoder,[object[]]@(,$cycle))
for ($depth = 0; $depth -lt 4; $depth++) { $cycleData = $cycleData['items'][0] }
Assert-Observation ($cycleData['reason'] -eq 'collectionDepthLimit') 'cyclic list bounded'
$enumData = $encode.Invoke($encoder,[object[]]@([DayOfWeek]::Friday))
Assert-Observation ($enumData['name'] -eq 'Friday' -and $enumData['numeric'] -eq 5) 'enum has named and numeric values'
$fsmFloat = [HutongGames.PlayMaker.FsmFloat]::new(2.5)
$fsmValue = $encode.Invoke($encoder,[object[]]@($fsmFloat))
Assert-Observation ($fsmValue['raw']['value'] -eq 2.5 -and $fsmValue['valueSource'] -eq 'existingBackingFields') 'FSM wrapper uses raw field'

$shapeType = $runtime.GetType('HollowKnightTAS.Runtime.Observation.ObservationColliders', $true)
$circle = $shapeType.GetMethod('Circle', $staticFlags)
$capsule = $shapeType.GetMethod('Capsule', $staticFlags)
$curve = $circle.Invoke($null,[object[]]@([UnityEngine.Vector3]::new(3,4,0),[float]2))
$points = $curve.GetType().GetField('Points',$instanceFlags).GetValue($curve)
Assert-Observation ($points.Length -eq 64) 'circle curve resolution'
$maxError = ($points | ForEach-Object { [Math]::Abs([Math]::Sqrt(($_.x - 3) * ($_.x - 3) + ($_.y - 4) * ($_.y - 4)) - 2) } | Measure-Object -Maximum).Maximum
Assert-Observation ($maxError -lt 0.00001) 'translated circle radius'
$curve = $capsule.Invoke($null,[object[]]@([UnityEngine.Vector3]::new(0,-2,0),[UnityEngine.Vector3]::new(0,2,0),[float]1))
$points = $curve.GetType().GetField('Points',$instanceFlags).GetValue($curve)
$xRange = $points.x | Measure-Object -Minimum -Maximum
$yRange = $points.y | Measure-Object -Minimum -Maximum
Assert-Observation ([Math]::Abs($xRange.Minimum + 1) -lt 0.00001 -and [Math]::Abs($xRange.Maximum - 1) -lt 0.00001 -and [Math]::Abs($yRange.Minimum + 3) -lt 0.00001 -and [Math]::Abs($yRange.Maximum - 3) -lt 0.00001) 'vertical capsule extents'
$curve = $capsule.Invoke($null,[object[]]@([UnityEngine.Vector3]::new(-2,0,0),[UnityEngine.Vector3]::new(2,0,0),[float]1))
$points = $curve.GetType().GetField('Points',$instanceFlags).GetValue($curve)
$xRange = $points.x | Measure-Object -Minimum -Maximum
$yRange = $points.y | Measure-Object -Minimum -Maximum
Assert-Observation ([Math]::Abs($xRange.Minimum + 3) -lt 0.00001 -and [Math]::Abs($xRange.Maximum - 3) -lt 0.00001 -and [Math]::Abs($yRange.Minimum + 1) -lt 0.00001 -and [Math]::Abs($yRange.Maximum - 1) -lt 0.00001) 'horizontal capsule extents'

# Check actual installed metadata rather than relying on SDK assumptions.
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $ManagedPath 'Assembly-CSharp.dll'))
try {
    foreach ($typeName in @('HeroController','GameManager','UIManager','GameCameras','PlayerData')) {
        $type = $game.MainModule.Types | Where-Object FullName -eq $typeName
        Assert-Observation (@($type.Fields | Where-Object { $_.Name -eq '_instance' -and $_.IsStatic }).Count -eq 1) "existing singleton field $typeName"
    }
} finally { $game.Dispose() }
$playmaker = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $ManagedPath 'PlayMaker.dll'))
try {
    foreach ($pair in @(@('PlayMakerFSM','fsm'),@('HutongGames.PlayMaker.Fsm','variables'),@('HutongGames.PlayMaker.Fsm','activeState'),@('HutongGames.PlayMaker.Fsm','activeStateName'),@('HutongGames.PlayMaker.Fsm','states'),@('HutongGames.PlayMaker.Fsm','globalTransitions'),@('HutongGames.PlayMaker.FsmState','actions'),@('HutongGames.PlayMaker.FsmState','transitions'))) {
        $type = $playmaker.MainModule.Types | Where-Object FullName -eq $pair[0]
        Assert-Observation (@($type.Fields | Where-Object Name -eq $pair[1]).Count -eq 1) ("FSM backing field " + $pair[0] + '.' + $pair[1])
    }
} finally { $playmaker.Dispose() }
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($runtimeFullPath)
try {
    $types = [Collections.Generic.List[object]]::new()
    function Add-ObservationType($Type) { $types.Add($Type); foreach ($nested in $Type.NestedTypes) { Add-ObservationType $nested } }
    foreach ($type in $assembly.MainModule.Types | Where-Object Namespace -eq 'HollowKnightTAS.Runtime.Observation') { Add-ObservationType $type }
    $forbidden = [Collections.Generic.List[string]]::new()
    foreach ($type in $types) {
        foreach ($method in $type.Methods | Where-Object HasBody) {
            foreach ($instruction in $method.Body.Instructions) {
                if ($instruction.Operand -isnot [Mono.Cecil.MethodReference]) { continue }
                $target = $instruction.Operand
                if (($target.DeclaringType.FullName -like 'UnityEngine.*' -and ($target.Name -match '^(set_|CreateMesh$|Instantiate$|Destroy|AddComponent$|Simulate$|SyncTransforms$)')) -or
                    ($target.DeclaringType.FullName -eq 'PlayMakerFSM' -and $target.Name -eq 'get_Fsm') -or
                    ($target.DeclaringType.FullName -eq 'HutongGames.PlayMaker.FsmState' -and $target.Name -eq 'get_Actions') -or
                    ($target.DeclaringType.FullName -eq 'System.Reflection.FieldInfo' -and $target.Name -eq 'SetValue')) { $forbidden.Add($target.FullName) }
            }
        }
    }
    Assert-Observation ($forbidden.Count -eq 0) ('compiled observer has no known mutating calls: ' + ($forbidden -join ', '))
} finally { $assembly.Dispose() }
[pscustomobject]@{ passed = $checks.Count; checks = $checks; scope = 'managed/offline only; no game process or native collider/viewport execution' } | ConvertTo-Json -Depth 5
