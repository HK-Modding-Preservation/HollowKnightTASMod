Set-StrictMode -Version Latest

function Convert-AdaptiveCombatObservation {
    param(
        [Parameter(Mandatory)][string]$Json,
        [Parameter(Mandatory)][long]$ResponseMovieTick
    )
    $frame = $Json | ConvertFrom-Json -Depth 100
    if ($frame.schemaVersion -ne 1 -or $frame.movieTick -ne $ResponseMovieTick `
            -or $ResponseMovieTick -lt 0 -or $frame.stamp.sceneEpoch -lt 0) {
        throw 'Combat observation has an unsupported schema or inconsistent tick/scene identity.'
    }
    if (@($frame.failures).Count -ne 0) {
        throw 'Combat observation contains provider failures; no input decision was made.'
    }
    $values = [ordered]@{}
    foreach ($entry in $frame.entries) {
        if ($entry.fresh -isnot [bool] -or -not $entry.fresh `
                -or $entry.sampledAtMovieTick -ne $ResponseMovieTick `
                -or $entry.ageMovieTicks -ne 0 `
                -or [string]::IsNullOrWhiteSpace([string]$entry.key) `
                -or $values.Contains([string]$entry.key)) {
            throw 'Combat observation contains stale, duplicate, or invalid fields; no input decision was made.'
        }
        $values[[string]$entry.key] = [string]$entry.displayValue
    }
    foreach ($required in @('scene.name', 'hero.position.x', 'hero.position.y')) {
        if (-not $values.Contains($required)) { throw "Combat observation omitted $required." }
    }
    return [pscustomobject]@{
        frame = $frame
        values = $values
        movieTick = [long]$frame.movieTick
        sceneEpoch = [int]$frame.stamp.sceneEpoch
    }
}

function Get-FalseKnightStringValue {
    param(
        [Parameter(Mandatory)]$Values,
        [Parameter(Mandatory)][string]$Key,
        [string]$Default = ''
    )

    if ($Values.Contains($Key)) {
        return [string]$Values[$Key]
    }
    return $Default
}

function Get-FalseKnightBooleanValue {
    param(
        [Parameter(Mandatory)]$Values,
        [Parameter(Mandatory)][string]$Key,
        [bool]$Default = $false
    )

    $raw = Get-FalseKnightStringValue -Values $Values -Key $Key
    $parsed = $false
    if ([bool]::TryParse($raw, [ref]$parsed)) {
        return $parsed
    }
    return $Default
}

function Get-FalseKnightInt64Value {
    param(
        [Parameter(Mandatory)]$Values,
        [Parameter(Mandatory)][string]$Key,
        [long]$Default = 0
    )

    $raw = Get-FalseKnightStringValue -Values $Values -Key $Key
    $parsed = 0L
    if ([long]::TryParse(
            $raw,
            [Globalization.NumberStyles]::Integer,
            [Globalization.CultureInfo]::InvariantCulture,
            [ref]$parsed)) {
        return $parsed
    }
    return $Default
}

function Get-FalseKnightSingleValue {
    param(
        [Parameter(Mandatory)]$Values,
        [Parameter(Mandatory)][string]$Key,
        [single]$Default = 0
    )

    $raw = Get-FalseKnightStringValue -Values $Values -Key $Key
    $parsed = [single]0
    if ([single]::TryParse(
            $raw,
            [Globalization.NumberStyles]::Float,
            [Globalization.CultureInfo]::InvariantCulture,
            [ref]$parsed)) {
        return $parsed
    }
    return $Default
}

function Get-FalseKnightLiveHeroCollider {
    param([Parameter(Mandatory)][string]$Json)
    $ErrorActionPreference = 'Stop'
    $catalog = @($Json | ConvertFrom-Json -ErrorAction Stop)
    $selected = @($catalog | Where-Object { $_.primaryHeroCollider -eq $true })
    if ($selected.Count -ne 1) { throw 'Exactly one native primary Hero collider is required.' }
    $collider = $selected[0]
    if ($collider.boundsAvailable -ne $true -or $collider.enabled -ne $true `
            -or $collider.activeInHierarchy -ne $true -or $collider.isTrigger -ne $false) {
        throw 'The native primary Hero collider is unavailable.'
    }
    foreach ($field in @('centerX', 'centerY', 'extentX', 'extentY')) {
        $number = 0.0
        if ($null -eq $collider.PSObject.Properties[$field] -or
                -not [double]::TryParse([string]$collider.$field,
                    [Globalization.NumberStyles]::Float,
                    [Globalization.CultureInfo]::InvariantCulture, [ref]$number) -or
                [double]::IsNaN($number) -or [double]::IsInfinity($number) -or
                ($field -like 'extent*' -and $number -le 0)) {
            throw "Invalid native Hero collider field: $field"
        }
    }
    return $collider
}

function New-FalseKnightSnapshot {
    param(
        [Parameter(Mandatory)]$Values,
        [Parameter(Mandatory)][long]$MovieTick,
        [Parameter(Mandatory)][int]$SceneEpoch
    )

    $attackCooldown = Get-FalseKnightSingleValue $Values 'hero.attack.cooldownSeconds' -Default ([single]::NaN)
    if ([single]::IsNaN($attackCooldown) -or [single]::IsInfinity($attackCooldown)) {
        throw 'A finite live hero.attack.cooldownSeconds is required for attack decisions.'
    }
    $heroCollider = Get-FalseKnightLiveHeroCollider -Json (
        Get-FalseKnightStringValue $Values 'combat.hero.collidersJson')
    return [pscustomobject][ordered]@{
        HeroColliderCenterX = [single]$heroCollider.centerX
        HeroColliderCenterY = [single]$heroCollider.centerY
        HeroColliderExtentX = [single]$heroCollider.extentX
        HeroColliderExtentY = [single]$heroCollider.extentY
        MovieTick = $MovieTick
        RngSha256 = Get-FalseKnightStringValue $Values 'rng.state.sha256'
        SceneEpoch = $SceneEpoch
        Scene = Get-FalseKnightStringValue $Values 'scene.name'
        EncounterActive = Get-FalseKnightBooleanValue `
            $Values 'bossPractice.encounter.active'
        EncounterScene = Get-FalseKnightStringValue `
            $Values 'bossPractice.encounter.scene'
        EncounterLevel = [int](Get-FalseKnightInt64Value `
            $Values 'bossPractice.encounter.level' -Default -1)
        EncounterDifficulty = Get-FalseKnightStringValue `
            $Values 'bossPractice.encounter.difficulty'
        BossesAlive = [int](Get-FalseKnightInt64Value `
            $Values 'bossPractice.encounter.bossesAlive')
        BossAvailable = Get-FalseKnightBooleanValue `
            $Values 'combat.primaryBoss.available'
        BossObjectPath = Get-FalseKnightStringValue `
            $Values 'combat.primaryBoss.objectPath'
        BossHp = [int](Get-FalseKnightInt64Value `
            $Values 'combat.primaryBoss.hp')
        BossDead = Get-FalseKnightBooleanValue `
            $Values 'combat.primaryBoss.dead'
        BossState = Get-FalseKnightStringValue `
            $Values 'combat.primaryBoss.mainState'
        BossRecentEvent = Get-FalseKnightStringValue `
            $Values 'combat.primaryBoss.recentEvent'
        BossFacing = Get-FalseKnightStringValue `
            $Values 'combat.primaryBoss.facing'
        BossCollidersJson = Get-FalseKnightStringValue `
            $Values 'combat.primaryBoss.collidersJson' -Default '[]'
        BossHealthManagersJson = Get-FalseKnightStringValue `
            $Values 'combat.primaryBoss.healthManagersJson'
        HazardsJson = Get-FalseKnightStringValue `
            $Values 'combat.hazardsJson' -Default '[]'
        HeroDamageSequence = Get-FalseKnightInt64Value `
            $Values 'combat.heroDamage.sequence'
        HeroDamageSourcePath = Get-FalseKnightStringValue `
            $Values 'combat.heroDamage.sourcePath'
        HeroDamageSourceName = Get-FalseKnightStringValue `
            $Values 'combat.heroDamage.sourceName'
        HeroDamageSide = Get-FalseKnightStringValue `
            $Values 'combat.heroDamage.side'
        HeroDamageAmount = [int](Get-FalseKnightInt64Value `
            $Values 'combat.heroDamage.amount')
        HeroDamageHazardType = [int](Get-FalseKnightInt64Value `
            $Values 'combat.heroDamage.hazardType')
        BossX = Get-FalseKnightSingleValue `
            $Values 'combat.primaryBoss.position.x'
        BossY = Get-FalseKnightSingleValue `
            $Values 'combat.primaryBoss.position.y'
        BossVelocityX = Get-FalseKnightSingleValue `
            $Values 'combat.primaryBoss.velocity.x'
        BossVelocityY = Get-FalseKnightSingleValue `
            $Values 'combat.primaryBoss.velocity.y'
        BossColliderCenterX = Get-FalseKnightSingleValue `
            $Values 'combat.primaryBoss.collider.center.x'
        BossColliderCenterY = Get-FalseKnightSingleValue `
            $Values 'combat.primaryBoss.collider.center.y'
        BossColliderExtentX = Get-FalseKnightSingleValue `
            $Values 'combat.primaryBoss.collider.extents.x'
        BossColliderExtentY = Get-FalseKnightSingleValue `
            $Values 'combat.primaryBoss.collider.extents.y'
        DeltaX = Get-FalseKnightSingleValue `
            $Values 'combat.relative.delta.x'
        DeltaY = Get-FalseKnightSingleValue `
            $Values 'combat.relative.delta.y'
        Distance = Get-FalseKnightSingleValue `
            $Values 'combat.relative.distance'
        BossSide = Get-FalseKnightStringValue `
            $Values 'combat.relative.horizontalSide'
        ContactOverlap = Get-FalseKnightBooleanValue `
            $Values 'combat.contact.overlap'
        HeroX = Get-FalseKnightSingleValue $Values 'hero.position.x'
        HeroY = Get-FalseKnightSingleValue $Values 'hero.position.y'
        HeroVelocityX = Get-FalseKnightSingleValue $Values 'hero.velocity.x'
        HeroVelocityY = Get-FalseKnightSingleValue $Values 'hero.velocity.y'
        HeroActorState = Get-FalseKnightStringValue $Values 'hero.actorState'
        HeroAnimation = Get-FalseKnightStringValue `
            $Values 'hero.animation.clip'
        HeroOnGround = Get-FalseKnightBooleanValue `
            $Values 'hero.cState.onGround'
        HeroJumping = Get-FalseKnightBooleanValue `
            $Values 'hero.cState.jumping'
        HeroFalling = Get-FalseKnightBooleanValue `
            $Values 'hero.cState.falling'
        HeroDashing = Get-FalseKnightBooleanValue `
            $Values 'hero.cState.dashing'
        HeroShadowDashing = Get-FalseKnightBooleanValue `
            $Values 'hero.cState.shadowDashing'
        HeroShadowDashReady = Get-FalseKnightBooleanValue `
            $Values 'hero.ability.shadowDashReady'
        HeroShadowDashCooldown = Get-FalseKnightSingleValue `
            $Values 'hero.cooldown.shadowDashSeconds'
        HeroAttacking = Get-FalseKnightBooleanValue `
            $Values 'hero.cState.attacking'
        HeroAttackCooldown = $attackCooldown
        HeroAcceptingInput = Get-FalseKnightBooleanValue `
            $Values 'hero.control.acceptingInput'
        HeroHealth = [int](Get-FalseKnightInt64Value $Values 'player.health')
        HeroMaxHealth = [int](Get-FalseKnightInt64Value `
            $Values 'player.maxHealth')
        HeroSoul = [int](Get-FalseKnightInt64Value $Values 'player.mp')
        BossDeathObserved = Get-FalseKnightBooleanValue `
            $Values 'bossPractice.milestone.bossDeathObserved'
        BossesDeadObserved = Get-FalseKnightBooleanValue `
            $Values 'bossPractice.milestone.bossesDeadObserved'
        SceneCompleteObserved = Get-FalseKnightBooleanValue `
            $Values 'bossPractice.milestone.sceneCompleteObserved'
        TerminalScene = Get-FalseKnightStringValue `
            $Values 'bossPractice.milestone.terminalScene'
        TerminalLevel = [int](Get-FalseKnightInt64Value `
            $Values 'bossPractice.milestone.terminalLevel' -Default -1)
        TerminalDifficulty = Get-FalseKnightStringValue `
            $Values 'bossPractice.milestone.terminalDifficulty'
    }
}

function New-FalseKnightControllerState {
    return [pscustomobject][ordered]@{
        LastAttackTick = -1000L
        LastJumpTick = -1000L
        LastDashTick = -1000L
        LastHealTick = -1000L
        FocusStartHealth = -1
        LastBossHp = -1
        MinimumBossHp = [int]::MaxValue
        MinimumHeroHealth = [int]::MaxValue
        ArmorBreakCount = 0
        DamageEventCount = 0
        DecisionCount = 0
        PreconditionRetryCount = 0
    }
}

function Get-FalseKnightTowardDirection {
    param([Parameter(Mandatory)]$Snapshot)

    $colliderDeltaX = [double]$Snapshot.BossColliderCenterX `
        - [double]$Snapshot.HeroX
    if ([Math]::Abs($colliderDeltaX) -gt 0.05) {
        if ($colliderDeltaX -gt 0) {
            return 'right'
        }
        return 'left'
    }
    if ($Snapshot.BossSide -eq 'right') {
        return 'right'
    }
    if ($Snapshot.BossSide -eq 'left') {
        return 'left'
    }
    return '-'
}

function Get-FalseKnightAwayDirection {
    param([Parameter(Mandatory)]$Snapshot)

    $toward = Get-FalseKnightTowardDirection -Snapshot $Snapshot
    if ($toward -eq 'right') {
        return 'left'
    }
    if ($toward -eq 'left') {
        return 'right'
    }
    if ($Snapshot.HeroX -lt 29.5) {
        return 'left'
    }
    return 'right'
}

function Resolve-FalseKnightArenaDirection {
    param(
        [Parameter(Mandatory)][string]$Direction,
        [Parameter(Mandatory)][single]$HeroX
    )

    if ($HeroX -le 15.25) {
        return 'right'
    }
    if ($HeroX -ge 44.25) {
        return 'left'
    }
    return $Direction
}

function Get-FalseKnightCombatGeometry {
    param([Parameter(Mandatory)]$Snapshot)

    $heroCenterX = [double]$Snapshot.HeroColliderCenterX
    $heroCenterY = [double]$Snapshot.HeroColliderCenterY
    $heroExtentX = [double]$Snapshot.HeroColliderExtentX
    $heroExtentY = [double]$Snapshot.HeroColliderExtentY
    $bossExtentX = [double]$Snapshot.BossColliderExtentX
    $bossExtentY = [double]$Snapshot.BossColliderExtentY
    $centerDeltaX = [double]$Snapshot.BossColliderCenterX - $heroCenterX
    $centerDeltaY = [double]$Snapshot.BossColliderCenterY - $heroCenterY
    $edgeGapX = [Math]::Max(
        0.0,
        [Math]::Abs($centerDeltaX) - $bossExtentX - $heroExtentX)
    $edgeGapY = [Math]::Max(
        0.0,
        [Math]::Abs($centerDeltaY) - $bossExtentY - $heroExtentY)

    return [pscustomobject][ordered]@{
        HeroCenterX = [single]$heroCenterX
        HeroCenterY = [single]$heroCenterY
        CenterDeltaX = [single]$centerDeltaX
        CenterDeltaY = [single]$centerDeltaY
        EdgeGapX = [single]$edgeGapX
        EdgeGapY = [single]$edgeGapY
        HorizontalOverlap = $edgeGapX -le 0.05
        VerticalOverlap = $edgeGapY -le 0.05
    }
}

function Get-FalseKnightExposedTarget {
    param([Parameter(Mandatory)]$Snapshot)

    $ErrorActionPreference = 'Stop'
    $heads = @($Snapshot.BossCollidersJson | ConvertFrom-Json | Where-Object {
        $_.objectName -eq 'Head' -and $_.objectPath.StartsWith($Snapshot.BossObjectPath + '/')
    })
    if ($heads.Count -ne 1) { throw 'Exactly one live False Knight Head collider is required.' }
    $head = $heads[0]
    $healths = @($Snapshot.BossHealthManagersJson | ConvertFrom-Json | Where-Object {
        $_.objectPath -eq $head.objectPath
    })
    if ($healths.Count -ne 1) { throw 'The Head collider must resolve to one observed HealthManager.' }
    $health = $healths[0]
    foreach ($field in @('centerX', 'centerY', 'extentX', 'extentY')) {
        if ($null -eq $head.PSObject.Properties[$field] -or $null -eq $head.$field -or
                [double]::IsNaN([double]$head.$field) -or
                [double]::IsInfinity([double]$head.$field) -or
                ($field -like 'extent*' -and [double]$head.$field -lt 0)) {
            throw "Invalid Head collider field: $field"
        }
    }
    $targetX = [double]$head.centerX
    $side = if ($targetX -gt $Snapshot.BossX) { 'right' } else { 'left' }
    $deltaX = $targetX - [double]$Snapshot.HeroX
    $verticalGap = [Math]::Abs([double]$head.centerY - $Snapshot.HeroColliderCenterY) `
        - [double]$head.extentY - $Snapshot.HeroColliderExtentY
    return [pscustomobject][ordered]@{
        CanAttemptSlash = $head.boundsAvailable -eq $true -and $head.enabled -eq $true `
            -and $head.activeInHierarchy -eq $true -and $head.extentX -gt 0 -and $head.extentY -gt 0 `
            -and $health.activeInHierarchy -eq $true -and $health.enabled -eq $true `
            -and $health.hp -gt 0 -and $health.dead -eq $false -and $health.invincible -eq $false `
            -and $health.invincibleFromDirection -eq 0 -and $verticalGap -le 0.65
        Side = $side
        X = [single]$targetX
        DeltaX = [single]$deltaX
        Direction = if ($deltaX -lt -0.05) {
            'left'
        }
        elseif ($deltaX -gt 0.05) {
            'right'
        }
        else {
            '-'
        }
        HeroOnExposedSide = ($side -eq 'right' `
                -and $Snapshot.HeroX -gt $Snapshot.BossX + 0.8) `
            -or ($side -eq 'left' `
                -and $Snapshot.HeroX -lt $Snapshot.BossX - 0.8)
    }
}

function Get-FalseKnightImminentHazardThreat {
    param([Parameter(Mandatory)]$Snapshot)

    try {
        $hazards = @($Snapshot.HazardsJson | ConvertFrom-Json -Depth 100)
    }
    catch {
        throw 'Hazard observation is malformed; refusing to interpret missing danger data as safe.'
    }

    $candidates = [Collections.Generic.List[object]]::new()
    foreach ($hazard in $hazards) {
        if ([string]$hazard.objectPath -eq $Snapshot.BossObjectPath `
                -or ([int]$hazard.damageDealt -le 0 `
                    -and [string]$hazard.layerName `
                        -notmatch '^(Attack|Enemy Attack)$') `
                -or [string]$hazard.mainState -eq 'Deactivate') {
            continue
        }

        $name = [string]$hazard.objectName
        $distance = [double]$hazard.distance
        $deltaX = [double]$hazard.deltaX
        $deltaY = [double]$hazard.deltaY
        $velocityY = [double]$hazard.velocityY
        $kind = ''
        $priority = 99

        if ([bool]$hazard.overlapHero) {
            $kind = 'Overlap'
            $priority = 0
        }
        elseif ($name -eq 'Hitter' `
                -and $distance -le 7.5) {
            # Ground and aerial captures measured the live Hitter connecting at
            # 6.28 and 5.2 units respectively.  Begin at 7.5 so a non-shade
            # dash can clear the measured future sweep edge; its published
            # transform is not its tall collider center, so deltaY must not
            # filter it out.
            $kind = 'Hitter'
            $priority = 1
        }
        elseif ($name -match 'Shockwave' `
                -and $distance -le 5.5 `
                -and [Math]::Abs($deltaY) -le 2.5) {
            $kind = 'Shockwave'
            $priority = 2
        }
        elseif ($name -match '(Barrel|Debris|Rock)' `
                -and ($deltaY -gt 0.4 -or $velocityY -lt -0.1) `
                -and ([Math]::Abs($deltaX) -le 2.4 `
                    -or $distance -le 2.75)) {
            $kind = 'FallingObject'
            $priority = 1
        }
        elseif ($distance -le 2.25) {
            $kind = 'Generic'
            $priority = 3
        }

        if ($kind -ne '') {
            $candidates.Add([pscustomobject][ordered]@{
                Kind = $kind
                Priority = $priority
                Hazard = $hazard
                Distance = [single]$distance
            })
        }
    }

    # DamageHero is attached to the Hitter object, while its transform-based
    # hazard entry does not expose the animated BoxCollider2D bounds.  The
    # descendant collider catalog does.  During the hammer antic the collider
    # is still high, but its horizontal sweep column already predicts the hit
    # two to three ticks before the transform entry becomes close.
    try {
        $bossColliders = @(
            $Snapshot.BossCollidersJson | ConvertFrom-Json -Depth 100
        )
    }
    catch {
        $bossColliders = @()
    }
    foreach ($collider in $bossColliders) {
        if ([string]$collider.objectName -ne 'Hitter' `
                -or -not [bool]$collider.activeInHierarchy `
                -or -not [bool]$collider.enabled `
                -or [double]$collider.extentX -le 0.0) {
            continue
        }
        $horizontalEdgeGap = [Math]::Max(
            0.0,
            [Math]::Abs([double]$collider.deltaX) `
                - [double]$collider.extentX `
                - 0.25)
        if ($horizontalEdgeGap -le 0.75) {
            $candidates.Add([pscustomobject][ordered]@{
                Kind = 'HitterCollider'
                Priority = 0
                Hazard = $collider
                Distance = [single]$horizontalEdgeGap
            })
        }
    }

    return $candidates |
        Sort-Object Priority, Distance,
            @{ Expression = { [string]$_.Hazard.objectPath } } |
        Select-Object -First 1
}

function Get-FalseKnightBossCollisionThreat {
    param(
        [Parameter(Mandatory)]$Snapshot,
        [Parameter(Mandatory)]$Geometry
    )

    if ($Snapshot.ContactOverlap) {
        return [pscustomobject]@{ Kind = 'ContactOverlap' }
    }

    $bossAbove = $Geometry.CenterDeltaY -gt 0.0
    $verticalClosingSpeed = [double]$Snapshot.HeroVelocityY `
        - [double]$Snapshot.BossVelocityY
    $verticalContactFrames = if ($verticalClosingSpeed -gt 0.5) {
        [double]$Geometry.EdgeGapY / $verticalClosingSpeed * 50.0
    }
    else {
        [double]::PositiveInfinity
    }
    $descending = $bossAbove `
        -and [double]$Snapshot.BossVelocityY -le -5.0 `
        -and ($Geometry.EdgeGapY -le 0.1 `
            -or $verticalContactFrames -le 8.0)
    if ($descending `
            -and $Geometry.EdgeGapX -le 2.0) {
        return [pscustomobject]@{
            Kind = 'DescendingBody'
            ContactFrames = [single]$verticalContactFrames
        }
    }

    $heroAbove = $Geometry.CenterDeltaY -lt 0.0
    $heroDownwardClosingSpeed = [double]$Snapshot.BossVelocityY `
        - [double]$Snapshot.HeroVelocityY
    $heroContactFrames = if ($heroDownwardClosingSpeed -gt 0.5) {
        [double]$Geometry.EdgeGapY / $heroDownwardClosingSpeed * 50.0
    }
    else {
        [double]::PositiveInfinity
    }
    if ($heroAbove `
            -and [double]$Snapshot.HeroVelocityY -le -3.0 `
            -and $heroContactFrames -le 8.0 `
            -and $Geometry.EdgeGapX -le 0.85) {
        return [pscustomobject]@{
            Kind = 'HeroDescendingBody'
            ContactFrames = [single]$heroContactFrames
        }
    }

    $relativeVelocityX = [double]$Snapshot.BossVelocityX `
        - [double]$Snapshot.HeroVelocityX
    $closingHorizontally = ($Geometry.CenterDeltaX -gt 0.05 `
            -and $relativeVelocityX -lt -2.0) `
        -or ($Geometry.CenterDeltaX -lt -0.05 `
            -and $relativeVelocityX -gt 2.0)
    if ($closingHorizontally `
            -and $Geometry.EdgeGapX -le 0.2 `
            -and $Geometry.EdgeGapY -le 0.1) {
        return [pscustomobject]@{ Kind = 'ClosingBody' }
    }

    if ($Geometry.EdgeGapX -le 0.05 `
            -and $Geometry.EdgeGapY -le 0.05) {
        return [pscustomobject]@{ Kind = 'NearContact' }
    }

    return $null
}

function New-FalseKnightDecision {
    param(
        [Parameter(Mandatory)][string]$RuleId,
        [Parameter(Mandatory)][string]$Hold,
        [Parameter(Mandatory)][ValidateRange(1, 60)][int]$Ticks,
        [Parameter(Mandatory)][string]$Reason
    )

    return [pscustomobject][ordered]@{
        RuleId = $RuleId
        Hold = $Hold
        Ticks = $Ticks
        Reason = $Reason
    }
}

function Select-FalseKnightDecision {
    param(
        [Parameter(Mandatory)]$Snapshot,
        [Parameter(Mandatory)]$ControllerState
    )

    if (-not $Snapshot.BossAvailable) {
        return New-FalseKnightDecision `
            -RuleId 'WAIT_TERMINAL_TRANSITION' `
            -Hold '-' `
            -Ticks 2 `
            -Reason 'No primary boss is currently exposed; advance only enough to observe latched terminal events.'
    }

    $state = [string]$Snapshot.BossState
    $hazardThreat = Get-FalseKnightImminentHazardThreat -Snapshot $Snapshot
    if ($ControllerState.FocusStartHealth -ge 0 `
            -and $null -eq $hazardThreat `
            -and $Snapshot.HeroAnimation -match '^Focus' `
            -and $state -match '^(Pause Short|Open Uuup|Opened( 2)?|Hit( 2)?)$' `
            -and $Snapshot.HeroHealth -le $ControllerState.FocusStartHealth `
            -and $Snapshot.HeroSoul -gt 0) {
        return New-FalseKnightDecision `
            -RuleId 'STAGGER_HEAL_HOLD' `
            -Hold 'cast' `
            -Ticks 2 `
            -Reason 'Continue the already-started focus for two exact ticks; stop as soon as health actually increases or the safe window closes.'
    }

    if (-not $Snapshot.HeroAcceptingInput `
            -or $Snapshot.HeroActorState -eq 'no_input' `
            -or $Snapshot.HeroAnimation -match '^(Stun|Recoil)') {
        return New-FalseKnightDecision `
            -RuleId 'WAIT_FOR_CONTROL' `
            -Hold '-' `
            -Ticks 1 `
            -Reason 'HeroController or its actor/animation state is in a non-input recovery window.'
    }

    $toward = Get-FalseKnightTowardDirection -Snapshot $Snapshot
    $away = Resolve-FalseKnightArenaDirection `
        -Direction (Get-FalseKnightAwayDirection -Snapshot $Snapshot) `
        -HeroX $Snapshot.HeroX
    $geometry = Get-FalseKnightCombatGeometry -Snapshot $Snapshot
    $attackReady = $Snapshot.HeroAttackCooldown -le 0 `
        -and $Snapshot.MovieTick - $ControllerState.LastAttackTick -ge 2
    $jumpReady = $Snapshot.MovieTick - $ControllerState.LastJumpTick -ge 24
    $dashReady = $Snapshot.MovieTick - $ControllerState.LastDashTick -ge 30

    if ($null -ne $hazardThreat) {
        $hazard = $hazardThreat.Hazard
        $hazardAway = if ([double]$hazard.deltaX -gt 0.1) {
            'left'
        }
        elseif ([double]$hazard.deltaX -lt -0.1) {
            'right'
        }
        else {
            $away
        }
        $hazardAway = Resolve-FalseKnightArenaDirection `
            -Direction $hazardAway `
            -HeroX $Snapshot.HeroX
        $hitterThreat = $hazardThreat.Kind -eq 'Hitter' `
            -or $hazardThreat.Kind -eq 'HitterCollider'
        $dashDodgeDirection = if (
            $hitterThreat -and $Snapshot.HeroShadowDashReady) {
            # False Knight's mace collider rotates from a tall column into a
            # long horizontal sweep.  Running away follows that sweep and can
            # remain inside it for several frames.  Cross the live swing toward
            # the boss instead: shade dash supplies the invulnerability and the
            # final position remains in immediate nail range.
            $toward
        }
        else {
            $hazardAway
        }
        if ($hazardThreat.Kind -eq 'Shockwave') {
            if ($Snapshot.HeroJumping -and -not $Snapshot.HeroFalling) {
                return New-FalseKnightDecision `
                    -RuleId 'MICRO_DODGE_SHOCKWAVE_HOLD_JUMP' `
                    -Hold "$toward,jump" `
                    -Ticks 2 `
                    -Reason ('Keep offensive drift toward the boss while clearing the measured nearby shockwave: ' + [string]$hazard.objectPath)
            }
            if ($jumpReady -and $Snapshot.HeroOnGround) {
                return New-FalseKnightDecision `
                    -RuleId 'MICRO_DODGE_SHOCKWAVE_JUMP' `
                    -Hold "$toward,jump" `
                    -Ticks 2 `
                    -Reason ('Jump toward the boss only after the shockwave enters its measured 5.5-unit intercept band: ' + [string]$hazard.objectPath)
            }
            if ($dashReady -and -not $Snapshot.HeroDashing) {
                return New-FalseKnightDecision `
                    -RuleId 'MICRO_DODGE_SHOCKWAVE_AIR_DASH' `
                    -Hold "$away,dash" `
                    -Ticks 1 `
                    -Reason ('Use one air-dash input away from the boss because a toward-dash would overshoot through its body: ' + [string]$hazard.objectPath)
            }
            return New-FalseKnightDecision `
                -RuleId 'MICRO_DODGE_SHOCKWAVE_DRIFT' `
                -Hold $toward `
                -Ticks 2 `
                -Reason ('Use the minimum two-frame continuous drift that can affect HeroController movement, then re-observe: ' + [string]$hazard.objectPath)
        }

        if ($dashReady -and -not $Snapshot.HeroDashing) {
            return New-FalseKnightDecision `
                -RuleId ('MICRO_DODGE_' + $hazardThreat.Kind.ToUpperInvariant() + '_DASH') `
                -Hold "$dashDodgeDirection,dash" `
                -Ticks 1 `
                -Reason ('One dash frame crosses the measured imminent hazard while preserving immediate attack range: ' + [string]$hazard.objectPath)
        }
        if ($jumpReady -and $Snapshot.HeroOnGround) {
            return New-FalseKnightDecision `
                -RuleId ('MICRO_DODGE_' + $hazardThreat.Kind.ToUpperInvariant() + '_JUMP') `
                -Hold "$hazardAway,jump" `
                -Ticks 2 `
                -Reason ('Two continuous frames are the minimum reliable jump-start for the measured imminent hazard: ' + [string]$hazard.objectPath)
        }
        return New-FalseKnightDecision `
            -RuleId ('MICRO_DODGE_' + $hazardThreat.Kind.ToUpperInvariant() + '_STEP') `
            -Hold $hazardAway `
            -Ticks 3 `
            -Reason ('Hold the selected crossing direction for the minimum three-frame batch that produces displacement after input/animation latency, then re-observe: ' + [string]$hazard.objectPath)
    }

    $bodyThreat = Get-FalseKnightBossCollisionThreat `
        -Snapshot $Snapshot `
        -Geometry $geometry
    if ($null -ne $bodyThreat) {
        if ($dashReady -and -not $Snapshot.HeroDashing) {
            $bossMovingTowardHero = ($Snapshot.BossX -lt $Snapshot.HeroX -and $Snapshot.BossVelocityX -gt 0.5) `
                -or ($Snapshot.BossX -gt $Snapshot.HeroX -and $Snapshot.BossVelocityX -lt -0.5)
            $bodyDashDirection = if ($bodyThreat.Kind -eq 'DescendingBody' `
                    -and $Snapshot.HeroShadowDashReady -and $bossMovingTowardHero) {
                $toward
            }
            else {
                $away
            }
            return New-FalseKnightDecision `
                -RuleId ('MICRO_DODGE_BOSS_' + $bodyThreat.Kind.ToUpperInvariant() + '_DASH') `
                -Hold "$bodyDashDirection,dash" `
                -Ticks 1 `
                -Reason ('Cross an approaching descending body only with shade dash available; otherwise clear contact outward: ' + $bodyThreat.Kind)
        }
        if ($jumpReady -and $Snapshot.HeroOnGround) {
            return New-FalseKnightDecision `
                -RuleId ('MICRO_DODGE_BOSS_' + $bodyThreat.Kind.ToUpperInvariant() + '_JUMP') `
                -Hold "$away,jump" `
                -Ticks 2 `
                -Reason ('The live colliders predict boss contact; use the minimum reliable two-frame jump-start: ' + $bodyThreat.Kind)
        }
        return New-FalseKnightDecision `
            -RuleId ('MICRO_DODGE_BOSS_' + $bodyThreat.Kind.ToUpperInvariant() + '_STEP') `
            -Hold $away `
            -Ticks 3 `
            -Reason ('The live colliders predict boss contact; hold away for the minimum three-frame displacement batch and re-observe: ' + $bodyThreat.Kind)
    }

    if ($state -match '^(Stun In Air|Stun Land)$') {
        $target = Get-FalseKnightExposedTarget -Snapshot $Snapshot
        $trackDirection = if ($target.HeroOnExposedSide) {
            [string]$target.Direction
        }
        else {
            [string]$target.Side
        }
        if ($trackDirection -eq '-') {
            $trackDirection = $toward
        }
        return New-FalseKnightDecision `
            -RuleId 'STAGGER_TRACK_EXPOSED_SIDE' `
            -Hold $trackDirection `
            -Ticks 2 `
            -Reason 'Track the facing-derived exposed side during knockdown instead of retreating from the coming damage window.'
    }

    if ($state -match '^(Pause Short|Open Uuup|Opened( 2)?|Hit( 2)?)$') {
        $target = Get-FalseKnightExposedTarget -Snapshot $Snapshot
        $targetGap = [Math]::Abs([double]$target.DeltaX)
        if (-not $target.HeroOnExposedSide) {
            if ($jumpReady -and $Snapshot.HeroOnGround) {
                return New-FalseKnightDecision `
                    -RuleId 'STAGGER_CROSS_TO_EXPOSED_SIDE' `
                    -Hold "$($target.Side),jump" `
                    -Ticks 1 `
                    -Reason 'The live facing field places the vulnerable side across the shell; start the shortest crossing jump.'
            }
            return New-FalseKnightDecision `
                -RuleId 'STAGGER_MOVE_TO_EXPOSED_SIDE' `
                -Hold $target.Side `
                -Ticks 2 `
                -Reason 'Continue crossing toward the vulnerable side selected from the current boss facing.'
        }
        if ($targetGap -lt 0.75) {
            $targetAway = if ($target.Direction -eq 'left') {
                'right'
            }
            elseif ($target.Direction -eq 'right') {
                'left'
            }
            else {
                $away
            }
            return New-FalseKnightDecision `
                -RuleId 'STAGGER_CREATE_SLASH_SPACE' `
                -Hold $targetAway `
                -Ticks 1 `
                -Reason 'Back off one tick only because the measured exposed target is inside the nail dead zone.'
        }
        if ($targetGap -gt 3.75) {
            return New-FalseKnightDecision `
                -RuleId 'STAGGER_APPROACH_EXPOSED' `
                -Hold $target.Direction `
                -Ticks 2 `
                -Reason 'Close the measured gap to the facing-derived exposed target before its recovery window ends.'
        }
        if ($Snapshot.HeroHealth -le 5 `
                -and $Snapshot.HeroSoul -ge 33 `
                -and $Snapshot.HeroOnGround `
                -and -not $Snapshot.HeroAttacking `
                -and $Snapshot.MovieTick - $ControllerState.LastHealTick -ge 240) {
            return New-FalseKnightDecision `
                -RuleId 'STAGGER_HEAL_ONCE' `
                -Hold 'cast' `
                -Ticks 2 `
                -Reason 'Start one focus inside this broken-shell safety window; subsequent two-tick observations stop it immediately after the actual heal.'
        }
        if ($attackReady -and $target.CanAttemptSlash) {
            return New-FalseKnightDecision `
                -RuleId 'STAGGER_SLASH_EXPOSED' `
                -Hold "$($target.Direction),attack" `
                -Ticks 1 `
                -Reason 'The live Head geometry and health flags allow a slash attempt; this is not a guarantee of damage.'
        }
        return New-FalseKnightDecision `
            -RuleId 'STAGGER_HOLD_RANGE' `
            -Hold '-' `
            -Ticks 1 `
            -Reason 'Remain in exposed-target range for one release frame before the next distinct nail press.'
    }

    if ($attackReady) {
        if ($geometry.EdgeGapX -le 1.7 `
                -and $geometry.EdgeGapY -le 0.65) {
            return New-FalseKnightDecision `
                -RuleId 'PRESSURE_ATTACK_HORIZONTAL' `
                -Hold "$toward,attack" `
                -Ticks 1 `
                -Reason ('Attack because the live collider edges are inside horizontal nail reach; boss state is observational only: ' + $state)
        }
        if ($geometry.CenterDeltaY -gt 0.2 `
                -and $geometry.EdgeGapX -le 0.85 `
                -and $geometry.EdgeGapY -le 2.3) {
            return New-FalseKnightDecision `
                -RuleId 'PRESSURE_ATTACK_UPWARD' `
                -Hold "$toward,up,attack" `
                -Ticks 1 `
                -Reason ('Attack upward because the live boss collider is above and inside vertical nail reach; boss state is observational only: ' + $state)
        }
        if (-not $Snapshot.HeroOnGround `
                -and $geometry.CenterDeltaY -lt -0.2 `
                -and $geometry.EdgeGapX -le 0.85 `
                -and $geometry.EdgeGapY -le 3.6) {
            return New-FalseKnightDecision `
                -RuleId 'PRESSURE_ATTACK_DOWNWARD' `
                -Hold "$toward,down,attack" `
                -Ticks 1 `
                -Reason ('Pogo because the live boss collider is below and inside vertical nail reach; boss state is observational only: ' + $state)
        }
    }

    $bossAbove = $geometry.CenterDeltaY -gt 0.4
    if ($bossAbove `
            -and $geometry.EdgeGapY -gt 0.65 `
            -and $geometry.EdgeGapX -le 3.5) {
        if ($jumpReady -and $Snapshot.HeroOnGround) {
            return New-FalseKnightDecision `
                -RuleId 'PRESSURE_JUMP_CHASE' `
                -Hold "$toward,jump" `
                -Ticks 2 `
                -Reason ('Jump toward the live airborne collider to create an attack, not because the FSM state is considered dangerous: ' + $state)
        }
        if ($Snapshot.HeroJumping -and -not $Snapshot.HeroFalling) {
            return New-FalseKnightDecision `
                -RuleId 'PRESSURE_HOLD_JUMP_CHASE' `
                -Hold "$toward,jump" `
                -Ticks 2 `
                -Reason ('Keep ascending toward the live collider so the next observation can produce an upward or horizontal hit: ' + $state)
        }
    }

    if ($geometry.EdgeGapX -gt 6.0 `
            -and $dashReady `
            -and -not $Snapshot.HeroDashing) {
        return New-FalseKnightDecision `
            -RuleId 'PRESSURE_DASH_APPROACH' `
            -Hold "$toward,dash" `
            -Ticks 1 `
            -Reason ('Use one dash input to close a large live collider-edge gap while leaving more than a full dash of stopping space: ' + $state)
    }

    if ($geometry.EdgeGapX -gt 1.45) {
        $approachTicks = if ($geometry.EdgeGapX -gt 4.0) {
            3
        }
        else {
            2
        }
        return New-FalseKnightDecision `
            -RuleId 'PRESSURE_APPROACH' `
            -Hold $toward `
            -Ticks $approachTicks `
            -Reason ('Close the live collider-edge gap in every FSM state; no predicted hit currently requires retreat: ' + $state)
    }

    if ($geometry.EdgeGapX -lt 0.4 -and $geometry.EdgeGapY -le 0.1) {
        return New-FalseKnightDecision `
            -RuleId 'PRESSURE_MICRO_SPACE' `
            -Hold $away `
            -Ticks 2 `
            -Reason 'Use the minimum two-frame displacement batch to leave the nail dead zone while staying adjacent.'
    }

    return New-FalseKnightDecision `
        -RuleId 'PRESSURE_HOLD_ATTACK_RANGE' `
        -Hold '-' `
        -Ticks 1 `
        -Reason ('Release attack for one frame while retaining immediate nail range; no dodge is geometrically necessary: ' + $state)
}

function Test-FalseKnightTerminalSuccess {
    param([Parameter(Mandatory)]$Snapshot)

    # HealthManager.OnDeath and BossSceneController.OnBossesDead are two
    # independent structured facts.  Together with the latched encounter
    # identity they prove the requested kill before the optional return-scene
    # transition can invalidate the paused authoring lease.
    return $Snapshot.BossDeathObserved `
        -and $Snapshot.BossesDeadObserved `
        -and $Snapshot.TerminalScene -eq 'GG_False_Knight' `
        -and $Snapshot.TerminalLevel -eq 0 `
        -and $Snapshot.TerminalDifficulty -eq 'Attuned'
}

function Update-FalseKnightControllerState {
    param(
        [Parameter(Mandatory)]$ControllerState,
        [Parameter(Mandatory)]$Before,
        [Parameter(Mandatory)]$After,
        [Parameter(Mandatory)]$Decision
    )

    $tokens = @([string]$Decision.Hold -split ',')
    if ($tokens -contains 'attack' `
            -and ($After.HeroAttacking `
                -or ($Before.BossAvailable `
                    -and $After.BossAvailable `
                    -and $After.BossHp -lt $Before.BossHp))) {
        $ControllerState.LastAttackTick = $Before.MovieTick
    }
    if ($tokens -contains 'jump' `
            -and $Before.HeroOnGround `
            -and (-not $After.HeroOnGround `
                -or $After.HeroJumping `
                -or $After.HeroVelocityY -gt $Before.HeroVelocityY + 1.0)) {
        $ControllerState.LastJumpTick = $Before.MovieTick
    }
    if ($tokens -contains 'dash' `
            -and ($After.HeroDashing `
                -or [Math]::Abs([double]$After.HeroVelocityX) -ge 15.0)) {
        $ControllerState.LastDashTick = $Before.MovieTick
    }
    if ($Decision.RuleId -eq 'STAGGER_HEAL_ONCE' `
            -and ($After.HeroAnimation -match '^Focus' `
                -or $After.HeroSoul -lt $Before.HeroSoul)) {
        $ControllerState.LastHealTick = $Before.MovieTick
        $ControllerState.FocusStartHealth = $Before.HeroHealth
    }
    if ($ControllerState.FocusStartHealth -ge 0 `
            -and $After.HeroHealth -gt $ControllerState.FocusStartHealth) {
        $ControllerState.FocusStartHealth = -1
    }
    if ($Before.BossAvailable) {
        $ControllerState.MinimumBossHp = [Math]::Min(
            $ControllerState.MinimumBossHp,
            $Before.BossHp)
    }
    if ($After.BossAvailable) {
        $ControllerState.MinimumBossHp = [Math]::Min(
            $ControllerState.MinimumBossHp,
            $After.BossHp)
    }
    $ControllerState.MinimumHeroHealth = [Math]::Min(
        $ControllerState.MinimumHeroHealth,
        $After.HeroHealth)
    if ($Before.BossAvailable -and $After.BossAvailable `
            -and $After.BossHp -lt $Before.BossHp) {
        $ControllerState.DamageEventCount++
    }
    if ($ControllerState.LastBossHp -ge 0 `
            -and $ControllerState.LastBossHp -le 36 `
            -and $After.BossHp -ge 200 `
            -and $After.BossState -match '^Stun') {
        $ControllerState.ArmorBreakCount++
    }
    if ($After.BossAvailable) {
        $ControllerState.LastBossHp = $After.BossHp
    }
    $ControllerState.DecisionCount++
}

function Convert-FalseKnightTraceState {
    param([Parameter(Mandatory)]$Snapshot)

    return [ordered]@{
        movieTick = $Snapshot.MovieTick
        sceneEpoch = $Snapshot.SceneEpoch
        scene = $Snapshot.Scene
        encounterActive = $Snapshot.EncounterActive
        encounterScene = $Snapshot.EncounterScene
        encounterLevel = $Snapshot.EncounterLevel
        encounterDifficulty = $Snapshot.EncounterDifficulty
        bossesAlive = $Snapshot.BossesAlive
        bossAvailable = $Snapshot.BossAvailable
        bossObjectPath = $Snapshot.BossObjectPath
        bossHp = $Snapshot.BossHp
        bossDead = $Snapshot.BossDead
        bossState = $Snapshot.BossState
        bossRecentEvent = $Snapshot.BossRecentEvent
        bossFacing = $Snapshot.BossFacing
        bossCollidersJson = $Snapshot.BossCollidersJson
        hazardsJson = $Snapshot.HazardsJson
        recentHeroDamage = [ordered]@{
            sequence = $Snapshot.HeroDamageSequence
            sourcePath = $Snapshot.HeroDamageSourcePath
            sourceName = $Snapshot.HeroDamageSourceName
            side = $Snapshot.HeroDamageSide
            amount = $Snapshot.HeroDamageAmount
            hazardType = $Snapshot.HeroDamageHazardType
        }
        bossPosition = [ordered]@{ x = $Snapshot.BossX; y = $Snapshot.BossY }
        bossVelocity = [ordered]@{
            x = $Snapshot.BossVelocityX
            y = $Snapshot.BossVelocityY
        }
        bossCollider = [ordered]@{
            centerX = $Snapshot.BossColliderCenterX
            centerY = $Snapshot.BossColliderCenterY
            extentX = $Snapshot.BossColliderExtentX
            extentY = $Snapshot.BossColliderExtentY
        }
        relative = [ordered]@{
            deltaX = $Snapshot.DeltaX
            deltaY = $Snapshot.DeltaY
            distance = $Snapshot.Distance
            side = $Snapshot.BossSide
            overlap = $Snapshot.ContactOverlap
        }
        hero = [ordered]@{
            x = $Snapshot.HeroX
            y = $Snapshot.HeroY
            velocityX = $Snapshot.HeroVelocityX
            velocityY = $Snapshot.HeroVelocityY
            health = $Snapshot.HeroHealth
            maxHealth = $Snapshot.HeroMaxHealth
            soul = $Snapshot.HeroSoul
            actorState = $Snapshot.HeroActorState
            animation = $Snapshot.HeroAnimation
            onGround = $Snapshot.HeroOnGround
            jumping = $Snapshot.HeroJumping
            falling = $Snapshot.HeroFalling
            dashing = $Snapshot.HeroDashing
            shadowDashing = $Snapshot.HeroShadowDashing
            shadowDashReady = $Snapshot.HeroShadowDashReady
            shadowDashCooldownSeconds = $Snapshot.HeroShadowDashCooldown
            attacking = $Snapshot.HeroAttacking
            acceptingInput = $Snapshot.HeroAcceptingInput
        }
        milestone = [ordered]@{
            bossDeathObserved = $Snapshot.BossDeathObserved
            bossesDeadObserved = $Snapshot.BossesDeadObserved
            sceneCompleteObserved = $Snapshot.SceneCompleteObserved
            terminalScene = $Snapshot.TerminalScene
            terminalLevel = $Snapshot.TerminalLevel
            terminalDifficulty = $Snapshot.TerminalDifficulty
        }
    }
}

function Convert-FalseKnightRecordingText {
    param([Parameter(Mandatory)][string]$RecordedMovieBase64)

    $recordedText = [Text.UTF8Encoding]::new($false, $true).GetString(
        [Convert]::FromBase64String($RecordedMovieBase64))
    if (-not [regex]::IsMatch($recordedText, '\Ahktas 1\r?\n') `
            -or -not [regex]::IsMatch($recordedText, '(?m)^---\r?$') `
            -or -not [regex]::IsMatch($recordedText, '(?m)^frames [1-9][0-9]* hold=')) {
        throw 'Recorded movie is missing its HKTAS header, separator, or input frames.'
    }

    # stopRecording already returns the complete journal rooted at its saved
    # baseline. Preserve every frame and every header byte. Leading idle ticks
    # are real simulation time; removing them changes animation, physics and RNG.
    # Do not graft an older route prelude or a new terminal checkpoint onto it.
    return [pscustomobject]@{
        Source = $recordedText
        JournalPrefixTicks = 0L
    }
}

function Convert-FalseKnightRecordingToReplayCandidate {
    param(
        [string]$RoutePath = '',
        [Parameter(Mandatory)][string]$RecordedMovieBase64,
        [Parameter(Mandatory)][string]$OutputPath
    )

    # RoutePath is accepted for existing callers only; it does not rewrite the
    # recorded root, prefix, or input. The caller still validates the full Movie.
    $candidate = Convert-FalseKnightRecordingText -RecordedMovieBase64 $RecordedMovieBase64
    [IO.File]::WriteAllText($OutputPath, $candidate.Source, [Text.UTF8Encoding]::new($false))
    return $candidate
}

function Wait-FalseKnightStablePaused {
    param(
        [Parameter(Mandatory)]$Client,
        [Parameter(Mandatory)][long]$MinimumMovieTick,
        [long]$MaximumMovieTick = [long]::MaxValue,
        [ValidateRange(2, 10)][int]$StableSamples = 3,
        [ValidateRange(1, 60)][int]$TimeoutSeconds = 15
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastTick = -1L
    $matchingSamples = 0
    $last = $null
    do {
        $last = $Client.GetSemanticStateAsync(
                [Threading.CancellationToken]::None
            ).GetAwaiter().GetResult()
        $playback = [string]$last.State.Fields['playbackMode']
        if ($last.State.RuntimeMode -eq 'Faulted' `
                -or $playback -eq 'Faulted') {
            $controlFault = [string]$last.State.Fields['controlFault']
            $abortReason = [string]$last.State.Fields['lastControlAbortReason']
            $playbackFault = [string]$last.State.Fields['lastPlaybackFault']
            throw (
                'Runtime faulted while waiting for Paused/Idle: ' `
                + 'mode={0}; playback={1}; tick={2}; controlFault={3}; ' `
                + 'lastControlAbortReason={4}; playbackFault={5}' -f `
                    $last.State.RuntimeMode, `
                    $playback, `
                    $last.State.MovieTick, `
                    $controlFault, `
                    $abortReason, `
                    $playbackFault
            )
        }
        if ($last.State.MovieTick -gt $MaximumMovieTick) {
            throw (
                'Bounded input exceeded its exact movie-tick boundary: ' `
                + 'expectedAtMost={0}; actual={1}; mode={2}; playback={3}' -f `
                    $MaximumMovieTick, `
                    $last.State.MovieTick, `
                    $last.State.RuntimeMode, `
                    $playback
            )
        }
        if ($last.State.RuntimeMode -eq 'Paused' `
                -and $playback -eq 'Idle' `
                -and $last.State.MovieTick -ge $MinimumMovieTick) {
            if ($last.State.MovieTick -eq $lastTick) {
                $matchingSamples++
            }
            else {
                $lastTick = $last.State.MovieTick
                $matchingSamples = 1
            }
            if ($matchingSamples -ge $StableSamples) {
                return $last
            }
        }
        else {
            $lastTick = -1L
            $matchingSamples = 0
        }
        Start-Sleep -Milliseconds 15
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    $detail = if ($null -eq $last) {
        '<none>'
    }
    else {
        'mode={0}; playback={1}; tick={2}' -f `
            $last.State.RuntimeMode, `
            [string]$last.State.Fields['playbackMode'], `
            $last.State.MovieTick
    }
    throw "Paused/Idle movie tick did not stabilize: $detail"
}

function Stop-FalseKnightRecording {
    param(
        [Parameter(Mandatory)]$Client,
        [Parameter(Mandatory)][string]$LeaseId,
        [Parameter(Mandatory)][long]$MinimumMovieTick
    )

    $last = $null
    foreach ($attempt in 1..10) {
        $runtime = Wait-FalseKnightStablePaused `
            -Client $Client `
            -MinimumMovieTick $MinimumMovieTick
        $last = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'stopRecording' `
            -Scope 'control.recording' `
            -LeaseId $LeaseId `
            -ExpectedRuntimeMode 'Paused' `
            -ExpectedMovieTick $runtime.State.MovieTick `
            -AllowFailure
        if ($last.Success) {
            return [pscustomobject]@{
                runtime = $runtime
                result = $last
                attempts = $attempt
            }
        }
        if ($last.ResultCode -ne 'PreconditionFailed') {
            throw (
                'stopRecording failed: {0}: {1}' -f `
                    $last.ResultCode, `
                    $last.Detail
            )
        }
    }
    throw (
        'stopRecording could not acquire a stable tick: {0}: {1}' -f `
            $last.ResultCode, `
            $last.Detail
    )
}

function Invoke-FalseKnightAdaptiveFight {
    param(
        [Parameter(Mandatory)]$Client,
        [Parameter(Mandatory)][string]$CaseDirectory,
        [Parameter(Mandatory)][string]$ManifestSha256,
        [Parameter(Mandatory)][string]$RouteMoviePath,
        [ValidateRange(100, 20000)][int]$MaxFightTicks = 6000
    )

    $tracePath = Join-Path $CaseDirectory 'false-knight-decisions.jsonl'
    $leaseTracePath = Join-Path `
        $CaseDirectory `
        'false-knight-lease-renewals.jsonl'
    $leaseId = Acquire-SdkLease `
        -Client $Client `
        -Scopes @(
            'control.playback',
            'control.input',
            'control.recording',
            'control.replay-save'
        )
    $released = $false
    $stopRecording = $null
    try {
        $running = $Client.GetSemanticStateAsync(
                [Threading.CancellationToken]::None
            ).GetAwaiter().GetResult()
        $initialObservation = Get-AdaptiveCombatState -Client $Client
        $initial = New-FalseKnightSnapshot `
            -Values $initialObservation.values `
            -MovieTick $initialObservation.movieTick `
            -SceneEpoch $initialObservation.sceneEpoch
        if ($initial.Scene -ne 'GG_False_Knight' `
                -or $initial.EncounterScene -ne 'GG_False_Knight' `
                -or $initial.EncounterLevel -ne 0 `
                -or $initial.EncounterDifficulty -ne 'Attuned') {
            throw (
                'Adaptive fight did not start in Attuned False Knight: ' `
                + ($initial | ConvertTo-Json -Compress)
            )
        }

        $startRecording = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'startRecording' `
            -Scope 'control.recording' `
            -LeaseId $leaseId `
            -ExpectedRuntimeMode 'Running'
        $saveLabel = 't16-false-knight-arena-' `
            + [DateTimeOffset]::UtcNow.ToString('HHmmssfff')
        $createSave = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'createReplaySave' `
            -Scope 'control.replay-save' `
            -Arguments @{ label = $saveLabel } `
            -LeaseId $leaseId `
            -ExpectedRuntimeMode 'Running'
        $arenaSave = Wait-AdaptiveReplaySaveReady `
            -Client $Client `
            -Label $saveLabel
        $pause = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'pause' `
            -Scope 'control.playback' `
            -LeaseId $leaseId `
            -ExpectedRuntimeMode 'Running'
        $runtime = Wait-FalseKnightStablePaused `
            -Client $Client `
            -MinimumMovieTick $running.State.MovieTick
        $observation = Get-AdaptiveCombatState -Client $Client
        $controllerState = New-FalseKnightControllerState
        $fightStartTick = $runtime.State.MovieTick
        $outcome = 'Unknown'
        $finalSnapshot = $null
        $rightReactiveDecisions = 0
        $leftReactiveDecisions = 0
        $leaseRenewedAt = [DateTimeOffset]::UtcNow
        $leaseRenewalCount = 0

        while ($true) {
            if (([DateTimeOffset]::UtcNow - $leaseRenewedAt).TotalSeconds `
                    -ge 120) {
                $renewal = Renew-SdkLease `
                    -Client $Client `
                    -Scope 'control.input' `
                    -LeaseId $leaseId `
                    -TtlSeconds 300
                $leaseRenewedAt = [DateTimeOffset]::UtcNow
                $leaseRenewalCount++
                $renewalTrace = [ordered]@{
                    schemaVersion = 1
                    sequence = $leaseRenewalCount
                    renewedAtUtc = $leaseRenewedAt.ToString('O')
                    movieTick = $runtime.State.MovieTick
                    result = Convert-AutomationResult $renewal
                }
                [IO.File]::AppendAllText(
                    $leaseTracePath,
                    (($renewalTrace |
                        ConvertTo-Json -Compress -Depth 100) + "`n"),
                    [Text.UTF8Encoding]::new($false))
            }

            $before = New-FalseKnightSnapshot `
                -Values $observation.values `
                -MovieTick $observation.movieTick `
                -SceneEpoch $observation.sceneEpoch
            $finalSnapshot = $before
            if (Test-FalseKnightTerminalSuccess -Snapshot $before) {
                $outcome = 'TerminalMilestones'
                break
            }
            if ($before.HeroHealth -le 0 `
                    -or $before.HeroActorState -match 'dead') {
                $outcome = 'PlayerDead'
                break
            }
            if ($before.MovieTick - $fightStartTick -ge $MaxFightTicks) {
                $outcome = 'FightTickBudgetExceeded'
                break
            }

            $decision = Select-FalseKnightDecision `
                -Snapshot $before `
                -ControllerState $controllerState
            if ($before.BossSide -eq 'right' `
                    -and $decision.Hold -match '(^|,)right(,|$)') {
                $rightReactiveDecisions++
            }
            if ($before.BossSide -eq 'left' `
                    -and $decision.Hold -match '(^|,)left(,|$)') {
                $leftReactiveDecisions++
            }
            $command = Invoke-SdkCommand `
                -Client $Client `
                -CommandId 'queueInputBatch' `
                -Scope 'control.input' `
                -Arguments @{
                    candidateMovieBase64 = New-AdaptiveInputMovie `
                        -ManifestSha256 $ManifestSha256 `
                        -Hold $decision.Hold `
                        -Ticks $decision.Ticks
                    expectedSceneEpoch = [string]$before.SceneEpoch
                } `
                -LeaseId $leaseId `
                -ExpectedRuntimeMode 'Paused' `
                -ExpectedMovieTick $before.MovieTick `
                -AllowFailure
            if (-not $command.Success) {
                if ($command.ResultCode -ne 'PreconditionFailed') {
                    throw (
                        'queueInputBatch failed: {0}: {1}' -f `
                            $command.ResultCode, `
                            $command.Detail
                    )
                }
                $controllerState.PreconditionRetryCount++
                $runtime = Wait-FalseKnightStablePaused `
                    -Client $Client `
                    -MinimumMovieTick $before.MovieTick
                $observation = Get-AdaptiveCombatState -Client $Client
                $after = New-FalseKnightSnapshot `
                    -Values $observation.values `
                    -MovieTick $observation.movieTick `
                    -SceneEpoch $observation.sceneEpoch
                $trace = [ordered]@{
                    schemaVersion = 1
                    sequence = $controllerState.DecisionCount `
                        + $controllerState.PreconditionRetryCount
                    observed = Convert-FalseKnightTraceState -Snapshot $before
                    decision = [ordered]@{
                        ruleId = $decision.RuleId
                        reason = $decision.Reason
                        hold = $decision.Hold
                        ticks = $decision.Ticks
                    }
                    command = Convert-AutomationResult $command
                    result = Convert-FalseKnightTraceState -Snapshot $after
                    retriedAfterFreshObservation = $true
                    visualRecognitionUsed = $false
                    gameplayMutationUsed = $false
                }
                [IO.File]::AppendAllText(
                    $tracePath,
                    (($trace | ConvertTo-Json -Compress -Depth 100) + "`n"),
                    [Text.UTF8Encoding]::new($false))
                $finalSnapshot = $after
                continue
            }
            $runtime = Wait-FalseKnightStablePaused `
                -Client $Client `
                -MinimumMovieTick ($before.MovieTick + $decision.Ticks) `
                -MaximumMovieTick ($before.MovieTick + $decision.Ticks) `
                -TimeoutSeconds 15
            $observation = Get-AdaptiveCombatState -Client $Client
            $after = New-FalseKnightSnapshot `
                -Values $observation.values `
                -MovieTick $observation.movieTick `
                -SceneEpoch $observation.sceneEpoch
            Update-FalseKnightControllerState `
                -ControllerState $controllerState `
                -Before $before `
                -After $after `
                -Decision $decision
            $trace = [ordered]@{
                schemaVersion = 1
                sequence = $controllerState.DecisionCount
                observed = Convert-FalseKnightTraceState -Snapshot $before
                decision = [ordered]@{
                    ruleId = $decision.RuleId
                    reason = $decision.Reason
                    hold = $decision.Hold
                    ticks = $decision.Ticks
                }
                command = Convert-AutomationResult $command
                result = Convert-FalseKnightTraceState -Snapshot $after
                visualRecognitionUsed = $false
                gameplayMutationUsed = $false
            }
            [IO.File]::AppendAllText(
                $tracePath,
                (($trace | ConvertTo-Json -Compress -Depth 100) + "`n"),
                [Text.UTF8Encoding]::new($false))
            $finalSnapshot = $after
            if (Test-FalseKnightTerminalSuccess -Snapshot $after) {
                $outcome = 'TerminalMilestones'
                break
            }
        }

        $stop = Stop-FalseKnightRecording `
            -Client $Client `
            -LeaseId $leaseId `
            -MinimumMovieTick $runtime.State.MovieTick
        $runtime = $stop.runtime
        $stopRecording = $stop.result
        $recordedMovieBase64 = [string]$stopRecording.Data['movieBase64']
        $recordedMovieId = [string]$stopRecording.Data['movieId']
        if ($recordedMovieId -notmatch '^[0-9a-f]{64}$' `
                -or [string]::IsNullOrWhiteSpace($recordedMovieBase64)) {
            throw 'Adaptive stopRecording omitted its canonical movie.'
        }
        [IO.File]::WriteAllBytes(
            (Join-Path $CaseDirectory 'false-knight-recorded-full.hktas'),
            [Convert]::FromBase64String($recordedMovieBase64))
        $fullMoviePath = Join-Path `
            $CaseDirectory `
            'false-knight-replay-candidate.hktas'
        $candidate = Convert-FalseKnightRecordingToReplayCandidate `
            -RoutePath $RouteMoviePath `
            -RecordedMovieBase64 $recordedMovieBase64 `
            -OutputPath $fullMoviePath
        $merged = [string]$candidate.Source
        $validate = Invoke-SdkCommand `
            -Client $Client `
            -CommandId 'validateMoviePatch' `
            -Scope 'movie.validate' `
            -Arguments @{
                candidateMovieBase64 = [Convert]::ToBase64String(
                    [Text.UTF8Encoding]::new($false, $true).GetBytes($merged))
            }

        $terminalPass = $outcome -eq 'TerminalMilestones' `
            -and (Test-FalseKnightTerminalSuccess -Snapshot $finalSnapshot)
        $reactiveSidePass = $rightReactiveDecisions -gt 0 `
            -and $leftReactiveDecisions -gt 0
        $verdict = if ($terminalPass -and $reactiveSidePass) {
            'ADAPTIVE_FIGHT_PASS'
        }
        elseif ($terminalPass) {
            'ADAPTIVE_FIGHT_POLICY_COVERAGE_FAIL'
        }
        else {
            'ADAPTIVE_FIGHT_FAIL'
        }
        $release = Release-SdkLease `
            -Client $Client `
            -Scope 'control.input' `
            -LeaseId $leaseId
        $released = $true
        $result = [ordered]@{
            schemaVersion = 1
            verdict = $verdict
            outcome = $outcome
            terminalPass = $terminalPass
            reactiveSidePass = $reactiveSidePass
            visualRecognitionUsed = $false
            gameplayMutationUsed = $false
            controlLoop = 'getCombatState -> queueInputBatch -> getCombatState'
            fightStartMovieTick = $fightStartTick
            finalMovieTick = $finalSnapshot.MovieTick
            fightMovieTicks = $finalSnapshot.MovieTick - $fightStartTick
            decisionCount = $controllerState.DecisionCount
            preconditionRetryCount = $controllerState.PreconditionRetryCount
            leaseRenewalCount = $leaseRenewalCount
            damageEventCount = $controllerState.DamageEventCount
            armorBreakCount = $controllerState.ArmorBreakCount
            minimumBossHp = if (
                $controllerState.MinimumBossHp -eq [int]::MaxValue
            ) { $null } else { $controllerState.MinimumBossHp }
            minimumHeroHealth = if (
                $controllerState.MinimumHeroHealth -eq [int]::MaxValue
            ) { $initial.HeroHealth } else { $controllerState.MinimumHeroHealth }
            rightReactiveDecisions = $rightReactiveDecisions
            leftReactiveDecisions = $leftReactiveDecisions
            terminal = Convert-FalseKnightTraceState -Snapshot $finalSnapshot
            arenaReplaySave = $arenaSave
            startRecording = Convert-AutomationResult $startRecording
            createReplaySave = Convert-AutomationResult $createSave
            pause = Convert-AutomationResult $pause
            runtimeDiagnostics = [ordered]@{
                controlFault = [string]$runtime.State.Fields['controlFault']
                lastControlAbortReason = [string]$runtime.State.Fields[
                    'lastControlAbortReason']
                lastPlaybackFault = [string]$runtime.State.Fields[
                    'lastPlaybackFault']
                pauseLeaseReassertionCount = [long][string]$runtime.State.Fields[
                    'pauseLeaseReassertionCount']
                ignoredFocusLossCount = [long][string]$runtime.State.Fields[
                    'ignoredFocusLossCount']
            }
            stopRecording = Convert-AutomationResult $stopRecording
            stopRecordingAttempts = $stop.attempts
            recordedFullMovieId = $recordedMovieId
            recordedJournalPrefixTicks =
                [long]$candidate.JournalPrefixTicks
            fullMoviePath = $fullMoviePath
            fullMovieId = [string]$validate.Data['branchMovieId']
            expandedTicks = [long]$validate.Data['expandedTicks']
            validation = Convert-AutomationResult $validate
            release = Convert-AutomationResult $release
        }
        $result |
            ConvertTo-Json -Depth 100 |
            Set-Content `
                -LiteralPath (Join-Path $CaseDirectory 'false-knight-result.json') `
                -Encoding utf8NoBOM

        return $result
    }
    finally {
        if ($null -eq $stopRecording) {
            try {
                $cleanupState = $Client.GetSemanticStateAsync(
                        [Threading.CancellationToken]::None
                    ).GetAwaiter().GetResult()
                [void](Invoke-SdkCommand `
                    -Client $Client `
                    -CommandId 'stopRecording' `
                    -Scope 'control.recording' `
                    -LeaseId $leaseId `
                    -ExpectedRuntimeMode $cleanupState.State.RuntimeMode `
                    -ExpectedMovieTick $cleanupState.State.MovieTick `
                    -AllowFailure)
            }
            catch {
            }
        }
        if (-not $released) {
            try {
                [void](Release-SdkLease `
                    -Client $Client `
                    -Scope 'control.input' `
                    -LeaseId $leaseId)
            }
            catch {
            }
        }
    }
}

function Test-FalseKnightAdaptivePolicy {
    function New-PolicyFixtureSnapshot($Values) {
        # Synthetic fixture geometry only; production uses native collider samples.
        $fixture = $Values.Clone()
        $fixture.HeroColliderCenterX = [single]$fixture.HeroX
        $fixture.HeroColliderCenterY = [single]($fixture.HeroY + 0.9)
        $fixture.HeroColliderExtentX = [single]0.55
        $fixture.HeroColliderExtentY = [single]0.9
        $headPath = $fixture.BossObjectPath + '/Head-0'
        $head = @{
            objectName = 'Head'; objectPath = $headPath
            centerX = $fixture.BossX + $(if ($fixture.BossFacing -eq 'right') { 2.1 } else { -2.1 })
            centerY = $fixture.HeroColliderCenterY; extentX = 1.04; extentY = 1.11
            boundsAvailable = $true; enabled = $true; activeInHierarchy = $true
        }
        $fixture.BossCollidersJson = ConvertTo-Json -InputObject @(
            @($fixture.BossCollidersJson | ConvertFrom-Json) + @($head)) -Compress
        $fixture.BossHealthManagersJson = ConvertTo-Json -InputObject @(@{
            objectPath = $headPath; activeInHierarchy = $true; enabled = $true
            hp = 50; dead = $false; invincible = $false; invincibleFromDirection = 0
        }) -Compress
        return [pscustomobject]$fixture
    }
    $base = @{
        MovieTick = 100L
        SceneEpoch = 3
        Scene = 'GG_False_Knight'
        EncounterActive = $true
        EncounterScene = 'GG_False_Knight'
        EncounterLevel = 0
        EncounterDifficulty = 'Attuned'
        BossesAlive = 1
        BossAvailable = $true
        BossObjectPath = 'Battle Scene-0/False Knight New-0'
        BossHp = 260
        BossDead = $false
        BossState = 'Idle'
        BossRecentEvent = 'FINISHED'
        BossFacing = 'right'
        BossCollidersJson = '[]'
        HazardsJson = '[]'
        HeroDamageSequence = 0L
        HeroDamageSourcePath = ''
        HeroDamageSourceName = ''
        HeroDamageSide = ''
        HeroDamageAmount = 0
        HeroDamageHazardType = 0
        BossX = [single]35
        BossY = [single]31.6
        BossVelocityX = [single]0
        BossVelocityY = [single]0
        BossColliderCenterX = [single]35
        BossColliderCenterY = [single]31
        BossColliderExtentX = [single]2
        BossColliderExtentY = [single]3
        DeltaX = [single]10
        DeltaY = [single]4
        Distance = [single]10.8
        BossSide = 'right'
        ContactOverlap = $false
        HeroX = [single]25
        HeroY = [single]27.4
        HeroVelocityX = [single]0
        HeroVelocityY = [single]0
        HeroActorState = 'idle'
        HeroAnimation = 'Idle'
        HeroOnGround = $true
        HeroJumping = $false
        HeroFalling = $false
        HeroDashing = $false
        HeroShadowDashing = $false
        HeroShadowDashReady = $true
        HeroShadowDashCooldown = [single]0
        HeroAttacking = $false
        HeroAttackCooldown = [single]0
        HeroAcceptingInput = $true
        HeroHealth = 9
        HeroMaxHealth = 9
        HeroSoul = 0
        BossDeathObserved = $false
        BossesDeadObserved = $false
        SceneCompleteObserved = $false
        TerminalScene = 'GG_False_Knight'
        TerminalLevel = 0
        TerminalDifficulty = 'Attuned'
    }
    $state = New-FalseKnightControllerState
    $right = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $base) `
        -ControllerState $state
    if ($right.RuleId -ne 'PRESSURE_DASH_APPROACH' `
            -or $right.Hold -ne 'right,dash') {
        throw 'Policy self-test failed for an offensive dash toward a distant boss on the right.'
    }

    $leftValues = $base.Clone()
    $leftValues.BossSide = 'left'
    $leftValues.DeltaX = [single]-5
    $leftValues.BossX = [single]20
    $leftValues.BossColliderCenterX = [single]20
    $left = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $leftValues) `
        -ControllerState (New-FalseKnightControllerState)
    if ($left.RuleId -ne 'PRESSURE_APPROACH' `
            -or $left.Hold -ne 'left' `
            -or $left.Ticks -ne 2) {
        throw 'Policy self-test failed for pressure toward a boss on the left.'
    }

    $attackValues = $base.Clone()
    $attackValues.BossX = [single]29
    $attackValues.BossColliderCenterX = [single]29
    $attackValues.DeltaX = [single]4
    $attack = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $attackValues) `
        -ControllerState (New-FalseKnightControllerState)
    if ($attack.RuleId -ne 'PRESSURE_ATTACK_HORIZONTAL' `
            -or $attack.Hold -ne 'right,attack') {
        throw 'Policy self-test failed for a live in-range horizontal attack.'
    }

    $recoveringSlash = $attackValues.Clone()
    $recoveringSlash.HeroAttacking = $true
    $readySlash = Select-FalseKnightDecision -Snapshot (New-PolicyFixtureSnapshot $recoveringSlash) -ControllerState (New-FalseKnightControllerState)
    if ($readySlash.RuleId -ne 'PRESSURE_ATTACK_HORIZONTAL') {
        throw 'A completed native cooldown must permit a new slash during the previous animation tail.'
    }
    $recoveringSlash.HeroAttackCooldown = [single]0.12
    $coolingSlash = Select-FalseKnightDecision -Snapshot (New-PolicyFixtureSnapshot $recoveringSlash) -ControllerState (New-FalseKnightControllerState)
    if ($coolingSlash.Hold -match 'attack') { throw 'Positive native cooldown must not trigger an attack.' }

    $airAttackValues = $attackValues.Clone()
    $airAttackValues.BossState = 'JA Fall'
    $airAttackValues.BossY = [single]34
    $airAttackValues.BossColliderCenterY = [single]31
    $airAttackValues.BossVelocityY = [single]-4
    $airAttackValues.HeroY = [single]29.5
    $airAttackValues.HeroOnGround = $false
    $airAttackValues.HeroFalling = $true
    $airAttack = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $airAttackValues) `
        -ControllerState (New-FalseKnightControllerState)
    if ($airAttack.RuleId -ne 'PRESSURE_ATTACK_HORIZONTAL' `
            -or $airAttack.Hold -ne 'right,attack') {
        throw 'Policy self-test failed: a mobile boss inside nail geometry must be attacked, not fled from.'
    }

    $rageValues = $base.Clone()
    $rageValues.BossState = 'Rage'
    $rage = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $rageValues) `
        -ControllerState (New-FalseKnightControllerState)
    if ($rage.RuleId -ne 'PRESSURE_DASH_APPROACH' `
            -or $rage.Hold -ne 'right,dash') {
        throw 'Policy self-test failed: a rage state without an imminent hit must be pressured.'
    }

    $descendingValues = $base.Clone()
    $descendingValues.BossState = 'Fall 2'
    $descendingValues.HeroX = [single]29.1
    $descendingValues.HeroY = [single]32.47
    $descendingValues.HeroOnGround = $false
    $descendingValues.HeroFalling = $true
    $descendingValues.BossX = [single]26.77
    $descendingValues.BossY = [single]36.88
    $descendingValues.BossColliderCenterX = [single]26.8
    $descendingValues.BossColliderCenterY = [single]33.5
    $descendingValues.BossColliderExtentX = [single]1.68
    $descendingValues.BossColliderExtentY = [single]2.21
    $descendingValues.BossVelocityX = [single]16.09
    $descendingValues.BossVelocityY = [single]-30.41
    $descendingValues.BossSide = 'left'
    $descending = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $descendingValues) `
        -ControllerState (New-FalseKnightControllerState)
    if ($descending.RuleId `
            -ne 'MICRO_DODGE_BOSS_DESCENDINGBODY_DASH' `
            -or $descending.Hold -ne 'left,dash' `
            -or $descending.Ticks -ne 1) {
        throw 'Policy self-test failed for a one-frame projected landing cross-dash.'
    }

    $unshieldedDescendingValues = $descendingValues.Clone()
    $unshieldedDescendingValues.HeroShadowDashReady = $false
    $unshieldedDescending = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $unshieldedDescendingValues) `
        -ControllerState (New-FalseKnightControllerState)
    if ($unshieldedDescending.Hold -ne 'right,dash') {
        throw 'Policy self-test failed: ordinary dash must not cross a descending boss body.'
    }

    $earlyLandingValues = $base.Clone()
    $earlyLandingValues.BossState = 'Fall'
    $earlyLandingValues.HeroX = [single]30.75
    $earlyLandingValues.HeroY = [single]28.73
    $earlyLandingValues.HeroVelocityY = [single]12.86
    $earlyLandingValues.HeroOnGround = $false
    $earlyLandingValues.HeroJumping = $true
    $earlyLandingValues.BossX = [single]30.52
    $earlyLandingValues.BossColliderCenterX = [single]30.49
    $earlyLandingValues.BossColliderCenterY = [single]34.63
    $earlyLandingValues.BossColliderExtentX = [single]1.68
    $earlyLandingValues.BossColliderExtentY = [single]2.21
    $earlyLandingValues.BossVelocityX = [single]-5
    $earlyLandingValues.BossVelocityY = [single]-13.84
    $earlyLandingValues.BossSide = 'left'
    $earlyLanding = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $earlyLandingValues) `
        -ControllerState (New-FalseKnightControllerState)
    if ($earlyLanding.RuleId `
            -ne 'MICRO_DODGE_BOSS_DESCENDINGBODY_DASH' `
            -or $earlyLanding.Hold -ne 'right,dash') {
        throw 'Policy self-test failed: projected contact must preempt a tempting upward attack several frames early.'
    }

    $cooldownState = New-FalseKnightControllerState
    $cooldownState.LastDashTick = 90L
    $cooldownState.LastJumpTick = 90L
    $landingFallback = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $earlyLandingValues) `
        -ControllerState $cooldownState
    if ($landingFallback.RuleId `
            -ne 'MICRO_DODGE_BOSS_DESCENDINGBODY_STEP' `
            -or $landingFallback.Hold -ne 'right' `
            -or $landingFallback.Ticks -ne 3) {
        throw 'Policy self-test failed: cooldown fallback must hold long enough to produce real displacement.'
    }

    $farHitterValues = $attackValues.Clone()
    $farHitterValues.BossObjectPath = 'boss'
    $farHitterValues.HazardsJson = @(
        [ordered]@{
            objectPath = 'boss/Hitter'
            objectName = 'Hitter'
            damageDealt = 2
            layerName = 'Enemy Attack'
            mainState = 'Idle'
            distance = 8.0
            deltaX = -8.0
            deltaY = 0.0
            velocityY = 0.0
            overlapHero = $false
        }
    ) | ConvertTo-Json -Compress
    $farHitter = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $farHitterValues) `
        -ControllerState (New-FalseKnightControllerState)
    if ($farHitter.RuleId -ne 'PRESSURE_ATTACK_HORIZONTAL') {
        throw 'Policy self-test failed: a far Hitter must not suppress a valid attack.'
    }

    $closeHitterValues = $farHitterValues.Clone()
    $closeHitterValues.HazardsJson = @(
        [ordered]@{
            objectPath = 'boss/Hitter'
            objectName = 'Hitter'
            damageDealt = 2
            layerName = 'Enemy Attack'
            mainState = 'Idle'
            distance = 6.28
            deltaX = -4.67
            deltaY = 4.19
            velocityY = 0.0
            overlapHero = $false
        }
    ) | ConvertTo-Json -Compress
    $closeHitter = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $closeHitterValues) `
        -ControllerState (New-FalseKnightControllerState)
    if ($closeHitter.RuleId -ne 'MICRO_DODGE_HITTER_DASH' `
            -or $closeHitter.Hold -ne 'right,dash' `
            -or $closeHitter.Ticks -ne 1) {
        throw 'Policy self-test failed for a measured one-frame Hitter dodge.'
    }

    $hitterColliderValues = $base.Clone()
    $hitterColliderValues.BossCollidersJson = @(
        [ordered]@{
            objectPath = 'boss/Hitter'
            objectName = 'Hitter'
            activeInHierarchy = $true
            enabled = $true
            centerX = 25.4
            centerY = 31.5
            extentX = 3.7
            extentY = 1.69
            deltaX = 0.4
            deltaY = 4.09
            distance = 4.11
            overlapHero = $false
        }
    ) | ConvertTo-Json -Compress
    $hitterCollider = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $hitterColliderValues) `
        -ControllerState (New-FalseKnightControllerState)
    if ($hitterCollider.RuleId -ne 'MICRO_DODGE_HITTERCOLLIDER_DASH' `
            -or $hitterCollider.Hold -ne 'right,dash' `
            -or $hitterCollider.Ticks -ne 1) {
        throw 'Policy self-test failed: the animated Hitter collider must be crossed toward the boss before its transform entry drops.'
    }

    $normalDashValues = $hitterColliderValues.Clone()
    $normalDashValues.HeroShadowDashReady = $false
    $normalDashValues.HeroShadowDashCooldown = [single]0.9
    $normalDash = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $normalDashValues) `
        -ControllerState (New-FalseKnightControllerState)
    if ($normalDash.RuleId -ne 'MICRO_DODGE_HITTERCOLLIDER_DASH' `
            -or $normalDash.Hold -ne 'left,dash') {
        throw 'Policy self-test failed: a normal dash must clear outward instead of pretending to have shade-dash invulnerability.'
    }

    $barrelValues = $base.Clone()
    $barrelValues.HazardsJson = @(
        [ordered]@{
            objectPath = 'Battle Scene-0/Falling Barrel-0'
            objectName = 'Falling Barrel(Clone)'
            damageDealt = 2
            layerName = 'Enemy Attack'
            mainState = 'Fall'
            distance = 2.0
            deltaX = 1.0
            deltaY = 3.0
            velocityY = -5.0
            overlapHero = $false
        }
    ) | ConvertTo-Json -Compress
    $barrel = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $barrelValues) `
        -ControllerState (New-FalseKnightControllerState)
    if ($barrel.RuleId -ne 'MICRO_DODGE_FALLINGOBJECT_DASH' `
            -or $barrel.Hold -ne 'left,dash' `
            -or $barrel.Ticks -ne 1) {
        throw 'Policy self-test failed for a one-frame falling-object dodge.'
    }

    $farBarrelValues = $base.Clone()
    $farBarrelValues.HazardsJson = @(
        [ordered]@{
            objectPath = 'Battle Scene-0/Falling Barrel-0'
            objectName = 'Falling Barrel(Clone)'
            damageDealt = 2
            layerName = 'Enemy Attack'
            mainState = 'Fall'
            distance = 5.0
            deltaX = 4.0
            deltaY = 3.0
            velocityY = -5.0
            overlapHero = $false
        }
    ) | ConvertTo-Json -Compress
    $farBarrel = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $farBarrelValues) `
        -ControllerState (New-FalseKnightControllerState)
    if ($farBarrel.RuleId -ne 'PRESSURE_DASH_APPROACH') {
        throw 'Policy self-test failed: a falling object outside the intercept column must be ignored.'
    }

    $shockwaveValues = $base.Clone()
    $shockwaveValues.HazardsJson = @(
        [ordered]@{
            objectPath = 'Battle Scene-0/Shockwave-0'
            objectName = 'Shockwave Spurt(Clone)'
            damageDealt = 1
            layerName = 'Enemy Attack'
            mainState = 'Run'
            distance = 4.0
            deltaX = -4.0
            deltaY = 0.2
            velocityY = 0.0
            overlapHero = $false
        }
    ) | ConvertTo-Json -Compress
    $shockwave = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $shockwaveValues) `
        -ControllerState (New-FalseKnightControllerState)
    if ($shockwave.RuleId -ne 'MICRO_DODGE_SHOCKWAVE_JUMP' `
            -or $shockwave.Hold -ne 'right,jump' `
            -or $shockwave.Ticks -ne 2) {
        throw 'Policy self-test failed: a nearby shockwave must be jumped toward the boss.'
    }

    $overlapValues = $base.Clone()
    $overlapValues.BossSide = 'overlap'
    $overlapValues.BossX = [single]25
    $overlapValues.BossColliderCenterX = [single]25
    $overlapValues.DeltaX = [single]0
    $overlapValues.ContactOverlap = $true
    $overlap = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $overlapValues) `
        -ControllerState (New-FalseKnightControllerState)
    if ($overlap.RuleId -ne 'MICRO_DODGE_BOSS_CONTACTOVERLAP_DASH' `
            -or $overlap.Hold -ne 'left,dash' `
            -or $overlap.Ticks -ne 1) {
        throw 'Policy self-test failed for the one-frame overlap fallback.'
    }

    $upValues = $base.Clone()
    $upValues.BossX = [single]25.3
    $upValues.BossColliderCenterX = [single]25.3
    $upValues.BossColliderCenterY = [single]32
    $upValues.BossColliderExtentX = [single]1
    $upValues.BossColliderExtentY = [single]1
    $upValues.BossState = 'Rise'
    $up = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $upValues) `
        -ControllerState (New-FalseKnightControllerState)
    if ($up.RuleId -ne 'PRESSURE_ATTACK_UPWARD' `
            -or $up.Hold -ne 'right,up,attack') {
        throw 'Policy self-test failed for a collider-driven upward attack.'
    }

    $downValues = $base.Clone()
    $downValues.HeroY = [single]32
    $downValues.HeroOnGround = $false
    $downValues.HeroFalling = $true
    $downValues.BossX = [single]25.3
    $downValues.BossColliderCenterX = [single]25.3
    $downValues.BossColliderCenterY = [single]29
    $downValues.BossColliderExtentX = [single]1
    $downValues.BossColliderExtentY = [single]1
    $downValues.BossState = 'Idle'
    $down = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $downValues) `
        -ControllerState (New-FalseKnightControllerState)
    if ($down.RuleId -ne 'PRESSURE_ATTACK_DOWNWARD' `
            -or $down.Hold -ne 'right,down,attack') {
        throw 'Policy self-test failed for a collider-driven pogo attack.'
    }

    $crossValues = $base.Clone()
    $crossValues.BossState = 'Opened'
    $crossValues.BossFacing = 'right'
    $cross = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $crossValues) `
        -ControllerState (New-FalseKnightControllerState)
    if ($cross.RuleId -ne 'STAGGER_CROSS_TO_EXPOSED_SIDE' `
            -or $cross.Hold -ne 'right,jump') {
        throw 'Policy self-test failed for crossing to the live exposed side.'
    }

    $healValues = $base.Clone()
    $healValues.BossState = 'Opened'
    $healValues.BossFacing = 'left'
    $healValues.HeroX = [single]30
    $healValues.HeroHealth = 5
    $healValues.HeroSoul = 33
    $heal = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $healValues) `
        -ControllerState (New-FalseKnightControllerState)
    if ($heal.RuleId -ne 'STAGGER_HEAL_ONCE' `
            -or $heal.Hold -ne 'cast' `
            -or $heal.Ticks -ne 2) {
        throw 'Policy self-test failed for one bounded heal inside a broken-shell-only window.'
    }

    $focusValues = $healValues.Clone()
    $focusValues.HeroAnimation = 'Focus'
    $focusValues.HeroAcceptingInput = $false
    $focusValues.HeroSoul = 16
    $focusState = New-FalseKnightControllerState
    $focusState.FocusStartHealth = 5
    $focusState.LastHealTick = 98L
    $focus = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $focusValues) `
        -ControllerState $focusState
    if ($focus.RuleId -ne 'STAGGER_HEAL_HOLD' `
            -or $focus.Hold -ne 'cast' `
            -or $focus.Ticks -ne 2) {
        throw 'Policy self-test failed: an incomplete focus must remain continuously held in two-tick observed batches.'
    }

    $unsafeFocusValues = $focusValues.Clone()
    $unsafeFocusValues.HazardsJson = $barrelValues.HazardsJson
    $unsafeFocus = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $unsafeFocusValues) -ControllerState $focusState
    if ($unsafeFocus.Hold -match 'cast') {
        throw 'Policy self-test failed: falling hazards must interrupt focus hold during knockdown.'
    }
    $invalidHazards = $base.Clone()
    $invalidHazards.HazardsJson = '{invalid'
    $refused = $false
    try {
        $null = Get-FalseKnightImminentHazardThreat -Snapshot (New-PolicyFixtureSnapshot $invalidHazards)
    } catch { $refused = $true }
    if (-not $refused) { throw 'Malformed hazard observation must fail closed.' }

    $healedState = New-FalseKnightControllerState
    $healedState.LastHealTick = 0L
    $postHeal = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $healValues) `
        -ControllerState $healedState
    if ($postHeal.RuleId -ne 'STAGGER_SLASH_EXPOSED') {
        throw 'Policy self-test failed: a second focus in the same knockdown must be replaced by immediate offense.'
    }

    foreach ($finalHeadState in @('Opened 2', 'Hit 2')) {
        $finalHeadValues = $healValues.Clone()
        $finalHeadValues.BossState = $finalHeadState
        $finalHead = Select-FalseKnightDecision `
            -Snapshot (New-PolicyFixtureSnapshot $finalHeadValues) `
            -ControllerState $healedState
        if ($finalHead.RuleId -ne 'STAGGER_SLASH_EXPOSED') {
            throw "Policy self-test failed: final head state $finalHeadState must use exposed-target logic."
        }
    }

    $heroDescentValues = $base.Clone()
    $heroDescentValues.HeroX = [single]29.1
    $heroDescentValues.HeroY = [single]33.0
    $heroDescentValues.HeroVelocityY = [single]-20
    $heroDescentValues.HeroOnGround = $false
    $heroDescentValues.HeroFalling = $true
    $heroDescentValues.BossX = [single]29
    $heroDescentValues.BossY = [single]31.6
    $heroDescentValues.BossColliderCenterX = [single]29
    $heroDescentValues.BossColliderCenterY = [single]28.5
    $heroDescentValues.BossColliderExtentX = [single]2.18
    $heroDescentValues.BossColliderExtentY = [single]2.52
    $heroDescent = Select-FalseKnightDecision `
        -Snapshot (New-PolicyFixtureSnapshot $heroDescentValues) `
        -ControllerState (New-FalseKnightControllerState)
    if ($heroDescent.RuleId `
            -ne 'MICRO_DODGE_BOSS_HERODESCENDINGBODY_DASH' `
            -or $heroDescent.Hold -ne 'right,dash') {
        throw 'Policy self-test failed: a missed pogo with certain body contact must use one outward dash instead of falling in place.'
    }

    $terminalValues = $base.Clone()
    $terminalValues.BossDeathObserved = $true
    $terminalValues.BossesDeadObserved = $true
    $terminalValues.SceneCompleteObserved = $false
    if (-not (Test-FalseKnightTerminalSuccess `
            -Snapshot (New-PolicyFixtureSnapshot $terminalValues))) {
        throw 'Policy self-test failed for the strict terminal conjunction.'
    }

    $measuredHead = New-PolicyFixtureSnapshot $healValues
    $headCatalog = @($measuredHead.BossCollidersJson | ConvertFrom-Json)
    $headCatalog[0].centerX = 32.75
    $measuredHead.BossCollidersJson = ConvertTo-Json -InputObject $headCatalog -Compress
    $target = Get-FalseKnightExposedTarget $measuredHead
    if ($target.X -ne 32.75 -or $target.Direction -ne 'right' -or -not $target.CanAttemptSlash) {
        throw 'Head targeting must use its measured position, not the facing-derived offset.'
    }
    $headCatalog[0].centerY = 226
    $measuredHead.BossCollidersJson = ConvertTo-Json -InputObject $headCatalog -Compress
    if ((Get-FalseKnightExposedTarget $measuredHead).CanAttemptSlash) {
        throw 'Hidden Head height must prevent slash attempts.'
    }
    $headCatalog[0].centerY = $measuredHead.HeroColliderCenterY
    $measuredHead.BossCollidersJson = ConvertTo-Json -InputObject $headCatalog -Compress
    $healthCatalog = @($measuredHead.BossHealthManagersJson | ConvertFrom-Json)
    $healthCatalog[0].invincible = $true
    $measuredHead.BossHealthManagersJson = ConvertTo-Json -InputObject $healthCatalog -Compress
    if ((Get-FalseKnightExposedTarget $measuredHead).CanAttemptSlash) {
        throw 'Invincible Head must prevent slash attempts.'
    }
    $measuredHead.BossHealthManagersJson = '[]'
    $refusedHead = $false
    try { $null = Get-FalseKnightExposedTarget $measuredHead } catch { $refusedHead = $true }
    if (-not $refusedHead) { throw 'Missing Head health must not fall back to the body HP.' }

    $native = @{
        primaryHeroCollider = $true; boundsAvailable = $true; enabled = $true
        activeInHierarchy = $true; isTrigger = $false
        centerX = 12.25; centerY = 7.5; extentX = 0.4; extentY = 1.2
    }
    $parsedCollider = Get-FalseKnightLiveHeroCollider -Json ($native | ConvertTo-Json -Compress)
    $nativeSnapshot = New-PolicyFixtureSnapshot $base
    $nativeSnapshot.HeroColliderCenterX = $parsedCollider.centerX
    $nativeSnapshot.HeroColliderCenterY = $parsedCollider.centerY
    $nativeSnapshot.HeroColliderExtentX = $parsedCollider.extentX
    $nativeSnapshot.HeroColliderExtentY = $parsedCollider.extentY
    $nativeGeometry = Get-FalseKnightCombatGeometry $nativeSnapshot
    if ([Math]::Abs($nativeGeometry.HeroCenterX - 12.25) -gt 0.0001 -or
            [Math]::Abs($nativeGeometry.HeroCenterY - 7.5) -gt 0.0001 -or
            [Math]::Abs($nativeGeometry.EdgeGapX - 20.35) -gt 0.0001) {
        throw 'Native collider geometry must override all Hero transform/size assumptions.'
    }
    $unavailable = $native.Clone(); $unavailable.boundsAvailable = $false
    $invalidExtent = $native.Clone(); $invalidExtent.extentX = 0
    $missingCenter = $native.Clone(); $missingCenter.Remove('centerY')
    foreach ($badJson in @('[]', '{invalid',
            (ConvertTo-Json -InputObject @($native, $native) -Compress),
            ($unavailable | ConvertTo-Json -Compress),
            ($invalidExtent | ConvertTo-Json -Compress),
            ($missingCenter | ConvertTo-Json -Compress))) {
        $rejected = $false
        try { $null = Get-FalseKnightLiveHeroCollider -Json $badJson }
        catch { $rejected = $true }
        if (-not $rejected) { throw 'Invalid primary Hero collider must be rejected.' }
    }

    return [pscustomobject]@{
        Verdict = 'FALSE_KNIGHT_POLICY_SELF_TEST_PASS'
        Cases = 42
    }
}
