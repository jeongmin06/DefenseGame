using Godot;
using DefenseGame.Client.Data;
using DefenseGame.Client.Network;
using DefenseGame.Client.Skills;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DefenseGame.Client.Combat;

public partial class StageOne : Node2D
{
    [Export] public StageDefinition Definition { get; set; } = null!;
    [Export] public PlayerSkillDefaults Defaults { get; set; } = null!;
    public SkillLoadout? Loadout { get; private set; }
    public RangedAttackSettings ArcherAttackSettings { get; private set; } = new();
    public int RemainingArchers => RemainingFor(DeploymentGrid.PlacementType.Ranged);
    public string SelectedDeploymentCharacterId => _selectedCharacterId;
    public float SecondsUntilFirstWave => _firstWaveCountdown;
    public float DeploymentPoints => _deploymentPoints;
    public int MaxDeploymentPoints => Definition.MaxDeploymentPoints;
    public bool IsChoosingDeploymentFacing => _grid is not null && _grid.IsChoosingFacing;
    public Vector2I PendingDeploymentCell => _pendingDeploymentCell;
    public string NextArcherCharacterId => _deploymentOrder.FirstOrDefault(deployment =>
        deployment.Profile.Unit?.Role == UnitRole.Ranged && _availableDeployments.ContainsKey(deployment.Profile.CharacterId))
        ?.Profile.CharacterId ?? "";
    public string[] PlacedArcherCharacterIds => _placedArcherCharacterIds.ToArray();
    public string SelectedRetreatCharacterId => _selectedActiveCharacterId;
    public bool IsBattlePaused => _battlePaused;
    public int BattleSpeed => _battleSpeed;
    public int CurrentWaveNumber => Mathf.Max(0, _waveIndex + 1);
    public int SpawnedInCurrentWave => _spawnedInWave;
    public bool CanStartFirstWaveEarly => !_battleEnded && _waveIndex < 0 && _firstWaveCountdown > 0;
    public string ResultSyncStatusCode { get; private set; } = "Idle";
    private readonly StageProgressServerClient _progressClient = new();
    private readonly System.Threading.CancellationTokenSource _progressCancellation = new();
    private DeploymentGrid _grid = null!;
    private Vector2I[] _pathCorners = [];
    private WaveSpec[] _waves = [];
    private readonly List<UnitDeployment> _deploymentOrder = new();
    private readonly Dictionary<string, UnitDeployment> _availableDeployments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActiveDeployment> _activeDeployments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _redeployRemaining = new(StringComparer.Ordinal);
    private readonly List<string> _placedArcherCharacterIds = new();
    private readonly List<string> _placedWarriorCharacterIds = new();
    private readonly List<string> _placedHealerCharacterIds = new();
    private string _selectedCharacterId = "";
    private string _pendingDeploymentCharacterId = "";
    private string _selectedActiveCharacterId = "";
    private Vector2I _pendingDeploymentCell = new(-1, -1);
    private float _firstWaveDelay;
    private float _firstWaveCountdown;
    private float _waveGap;
    private float _deploymentPoints;
    private bool _combatTimelineStarted;
    private bool _battlePaused;
    private int _battleSpeed = 1;
    private int _waveScheduleGeneration;
    private int _lastDisplayedCountdownSecond = -1;
    private int _lastDisplayedDeploymentPoints = -1;
    private DeploymentGrid.PlacementType _selectedType;

    private int _waveIndex = -1;
    private int _spawnedInWave;
    private int _activeEnemies;
    private int _defeatedEnemies;
    private int _escapedEnemies;
    private int _baseHealth;
    private bool _allWavesSpawned;
    private bool _battleEnded;
    private bool _battleVictory;
    private bool _resultSyncBusy;

    private Path2D _enemyPath = null!;
    private Timer _spawnTimer = null!;
    private UI.CombatHud _hud = null!;

    public string[] PlacedWarriorCharacterIds => _placedWarriorCharacterIds.ToArray();
    public string[] PlacedHealerCharacterIds => _placedHealerCharacterIds.ToArray();

    public override void _Ready()
    {
        RestoreSimulationDefaults();
        if (StageSelectionState.SelectedStage is not null)
            Definition = StageSelectionState.SelectedStage;
        BuildDeploymentQueues();
        UnitDeployment? firstArcher = _deploymentOrder.FirstOrDefault(deployment => deployment.Profile.Unit?.Role == UnitRole.Ranged);
        Loadout = firstArcher?.Loadout ?? StageSelectionState.SelectedLoadout;
        ArcherAttackSettings = firstArcher?.RangedSettings
            ?? new RangedSkillEffectComposer().Compose(Loadout);

        _enemyPath = GetNode<Path2D>("EnemyPath");
        _spawnTimer = GetNode<Timer>("SpawnTimer");
        _hud = GetNode<UI.CombatHud>("HUD");

        _baseHealth = Definition.BaseHealth;
        _firstWaveDelay = Definition.FirstWaveDelay;
        _waveGap = Definition.WaveGap;
        _deploymentPoints = Definition.InitialDeploymentPoints;
        _pathCorners = System.Linq.Enumerable.ToArray(Definition.PathCorners);
        _waves = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(Definition.Waves,
            w => new WaveSpec(w.Enemy, w.Count, w.HealthOverride, w.SpeedOverride, w.Interval)));
        _grid = new DeploymentGrid { Name = "DeploymentGrid" };
        AddChild(_grid);
        _grid.Configure(Definition);
        _grid.CellSelected += OnCellSelected;
        _grid.FacingSelected += OnFacingSelected;
        _grid.FacingSelectionCanceled += OnFacingSelectionCanceled;
        _hud.CharacterSelected += characterId => SelectDeploymentCharacter(characterId);
        _hud.CharacterDragMoved += PreviewCharacterDrag;
        _hud.CharacterDragEnded += _grid.ClearDragRangePreview;
        _hud.CharacterDropped += (characterId, globalPosition) =>
            TryDeployCharacterAtGlobalPosition(characterId, globalPosition);
        _hud.RetreatRequested += characterId => RetreatCharacter(characterId);
        _hud.PauseRequested += () => ToggleBattlePause();
        _hud.BattleSpeedRequested += () => ToggleBattleSpeed();
        _hud.FirstWaveStartRequested += () => StartFirstWaveEarly();
        _hud.RetryRequested += RestartStage;
        _hud.StageListRequested += ReturnToStageList;
        _hud.ResultSyncRetryRequested += RetryBattleResultSync;
        _hud.UpdateStage(Definition.DisplayName, Definition.Id);
        _hud.ConfigureDeploymentCards(_deploymentOrder.Select(deployment => deployment.Profile));
        SelectNextAffordableCharacter();
        BuildEnemyPath();
        _spawnTimer.Timeout += SpawnEnemy;
        _combatTimelineStarted = true;
        _firstWaveCountdown = _firstWaveDelay;
        ScheduleNextWave(_firstWaveDelay);
        UpdateHud();
        UpdatePlacementHud();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_combatTimelineStarted || _battleEnded) return;

        RegenerateDeploymentPoints((float)delta);
        TickRedeploymentCooldowns((float)delta);
        RefreshUnitSelectionHud();
        if (_waveIndex >= 0) return;

        _firstWaveCountdown = Mathf.Max(0.0f, _firstWaveCountdown - (float)delta);
        int displayedSecond = Mathf.CeilToInt(_firstWaveCountdown);
        if (displayedSecond == _lastDisplayedCountdownSecond) return;

        _lastDisplayedCountdownSecond = displayedSecond;
        UpdateHud();
    }

    public void RestartStage()
    {
        RestoreSimulationDefaults();
        GetTree().ReloadCurrentScene();
    }

    public void ReturnToStageList()
    {
        RestoreSimulationDefaults();
        StageSelectionState.ClearStage();
        GetTree().ChangeSceneToFile("res://scenes/stage_select.tscn");
    }

    public bool ToggleBattlePause()
    {
        if (_battleEnded) return false;
        bool pause = !_battlePaused;
        if (pause)
        {
            _hud.CancelCharacterDrag();
            _grid.SetPlacementEnabled(false);
        }
        _battlePaused = pause;
        GetTree().Paused = _battlePaused;
        if (!pause) _grid.SetPlacementEnabled(true);
        UpdatePlacementHud();
        _hud.UpdateBattleControls(_battlePaused, _battleSpeed, CanStartFirstWaveEarly);
        return true;
    }

    public int ToggleBattleSpeed()
    {
        if (_battleEnded) return _battleSpeed;
        _battleSpeed = _battleSpeed == 1 ? 2 : 1;
        Engine.TimeScale = _battleSpeed;
        _hud.UpdateBattleControls(_battlePaused, _battleSpeed, CanStartFirstWaveEarly);
        return _battleSpeed;
    }

    public bool StartFirstWaveEarly()
    {
        if (_battlePaused || !CanStartFirstWaveEarly) return false;
        _waveScheduleGeneration++;
        _firstWaveCountdown = 0.0f;
        StartNextWave();
        return true;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, 1280, 720), new Color("5b843e"));
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (_battleEnded || _battlePaused || _grid.IsChoosingFacing || _hud.IsDraggingCharacter
            || input is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click)
            return;

        Vector2I cell = _grid.GlobalToCell(click.GlobalPosition);
        if (SelectDeployedUnitAtCell(cell))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_grid.ContainsCell(cell)) ClearActiveUnitSelection();
    }

    private void BuildEnemyPath()
    {
        var curve = new Curve2D();
        foreach (Vector2I cell in _pathCorners)
        {
            curve.AddPoint(_enemyPath.ToLocal(_grid.CellToGlobal(cell)));
        }
        _enemyPath.Curve = curve;
    }

    public bool SelectPlacementType(DeploymentGrid.PlacementType type)
    {
        if (_battleEnded) return false;
        UnitDeployment? deployment = _deploymentOrder.FirstOrDefault(candidate =>
            candidate.Profile.Unit is not null && ToPlacementType(candidate.Profile.Unit.Role) == type
            && _availableDeployments.ContainsKey(candidate.Profile.CharacterId));
        if (deployment is null) return false;
        return SelectDeploymentCharacter(deployment.Profile.CharacterId);
    }

    public bool SelectDeploymentCharacter(string characterId)
    {
        if (_battleEnded || _battlePaused
            || !_availableDeployments.TryGetValue(characterId, out UnitDeployment? deployment)
            || deployment.Profile.Unit is null || !CanAfford(deployment.Profile.Unit)) return false;
        if (_grid.IsChoosingFacing) _grid.CancelFacingSelection();
        ClearActiveUnitSelection();
        _selectedCharacterId = characterId;
        _selectedType = ToPlacementType(deployment.Profile.Unit.Role);
        _grid.SetPlacementType(_selectedType);
        UpdatePlacementHud();
        return true;
    }

    public bool TryDeployCharacterAtGlobalPosition(string characterId, Vector2 globalPosition)
    {
        if (!SelectDeploymentCharacter(characterId)) return false;
        return BeginPendingDeployment(_grid.GlobalToCell(globalPosition));
    }

    private void PreviewCharacterDrag(string characterId, Vector2 globalPosition)
    {
        Vector2I cell = _grid.GlobalToCell(globalPosition);
        Vector2 previewPosition = _grid.ContainsCell(cell) ? _grid.CellToGlobal(cell) : globalPosition;
        if (_battleEnded || _battlePaused
            || !_availableDeployments.TryGetValue(characterId, out UnitDeployment? deployment)
            || deployment.Profile.Unit is null)
        {
            _grid.ClearDragRangePreview();
            _hud.UpdateCharacterDragPreview(previewPosition, false, "현재 배치 불가");
            return;
        }
        if (!CanAfford(deployment.Profile.Unit))
        {
            _grid.ClearDragRangePreview();
            _hud.UpdateCharacterDragPreview(previewPosition, false, "포인트 부족");
            return;
        }

        DeploymentGrid.PlacementBlockReason reason = _grid.GetPlacementBlockReason(
            cell,
            ToPlacementType(deployment.Profile.Unit.Role));
        bool valid = reason == DeploymentGrid.PlacementBlockReason.None;
        if (valid) _grid.ShowDragRangePreview(cell, deployment.Profile.Unit.AttackCellOffsets);
        else _grid.ClearDragRangePreview();
        _hud.UpdateCharacterDragPreview(previewPosition, valid, reason switch
        {
            DeploymentGrid.PlacementBlockReason.None => "배치 가능",
            DeploymentGrid.PlacementBlockReason.OutsideGrid => "전장 밖",
            DeploymentGrid.PlacementBlockReason.Occupied => "이미 점유된 타일",
            DeploymentGrid.PlacementBlockReason.BlockedTerrain => "배치할 수 없는 지형",
            DeploymentGrid.PlacementBlockReason.TerrainNotAllowed => "배치할 수 없는 지형",
            _ => "현재 배치 불가"
        });
    }

    private int RemainingFor(DeploymentGrid.PlacementType type) => _availableDeployments.Values.Count(deployment =>
        deployment.Profile.Unit is not null && ToPlacementType(deployment.Profile.Unit.Role) == type);

    private void UpdatePlacementHud()
    {
        CatProfile? selected = _availableDeployments.TryGetValue(_selectedCharacterId, out UnitDeployment? deployment)
            ? deployment.Profile : null;
        _hud.UpdateDeploymentCards(
            _availableDeployments.Keys,
            _selectedCharacterId,
            _deploymentPoints,
            _redeployRemaining,
            _battlePaused);
        _hud.UpdatePlacement(
            selected,
            _availableDeployments.Count,
            _grid.IsChoosingFacing,
            _redeployRemaining.Count > 0,
            _availableDeployments.Values.Any(candidate =>
                candidate.Profile.Unit is not null && CanAfford(candidate.Profile.Unit)),
            _battlePaused);
        _hud.UpdateDeploymentPoints(Mathf.FloorToInt(_deploymentPoints), Definition.MaxDeploymentPoints);
    }

    private void OnCellSelected(Vector2I cell)
    {
        ClearActiveUnitSelection();
        BeginPendingDeployment(cell);
    }

    private bool BeginPendingDeployment(Vector2I cell)
    {
        if (_battleEnded || _battlePaused
            || !_availableDeployments.TryGetValue(_selectedCharacterId, out UnitDeployment? deployment)
            || deployment.Profile.Unit is null || !CanAfford(deployment.Profile.Unit)
            || !_grid.CanPlace(cell, _selectedType)) return false;
        _pendingDeploymentCharacterId = deployment.Profile.CharacterId;
        _pendingDeploymentCell = cell;
        if (!_grid.BeginFacingSelection(cell, deployment.Profile.Unit.AttackCellOffsets))
        {
            ClearPendingDeployment();
            return false;
        }
        UpdatePlacementHud();
        return true;
    }

    private void OnFacingSelected(int facing) => CommitPendingDeployment((UnitFacing)facing);

    private void OnFacingSelectionCanceled()
    {
        ClearPendingDeployment();
        UpdatePlacementHud();
    }

    private bool CommitPendingDeployment(UnitFacing facing)
    {
        string pendingCharacterId = _pendingDeploymentCharacterId;
        Vector2I cell = _pendingDeploymentCell;
        ClearPendingDeployment();
        if (_battleEnded || !_availableDeployments.TryGetValue(pendingCharacterId, out UnitDeployment? deployment)
            || deployment.Profile.Unit is null || !CanAfford(deployment.Profile.Unit))
        {
            UpdatePlacementHud();
            return false;
        }
        string characterId = deployment.Profile.CharacterId;
        UnitDefinition unit = deployment.Profile.Unit;
        DeploymentGrid.PlacementType placementType = ToPlacementType(unit.Role);
        if (!_grid.TryOccupy(cell, placementType))
        {
            UpdatePlacementHud();
            return false;
        }
        _deploymentPoints -= unit.DeploymentCost;
        Node2D deployedUnit;
        if (unit.Behavior == UnitBehavior.RangedProjectile)
        {
            Tower tower = unit.Scene.Instantiate<Tower>();
            AddChild(tower);
            tower.GlobalPosition = _grid.CellToGlobal(cell);
            tower.SetupRanged(unit, deployment.RangedSettings ?? new RangedAttackSettings(), characterId,
                _grid, cell, facing);
            tower.SetBattleActive(_combatTimelineStarted);
            _placedArcherCharacterIds.Add(tower.CharacterId);
            tower.Defeated += _ => OnDeployedUnitDefeated(characterId);
            deployedUnit = tower;
        }
        else if (unit.Behavior == UnitBehavior.MeleeBlocker)
        {
            Warrior warrior = unit.Scene.Instantiate<Warrior>();
            AddChild(warrior);
            warrior.GlobalPosition = _grid.CellToGlobal(cell);
            warrior.Setup(unit, _grid, cell, characterId, deployment.Loadout, facing);
            warrior.SetBattleActive(_combatTimelineStarted);
            _placedWarriorCharacterIds.Add(warrior.CharacterId);
            warrior.Defeated += _ => OnDeployedUnitDefeated(characterId);
            deployedUnit = warrior;
        }
        else if (unit.Behavior == UnitBehavior.SingleTargetHealer)
        {
            Healer healer = unit.Scene.Instantiate<Healer>();
            AddChild(healer);
            healer.GlobalPosition = _grid.CellToGlobal(cell);
            healer.Setup(unit, characterId, deployment.Loadout, _grid, cell, facing);
            healer.SetBattleActive(_combatTimelineStarted);
            _placedHealerCharacterIds.Add(healer.CharacterId);
            healer.Defeated += _ => OnDeployedUnitDefeated(characterId);
            deployedUnit = healer;
        }
        else
        {
            _deploymentPoints += unit.DeploymentCost;
            _grid.ReleaseCell(cell);
            return false;
        }
        _activeDeployments.Add(characterId, new ActiveDeployment(deployedUnit, deployment, cell));
        _availableDeployments.Remove(characterId);
        SelectNextAffordableCharacter();
        UpdatePlacementHud();
        return true;
    }

    private void ClearPendingDeployment()
    {
        _pendingDeploymentCharacterId = "";
        _pendingDeploymentCell = new Vector2I(-1, -1);
    }

    public bool SelectDeployedUnitAtCell(Vector2I cell)
    {
        if (_battleEnded || _battlePaused || _grid.IsChoosingFacing || _hud.IsDraggingCharacter) return false;
        ActiveDeployment? active = _activeDeployments.Values.FirstOrDefault(candidate => candidate.Cell == cell);
        if (active is null || !GodotObject.IsInstanceValid(active.Unit) || active.Unit.IsQueuedForDeletion()) return false;
        _selectedCharacterId = "";
        _selectedActiveCharacterId = active.Deployment.Profile.CharacterId;
        UpdatePlacementHud();
        RefreshUnitSelectionHud();
        return true;
    }

    public bool RetreatSelectedUnit() => RetreatCharacter(_selectedActiveCharacterId);

    public float GetRedeployRemaining(string characterId) =>
        _redeployRemaining.TryGetValue(characterId, out float remaining) ? remaining : 0.0f;

    private bool RetreatCharacter(string characterId)
    {
        if (_battleEnded || _battlePaused || string.IsNullOrEmpty(characterId)) return false;
        return RemoveActiveDeployment(characterId, refund: true, queueFree: true);
    }

    private void OnDeployedUnitDefeated(string characterId)
    {
        if (_battleEnded) return;
        RemoveActiveDeployment(characterId, refund: false, queueFree: false);
    }

    private bool RemoveActiveDeployment(string characterId, bool refund, bool queueFree)
    {
        if (!_activeDeployments.Remove(characterId, out ActiveDeployment? active)) return false;

        StopUnit(active.Unit);
        _grid.ReleaseCell(active.Cell);
        RemovePlacedCharacter(active.Deployment.Profile.Unit!.Role, characterId);
        if (refund)
        {
            UnitDefinition unit = active.Deployment.Profile.Unit!;
            int refundPoints = Mathf.FloorToInt(unit.DeploymentCost * unit.RetreatRefundRate);
            _deploymentPoints = Mathf.Min(Definition.MaxDeploymentPoints, _deploymentPoints + refundPoints);
        }
        StartRedeploymentCooldown(active.Deployment);
        if (queueFree && GodotObject.IsInstanceValid(active.Unit)) active.Unit.QueueFree();
        if (_selectedActiveCharacterId == characterId) ClearActiveUnitSelection();
        UpdatePlacementHud();
        return true;
    }

    private void StartRedeploymentCooldown(UnitDeployment deployment)
    {
        string characterId = deployment.Profile.CharacterId;
        float delay = Mathf.Max(0.0f, deployment.Profile.Unit!.RedeployDelay);
        if (delay <= 0.0f)
        {
            _availableDeployments.TryAdd(characterId, deployment);
            return;
        }
        _redeployRemaining[characterId] = delay;
    }

    private void TickRedeploymentCooldowns(float delta)
    {
        if (_redeployRemaining.Count == 0) return;
        bool displayChanged = false;
        foreach (string characterId in _redeployRemaining.Keys.ToArray())
        {
            float previous = _redeployRemaining[characterId];
            float remaining = Mathf.Max(0.0f, previous - delta);
            if (Mathf.CeilToInt(previous) != Mathf.CeilToInt(remaining)) displayChanged = true;
            if (remaining > 0.0f)
            {
                _redeployRemaining[characterId] = remaining;
                continue;
            }

            _redeployRemaining.Remove(characterId);
            UnitDeployment? deployment = _deploymentOrder.FirstOrDefault(candidate =>
                candidate.Profile.CharacterId == characterId);
            if (deployment is not null) _availableDeployments.TryAdd(characterId, deployment);
            displayChanged = true;
        }
        if (displayChanged) UpdatePlacementHud();
    }

    private void RefreshUnitSelectionHud()
    {
        if (string.IsNullOrEmpty(_selectedActiveCharacterId)
            || !_activeDeployments.TryGetValue(_selectedActiveCharacterId, out ActiveDeployment? active)
            || !GodotObject.IsInstanceValid(active.Unit) || active.Unit.IsQueuedForDeletion())
        {
            ClearActiveUnitSelection();
            return;
        }

        UnitDefinition unit = active.Deployment.Profile.Unit!;
        (float current, float maximum) = UnitHealthValues(active.Unit);
        int refund = Mathf.FloorToInt(unit.DeploymentCost * unit.RetreatRefundRate);
        _hud.ShowUnitSelection(
            active.Deployment.Profile.CharacterId,
            active.Deployment.Profile.DisplayName,
            unit.Role,
            current,
            maximum,
            refund);
    }

    private void ClearActiveUnitSelection()
    {
        bool hadSelection = !string.IsNullOrEmpty(_selectedActiveCharacterId);
        _selectedActiveCharacterId = "";
        _hud.HideUnitSelection();
        if (hadSelection && !_battleEnded) UpdatePlacementHud();
    }

    private static (float Current, float Maximum) UnitHealthValues(Node2D unit) => unit switch
    {
        Tower tower => (tower.CurrentHealth, tower.MaxHealth),
        Warrior warrior => (warrior.CurrentHealth, warrior.MaxHealth),
        Healer healer => (healer.CurrentHealth, healer.MaxHealth),
        _ => (0.0f, 0.0f)
    };

    private static void StopUnit(Node2D unit)
    {
        switch (unit)
        {
            case Tower tower:
                tower.SetBattleActive(false);
                break;
            case Warrior warrior:
                warrior.SetBattleActive(false);
                break;
            case Healer healer:
                healer.SetBattleActive(false);
                break;
        }
    }

    private void RemovePlacedCharacter(UnitRole role, string characterId)
    {
        switch (role)
        {
            case UnitRole.Ranged:
                _placedArcherCharacterIds.Remove(characterId);
                break;
            case UnitRole.Melee:
                _placedWarriorCharacterIds.Remove(characterId);
                break;
            case UnitRole.Support:
                _placedHealerCharacterIds.Remove(characterId);
                break;
        }
    }

    private void SelectNextAffordableCharacter()
    {
        UnitDeployment? next = _deploymentOrder.FirstOrDefault(deployment =>
            _availableDeployments.ContainsKey(deployment.Profile.CharacterId)
            && deployment.Profile.Unit is not null && CanAfford(deployment.Profile.Unit));
        _selectedCharacterId = next?.Profile.CharacterId ?? "";
        if (next?.Profile.Unit is null) return;
        _selectedType = ToPlacementType(next.Profile.Unit.Role);
        _grid.SetPlacementType(_selectedType);
    }

    private bool CanAfford(UnitDefinition unit) => _deploymentPoints >= unit.DeploymentCost;

    private void RegenerateDeploymentPoints(float delta)
    {
        if (_deploymentPoints >= Definition.MaxDeploymentPoints) return;

        _deploymentPoints = Mathf.Min(
            Definition.MaxDeploymentPoints,
            _deploymentPoints + Definition.DeploymentPointRegenPerSecond * delta);
        int displayedPoints = Mathf.FloorToInt(_deploymentPoints);
        if (displayedPoints == _lastDisplayedDeploymentPoints) return;

        _lastDisplayedDeploymentPoints = displayedPoints;
        if (string.IsNullOrEmpty(_selectedCharacterId)) SelectNextAffordableCharacter();
        UpdatePlacementHud();
    }

    private static DeploymentGrid.PlacementType ToPlacementType(UnitRole role) => role switch
    {
        UnitRole.Melee => DeploymentGrid.PlacementType.Melee,
        UnitRole.Support => DeploymentGrid.PlacementType.Support,
        _ => DeploymentGrid.PlacementType.Ranged
    };

    private void StartNextWave()
    {
        if (_battleEnded || !_combatTimelineStarted)
        {
            return;
        }

        _waveIndex++;
        _firstWaveCountdown = 0.0f;
        if (_waveIndex >= _waves.Length)
        {
            _allWavesSpawned = true;
            CheckForVictory();
            return;
        }

        _spawnedInWave = 0;
        WaveSpec wave = _waves[_waveIndex];
        _spawnTimer.WaitTime = wave.Interval;
        SpawnEnemy();
        if (!_battleEnded && _spawnedInWave < wave.Count)
        {
            _spawnTimer.Start();
        }

        UpdateHud();
    }

    private void SpawnEnemy()
    {
        if (_battleEnded || _waveIndex < 0 || _waveIndex >= _waves.Length)
        {
            _spawnTimer.Stop();
            return;
        }

        WaveSpec wave = _waves[_waveIndex];
        if (wave.Enemy.Behavior != UnitBehavior.PathEnemyMelee)
            throw new InvalidOperationException($"Unsupported enemy behavior '{wave.Enemy.Behavior}'.");
        Enemy enemy = wave.Enemy.Scene.Instantiate<Enemy>();
        _enemyPath.AddChild(enemy);
        enemy.Setup(wave.Enemy, wave.Health, wave.Speed);
        enemy.Defeated += OnEnemyDefeated;
        enemy.ReachedGoal += OnEnemyReachedGoal;

        _spawnedInWave++;
        _activeEnemies++;
        UpdateHud();

        if (_spawnedInWave < wave.Count)
        {
            return;
        }

        _spawnTimer.Stop();
        if (_waveIndex == _waves.Length - 1)
        {
            _allWavesSpawned = true;
            CheckForVictory();
        }
        else
        {
            ScheduleNextWave(_waveGap);
        }
    }

    private void ScheduleNextWave(double delay)
    {
        int generation = ++_waveScheduleGeneration;
        SceneTreeTimer timer = GetTree().CreateTimer(delay, processAlways: false, processInPhysics: true);
        timer.Timeout += () =>
        {
            if (generation == _waveScheduleGeneration && IsInsideTree()) StartNextWave();
        };
    }

    private void OnEnemyDefeated(Enemy enemy)
    {
        enemy.Defeated -= OnEnemyDefeated;
        enemy.ReachedGoal -= OnEnemyReachedGoal;
        _activeEnemies = Mathf.Max(0, _activeEnemies - 1);
        _defeatedEnemies++;
        UpdateHud();
        CheckForVictory();
    }

    private void OnEnemyReachedGoal(Enemy enemy)
    {
        enemy.Defeated -= OnEnemyDefeated;
        enemy.ReachedGoal -= OnEnemyReachedGoal;
        _activeEnemies = Mathf.Max(0, _activeEnemies - 1);
        _escapedEnemies++;
        _baseHealth = Mathf.Max(0, _baseHealth - 1);
        UpdateHud();

        if (_baseHealth <= 0)
        {
            FinishBattle(false);
        }
        else
        {
            CheckForVictory();
        }
    }

    private void CheckForVictory()
    {
        if (_allWavesSpawned && _activeEnemies == 0 && !_battleEnded)
        {
            FinishBattle(true);
        }
    }

    private void FinishBattle(bool victory)
    {
        if (_battleEnded)
        {
            return;
        }

        _battleEnded = true;
        RestoreSimulationDefaults();
        _spawnTimer.Stop();
        _grid.SetPlacementEnabled(false);
        ClearActiveUnitSelection();

        foreach (Node node in GetTree().GetNodesInGroup("towers"))
        {
            if (node is Tower tower)
            {
                tower.SetBattleActive(false);
            }
        }

        foreach (Node node in GetTree().GetNodesInGroup("warriors"))
            if (node is Warrior warrior) warrior.SetBattleActive(false);

        foreach (Node node in GetTree().GetNodesInGroup("healers"))
            if (node is Healer healer) healer.SetBattleActive(false);

        foreach (Node node in GetTree().GetNodesInGroup("enemies"))
            if (node is Enemy enemy) enemy.SetBattleActive(false);
        foreach (Node projectile in GetNode("Projectiles").GetChildren())
        {
            projectile.SetPhysicsProcess(false);
            projectile.QueueFree();
        }

        if (!victory)
        {
            foreach (Node node in GetTree().GetNodesInGroup("enemies"))
            {
                if (node is Enemy enemy)
                {
                    enemy.Despawn();
                }
            }

            _activeEnemies = 0;
        }

        UpdateHud();
        _hud.ShowResult(victory, _defeatedEnemies, _escapedEnemies);
        _battleVictory = victory;
        ResultSyncStatusCode = "Saving";
        if (UseLocalResultSync())
        {
            ResultSyncStatusCode = "Saved";
            _hud.ShowResultSyncSucceeded("테스트 진행도 // 서버 제출 생략");
        }
        else
        {
            _ = SubmitBattleResultAsync();
        }
        GD.Print($"Battle finished: {(victory ? "victory" : "defeat")}, defeated={_defeatedEnemies}, escaped={_escapedEnemies}");
    }

    public void RetryBattleResultSync()
    {
        if (!_battleEnded || _resultSyncBusy || ResultSyncStatusCode == "Saved") return;
        _ = SubmitBattleResultAsync();
    }

    private async Task SubmitBattleResultAsync()
    {
        if (_resultSyncBusy) return;
        if (!StageProgressionId.TryParse(Definition.Id, out int progressionId))
        {
            ResultSyncStatusCode = "invalid_stage_id";
            _hud.ShowResultSyncFailed(ResultSyncStatusCode);
            return;
        }

        _resultSyncBusy = true;
        ResultSyncStatusCode = "Saving";
        _hud.ShowResultSyncPending();
        StageResultSubmitResult result = await _progressClient.SubmitResultAsync(
            ServerBaseUrl(), PlayerId(), progressionId, _battleVictory, _progressCancellation.Token);
        if (!IsInsideTree()) return;
        _resultSyncBusy = false;
        if (!result.IsSuccess || result.Payload is null || result.Payload.StageId != progressionId)
        {
            ResultSyncStatusCode = result.IsSuccess ? "invalid_response" : result.ErrorCode;
            _hud.ShowResultSyncFailed(ResultSyncStatusCode);
            return;
        }

        ResultSyncStatusCode = "Saved";
        string message = !_battleVictory ? "서버 확인 완료 // 패배 기록 · 진행도 변경 없음"
            : result.Payload.ProgressUpdated
                ? "서버 저장 완료 // 첫 클리어 · 다음 작전 해금"
                : "서버 저장 완료 // 기존 클리어 기록 유지";
        _hud.ShowResultSyncSucceeded(message);
    }

    private static bool UseLocalResultSync()
    {
        if (StageSelectionState.UseLocalProgressionForTests) return true;
        bool submitInHeadless = (bool)ProjectSettings.GetSetting(
            "defense_game/progress/submit_in_headless", false);
        return DisplayServer.GetName() == "headless" && !submitInHeadless;
    }

    private static string ServerBaseUrl() => (string)ProjectSettings.GetSetting(
        "defense_game/server/base_url", "http://127.0.0.1:5080");

    private static string PlayerId() => (string)ProjectSettings.GetSetting(
        "defense_game/player/development_user_id", "local-development-user");

    private void UpdateHud()
    {
        _hud.UpdateStatus(
            Mathf.Max(0, _waveIndex + 1),
            _waves.Length,
            _activeEnemies,
            _defeatedEnemies,
            _escapedEnemies,
            _baseHealth,
            _waveIndex < 0 ? Mathf.CeilToInt(_firstWaveCountdown) : -1);
        _hud.UpdateBattleControls(_battlePaused, _battleSpeed, CanStartFirstWaveEarly, _battleEnded);
    }

    private void BuildDeploymentQueues()
    {
        IEnumerable<CatProfile> runtimeProfiles = StageSelectionState.SelectedProfiles.Count > 0
            ? StageSelectionState.SelectedProfiles : Defaults.CatProfiles;
        var profiles = runtimeProfiles.ToDictionary(profile => profile.CharacterId, StringComparer.Ordinal);
        var loadouts = StageSelectionState.SelectedCharacterLoadouts
            .ToDictionary(entry => entry.CharacterId, entry => entry.Loadout, StringComparer.Ordinal);
        var composer = new RangedSkillEffectComposer();
        IEnumerable<string> selectedIds = StageSelectionState.SelectedCharacterIds.Count > 0
            ? StageSelectionState.SelectedCharacterIds : Defaults.CatProfiles.Select(profile => profile.CharacterId);
        foreach (string characterId in selectedIds)
        {
            if (!profiles.TryGetValue(characterId, out CatProfile? profile) || profile.Unit is null) continue;
            loadouts.TryGetValue(characterId, out SkillLoadout? loadout);
            switch (profile.Unit.Behavior)
            {
                case UnitBehavior.RangedProjectile:
                    _deploymentOrder.Add(new UnitDeployment(profile, loadout, composer.Compose(loadout)));
                    break;
                case UnitBehavior.MeleeBlocker:
                case UnitBehavior.SingleTargetHealer:
                    _deploymentOrder.Add(new UnitDeployment(profile, loadout, null));
                    break;
            }
        }
        foreach (UnitDeployment deployment in _deploymentOrder)
            _availableDeployments.Add(deployment.Profile.CharacterId, deployment);
    }

    public override void _ExitTree()
    {
        _waveScheduleGeneration++;
        RestoreSimulationDefaults();
        _progressCancellation.Cancel();
        _progressCancellation.Dispose();
    }

    private readonly record struct WaveSpec(UnitDefinition Enemy, int Count, float Health, float Speed, double Interval);
    private sealed record UnitDeployment(CatProfile Profile, SkillLoadout? Loadout,
        RangedAttackSettings? RangedSettings);
    private sealed record ActiveDeployment(Node2D Unit, UnitDeployment Deployment, Vector2I Cell);

    private void RestoreSimulationDefaults()
    {
        _battlePaused = false;
        _battleSpeed = 1;
        Engine.TimeScale = 1.0;
        GetTree().Paused = false;
    }
}
