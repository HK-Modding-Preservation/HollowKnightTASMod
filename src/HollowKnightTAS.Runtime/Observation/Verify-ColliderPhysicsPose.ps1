param(
    [string]$RuntimePath = (Join-Path $PSScriptRoot '..\bin\Release\HollowKnightTAS.dll'),
    [string]$ManagedPath = 'D:\SteamLibrary\steamapps\common\Hollow Knight\hollow_knight_Data\Managed'
)
$ErrorActionPreference = 'Stop'
# Tests only pure Vector2/Vector3 arithmetic methods. No UnityEngine.Object is created,
# no game process is contacted, and no transform or physics state is written.
foreach ($name in @('UnityEngine.CoreModule','UnityEngine.Physics2DModule')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $ManagedPath ($name + '.dll')))
}
$runtime = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $RuntimePath).Path)
$shape = $runtime.GetType('HollowKnightTAS.Runtime.Observation.ObservationColliders', $true)
$flags = [Reflection.BindingFlags]'Static,NonPublic'
$point = $shape.GetMethod('ApplyPhysicsPoint', $flags)
$direction = $shape.GetMethod('ApplyPhysicsDirection', $flags)
$checks = [Collections.Generic.List[string]]::new()
function Assert-Vector($Actual, [double]$X, [double]$Y, [double]$Z, [string]$Name) {
    if ([Math]::Abs($Actual.x - $X) -gt 0.00001 -or [Math]::Abs($Actual.y - $Y) -gt 0.00001 -or [Math]::Abs($Actual.z - $Z) -gt 0.00001) {
        throw ('FAIL: {0}: got ({1}, {2}, {3}), expected ({4}, {5}, {6})' -f $Name,$Actual.x,$Actual.y,$Actual.z,$X,$Y,$Z)
    }
    $checks.Add($Name)
}
function Invoke-Point([double]$X,[double]$Y,[double]$Z,[bool]$HasBody,[double]$TX,[double]$TY,[double]$TZ,[double]$BX,[double]$BY,[double]$Delta) {
    return $point.Invoke($null,[object[]]@([UnityEngine.Vector3]::new($X,$Y,$Z),$HasBody,
        [UnityEngine.Vector3]::new($TX,$TY,$TZ),[UnityEngine.Vector2]::new($BX,$BY),[float]$Delta))
}
$result = Invoke-Point 12 23 7 $true 10 20 5 13 16 0
Assert-Vector $result 15 19 7 'physics translation preserves relative offset and world z'
$result = Invoke-Point 12 23 7 $true 10 20 5 10 20 90
Assert-Vector $result 7 22 7 'rotation is around body origin rather than collider center or world origin'

# TransformPoint output for body display pose (10,20,5), angle30; child local
# position(3,4,2), angle90, scale(2,3,1); point(1,2)+collider offset(.5,-1).
# Before body rotation the combined local vector is (0,7,2). Physics angle90
# and position(100,200) must produce (93,200,7), retaining hierarchy and scale.
$result = Invoke-Point 6.5 26.0621778264911 7 $true 10 20 5 100 200 60
Assert-Vector $result 93 200 7 'child rotation nonuniform scale and collider offset are preserved'
# Mirrored child: body display(10,20,2), angle90; child position(3,-2,4),
# scale(2,-3,1), offset(.5,-1), body physics(-4,8), angle180.
$result = Invoke-Point 15 26 6 $true 10 20 2 -4 8 90
Assert-Vector $result -10 13 6 'mirrored scaled child first point retains its full relative transform'
$result = Invoke-Point 3 22 6 $true 10 20 2 -4 8 90
Assert-Vector $result -6 1 6 'mirrored scaled child opposite point preserves contour extent'
$result = Invoke-Point 12 23 7 $true 10 20 5 10 20 0
Assert-Vector $result 12 23 7 'identical display and physics poses are unchanged'
$result = Invoke-Point 12 23 7 $false 999 -100 -7 -333 300 123
Assert-Vector $result 12 23 7 'no attached rigidbody leaves static transform points unchanged'
$result = $direction.Invoke($null,[object[]]@([UnityEngine.Vector3]::new(0.866025403784,0.5,4),$true,[float]60))
Assert-Vector $result 0 1 4 'capsule axis receives rotation without translation and retains z'
$result = $direction.Invoke($null,[object[]]@([UnityEngine.Vector3]::new(2,3,4),$false,[float]90))
Assert-Vector $result 2 3 4 'no attached rigidbody leaves static direction unchanged'
$result = Invoke-Point 1 0 0 $true 0 0 0 0 0 -350
Assert-Vector $result 0.984807753012 0.173648177667 0 'wrapped Unity Euler difference gives the correct rotation'

# Numeric regression from the live observe-v1 Knight: interpolated Transform y
# differs from Rigidbody y; its core box center must now agree with live bounds.
$result = Invoke-Point 38.2739944 29.8653774 0.004 $true 38.2739944 30.6153774 0.004 38.2739944 30.5888329 0
Assert-Vector $result 38.2739944 29.8388329 0.004 'live interpolated Knight core contour agrees with physics bounds center'
Write-Output ('PASS: {0}/{0} checks' -f $checks.Count)
$checks | ForEach-Object { Write-Output ('  ' + $_) }
