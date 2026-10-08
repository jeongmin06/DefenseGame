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
    public int RemainingArchers => _remainingArchers;
    public string NextArcherCharacterId => _archerPlacementIndex < _archerQueue.Count
        ? _archerQueue[_archerPlacementIndex].CharacterId : "";
    public string[] PlacedArcherCharacterIds => _placedArcherCharacterIds.ToArray();
    private DeploymentGrid _grid = null!;
    private Vector2I[] _pathCorners = [];
    private WaveSpec[] _waves = [];
    private readonly Dictionary<DeploymentGrid.PlacementType, UnitDefinition> _units = new();
    private readonly List<ArcherDeployment> _archerQueue = new();
    private readonly List<string> _placedArcherCharacterIds = new();
    private int _archerPlacementIndex;
    private int _remainingArchers;
    private int _remainingWarriors;
    private int _remainingHealers;
    private float _firstWaveDelay;
    private float _waveGap;
    private bool _battleStarted;
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

    public override void _Ready()
    {
        if (StageSelectionState.SelectedStage is not null)
            Definition = StageSelectionState.SelectedStage;
        BuildArcherQueue();
        Loadout = _archerQueue.FirstOrDefault()?.Loadout ?? StageSelectionState.SelectedLoadout;
        ArcherAttackSettings = _archerQueue.FirstOrDefault()?.Settings
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
        foreach (RosterEntry entry in Definition.Roster)
        {
            var type = (DeploymentGrid.PlacementType)entry.Unit.Role;
            _units.Add(type, entry.Unit);
            switch (type)
            {
                case DeploymentGrid.PlacementType.Ranged:
                    if (_archerQueue.Count == 0) _remainingArchers = entry.Count;
                    break;
                case DeploymentGrid.PlacementType.Melee: _remainingWarriors = entry.Count; break;
                case DeploymentGrid.PlacementType.Support: _remainingHealers = entry.Count; break;
            }
        }
        _grid = new DeploymentGrid { Name = "DeploymentGrid" };
        AddChild(_grid);
        _grid.Configure(Definition);
        _grid.CellSelected += OnCellSelected;
        _hud.ArcherSelected += () => SelectPlacementType(DeploymentGrid.PlacementType.Ranged);
        _hud.WarriorSelected += () => SelectPlacementType(DeploymentGrid.PlacementType.Melee);
        _hud.HealerSelected += () => SelectPlacementType(DeploymentGrid.PlacementType.Support);
        _hud.RetryRequested += RestartStage;
        _hud.StageListRequested += ReturnToStageList;
        _hud.UpdateStage(Definition.DisplayName, Definition.Id);
        BuildEnemyPath();
        _spawnTimer.Timeout += SpawnEnemy;
        UpdateHud();
        UpdatePlacementHud();
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
        if (_battleStarted || _battleEnded) return false;
        if (RemainingFor(type) <= 0) return false;
        _selectedType = type;
        _grid.SetPlacementType(type);
        UpdatePlacementHud();
        return true;
    }

    private int RemainingFor(DeploymentGrid.PlacementType type) => type switch
    {
        DeploymentGrid.PlacementType.Ranged => _remainingArchers,
        DeploymentGrid.PlacementType.Melee => _remainingWarriors,
        DeploymentGrid.PlacementType.Support => _remainingHealers,
        _ => 0
    };

    private void UpdatePlacementHud() => _hud.UpdatePlacement(_remainingArchers, _remainingWarriors,
        _remainingHealers, _selectedType, NextArcherCharacterId);

    private void OnCellSelected(Vector2I cell)
    {
        int remaining = RemainingFor(_selectedType);
        if (_battleStarted || _battleEnded || remaining <= 0 || !_grid.TryOccupy(cell, _selectedType)) return;
        UnitDefinition unit = _units[_selectedType];
        if (_selectedType == DeploymentGrid.PlacementType.Ranged)
        {
            _remainingArchers--;
            ArcherDeployment? deployment = _archerPlacementIndex < _archerQueue.Count
                ? _archerQueue[_archerPlacementIndex++] : null;
            if (deployment is not null) unit = deployment.Unit;
            Tower tower = unit.Scene.Instantiate<Tower>();
            AddChild(tower);
            tower.SetupRanged(unit, deployment?.Settings ?? ArcherAttackSettings, deployment?.CharacterId ?? "");
            _placedArcherCharacterIds.Add(tower.CharacterId);
            tower.GlobalPosition = _grid.CellToGlobal(cell);
            tower.Defeated += _ => _grid.ReleaseCell(cell);
        }
        else if (_selectedType == DeploymentGrid.PlacementType.Melee)
        {
            _remainingWarriors--;
            Warrior warrior = unit.Scene.Instantiate<Warrior>();
            AddChild(warrior);
            warrior.GlobalPosition = _grid.CellToGlobal(cell);
            warrior.Setup(unit, _grid, cell);
            warrior.Defeated += _ => _grid.ReleaseCell(cell);
        }
        else
        {
            _remainingHealers--;
            Healer healer = unit.Scene.Instantiate<Healer>();
            AddChild(healer);
            healer.Setup(unit);
            healer.GlobalPosition = _grid.CellToGlobal(cell);
            healer.Defeated += _ => _grid.ReleaseCell(cell);
        }
        if (_remainingArchers + _remainingWarriors + _remainingHealers == 0)
        {
            _battleStarted = true;
            _grid.SetPlacementEnabled(false);
            foreach (Node node in GetTree().GetNodesInGroup("warriors"))
                if (node is Warrior warrior) warrior.SetBattleActive(true);
            foreach (Node node in GetTree().GetNodesInGroup("healers"))
                if (node is Healer healer) healer.SetBattleActive(true);
            ScheduleNextWave(_firstWaveDelay);
        }
        else if (remaining == 1)
        {
            SelectPlacementType(_remainingArchers > 0 ? DeploymentGrid.PlacementType.Ranged
                : _remainingWarriors > 0 ? DeploymentGrid.PlacementType.Melee : DeploymentGrid.PlacementType.Support);
        }
        UpdatePlacementHud();
    }

    private void StartNextWave()
    {
        if (_battleEnded || !_battleStarted)
        {
            return;
        }

        _waveIndex++;
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
            _baseHealth);
    }

    private void BuildArcherQueue()
    {
        IEnumerable<CatProfile> runtimeProfiles = StageSelectionState.SelectedProfiles.Count > 0
            ? StageSelectionState.SelectedProfiles : Defaults.CatProfiles;
        var profiles = runtimeProfiles.ToDictionary(profile => profile.CharacterId, StringComparer.Ordinal);
        var loadouts = StageSelectionState.SelectedCharacterLoadouts
            .ToDictionary(entry => entry.CharacterId, entry => entry.Loadout, StringComparer.Ordinal);
        var composer = new RangedSkillEffectComposer();
        foreach (string characterId in StageSelectionState.SelectedCharacterIds)
        {
            if (!profiles.TryGetValue(characterId, out CatProfile? profile) || profile.Unit is null) continue;
            loadouts.TryGetValue(characterId, out SkillLoadout? loadout);
            _archerQueue.Add(new ArcherDeployment(characterId, profile.Unit, loadout, composer.Compose(loadout)));
        }
        _remainingArchers = _archerQueue.Count;
    }

    private readonly record struct WaveSpec(UnitDefinition Enemy, int Count, float Health, float Speed, double Interval);
    private sealed record ArcherDeployment(string CharacterId, UnitDefinition Unit, SkillLoadout? Loadout,
        RangedAttackSettings Settings);
}
