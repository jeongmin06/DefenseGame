using Godot;
using DefenseGame.Client.Data;
using DefenseGame.Client.Skills;
using System;
using System.Collections.Generic;
using System.Linq;

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
    public string NextArcherCharacterId => _deploymentOrder.FirstOrDefault(deployment =>
        deployment.Profile.Unit?.Role == UnitRole.Ranged && _availableDeployments.ContainsKey(deployment.Profile.CharacterId))
        ?.Profile.CharacterId ?? "";
    public string[] PlacedArcherCharacterIds => _placedArcherCharacterIds.ToArray();
    private DeploymentGrid _grid = null!;
    private Vector2I[] _pathCorners = [];
    private WaveSpec[] _waves = [];
    private readonly List<UnitDeployment> _deploymentOrder = new();
    private readonly Dictionary<string, UnitDeployment> _availableDeployments = new(StringComparer.Ordinal);
    private readonly List<string> _placedArcherCharacterIds = new();
    private readonly List<string> _placedWarriorCharacterIds = new();
    private readonly List<string> _placedHealerCharacterIds = new();
    private string _selectedCharacterId = "";
    private float _firstWaveDelay;
    private float _firstWaveCountdown;
    private float _waveGap;
    private bool _combatTimelineStarted;
    private int _lastDisplayedCountdownSecond = -1;
    private DeploymentGrid.PlacementType _selectedType;

    private int _waveIndex = -1;
    private int _spawnedInWave;
    private int _activeEnemies;
    private int _defeatedEnemies;
    private int _escapedEnemies;
    private int _baseHealth;
    private bool _allWavesSpawned;
    private bool _battleEnded;

    private Path2D _enemyPath = null!;
    private Timer _spawnTimer = null!;
    private UI.CombatHud _hud = null!;

    public string[] PlacedWarriorCharacterIds => _placedWarriorCharacterIds.ToArray();
    public string[] PlacedHealerCharacterIds => _placedHealerCharacterIds.ToArray();

    public override void _Ready()
    {
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
        _pathCorners = System.Linq.Enumerable.ToArray(Definition.PathCorners);
        _waves = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(Definition.Waves,
            w => new WaveSpec(w.Enemy, w.Count, w.HealthOverride, w.SpeedOverride, w.Interval)));
        _grid = new DeploymentGrid { Name = "DeploymentGrid" };
        AddChild(_grid);
        _grid.Configure(Definition);
        _grid.CellSelected += OnCellSelected;
        _hud.CharacterSelected += characterId => SelectDeploymentCharacter(characterId);
        _hud.CharacterDropped += (characterId, globalPosition) =>
            TryDeployCharacterAtGlobalPosition(characterId, globalPosition);
        _hud.RetryRequested += RestartStage;
        _hud.StageListRequested += ReturnToStageList;
        _hud.UpdateStage(Definition.DisplayName, Definition.Id);
        _hud.ConfigureDeploymentCards(_deploymentOrder.Select(deployment => deployment.Profile));
        SelectNextAvailableCharacter();
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
        if (!_combatTimelineStarted || _battleEnded || _waveIndex >= 0) return;

        _firstWaveCountdown = Mathf.Max(0.0f, _firstWaveCountdown - (float)delta);
        int displayedSecond = Mathf.CeilToInt(_firstWaveCountdown);
        if (displayedSecond == _lastDisplayedCountdownSecond) return;

        _lastDisplayedCountdownSecond = displayedSecond;
        UpdateHud();
    }

    public void RestartStage() => GetTree().ReloadCurrentScene();

    public void ReturnToStageList()
    {
        StageSelectionState.ClearStage();
        GetTree().ChangeSceneToFile("res://scenes/stage_select.tscn");
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, 1280, 720), new Color("5b843e"));
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
        if (_battleEnded || !_availableDeployments.TryGetValue(characterId, out UnitDeployment? deployment)
            || deployment.Profile.Unit is null) return false;
        _selectedCharacterId = characterId;
        _selectedType = ToPlacementType(deployment.Profile.Unit.Role);
        _grid.SetPlacementType(_selectedType);
        UpdatePlacementHud();
        return true;
    }

    public bool TryDeployCharacterAtGlobalPosition(string characterId, Vector2 globalPosition)
    {
        if (!SelectDeploymentCharacter(characterId)) return false;
        return _grid.SelectCell(_grid.GlobalToCell(globalPosition));
    }

    private int RemainingFor(DeploymentGrid.PlacementType type) => _availableDeployments.Values.Count(deployment =>
        deployment.Profile.Unit is not null && ToPlacementType(deployment.Profile.Unit.Role) == type);

    private void UpdatePlacementHud()
    {
        CatProfile? selected = _availableDeployments.TryGetValue(_selectedCharacterId, out UnitDeployment? deployment)
            ? deployment.Profile : null;
        _hud.UpdateDeploymentCards(_availableDeployments.Keys, _selectedCharacterId);
        _hud.UpdatePlacement(selected, _availableDeployments.Count);
    }

    private void OnCellSelected(Vector2I cell)
    {
        if (_battleEnded || !_availableDeployments.TryGetValue(_selectedCharacterId, out UnitDeployment? deployment)
            || deployment.Profile.Unit is null || !_grid.TryOccupy(cell, _selectedType)) return;
        string characterId = deployment.Profile.CharacterId;
        UnitDefinition unit = deployment.Profile.Unit;
        if (unit.Role == UnitRole.Ranged)
        {
            Tower tower = unit.Scene.Instantiate<Tower>();
            AddChild(tower);
            tower.SetupRanged(unit, deployment.RangedSettings ?? new RangedAttackSettings(), characterId);
            tower.SetBattleActive(_combatTimelineStarted);
            _placedArcherCharacterIds.Add(tower.CharacterId);
            tower.GlobalPosition = _grid.CellToGlobal(cell);
            tower.Defeated += _ => _grid.ReleaseCell(cell);
        }
        else if (unit.Role == UnitRole.Melee)
        {
            Warrior warrior = unit.Scene.Instantiate<Warrior>();
            AddChild(warrior);
            warrior.GlobalPosition = _grid.CellToGlobal(cell);
            warrior.Setup(unit, _grid, cell, characterId, deployment.Loadout);
            warrior.SetBattleActive(_combatTimelineStarted);
            _placedWarriorCharacterIds.Add(warrior.CharacterId);
            warrior.Defeated += _ => _grid.ReleaseCell(cell);
        }
        else
        {
            Healer healer = unit.Scene.Instantiate<Healer>();
            AddChild(healer);
            healer.Setup(unit, characterId, deployment.Loadout);
            healer.SetBattleActive(_combatTimelineStarted);
            _placedHealerCharacterIds.Add(healer.CharacterId);
            healer.GlobalPosition = _grid.CellToGlobal(cell);
            healer.Defeated += _ => _grid.ReleaseCell(cell);
        }
        _availableDeployments.Remove(characterId);
        SelectNextAvailableCharacter();
        UpdatePlacementHud();
    }

    private void SelectNextAvailableCharacter()
    {
        UnitDeployment? next = _deploymentOrder.FirstOrDefault(deployment =>
            _availableDeployments.ContainsKey(deployment.Profile.CharacterId));
        _selectedCharacterId = next?.Profile.CharacterId ?? "";
        if (next?.Profile.Unit is null) return;
        _selectedType = ToPlacementType(next.Profile.Unit.Role);
        _grid.SetPlacementType(_selectedType);
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
        SceneTreeTimer timer = GetTree().CreateTimer(delay, true, true);
        timer.Timeout += StartNextWave;
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
        _spawnTimer.Stop();
        _grid.SetPlacementEnabled(false);

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
        GD.Print($"Battle finished: {(victory ? "victory" : "defeat")}, defeated={_defeatedEnemies}, escaped={_escapedEnemies}");
    }

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
            switch (profile.Unit.Role)
            {
                case UnitRole.Ranged:
                    _deploymentOrder.Add(new UnitDeployment(profile, loadout, composer.Compose(loadout)));
                    break;
                case UnitRole.Melee:
                case UnitRole.Support:
                    _deploymentOrder.Add(new UnitDeployment(profile, loadout, null));
                    break;
            }
        }
        foreach (UnitDeployment deployment in _deploymentOrder)
            _availableDeployments.Add(deployment.Profile.CharacterId, deployment);
    }

    private readonly record struct WaveSpec(UnitDefinition Enemy, int Count, float Health, float Speed, double Interval);
    private sealed record UnitDeployment(CatProfile Profile, SkillLoadout? Loadout,
        RangedAttackSettings? RangedSettings);
}
