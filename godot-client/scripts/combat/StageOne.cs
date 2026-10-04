using Godot;

namespace DefenseGame.Client.Combat;

public partial class StageOne : Node2D
{
    private static readonly Vector2I[] PathCorners =
    [
        new(-2, 3), new(1, 3), new(1, 0), new(6, 0),
        new(6, 5), new(11, 5), new(11, 2), new(17, 2),
    ];

    private static readonly Vector2I[] BlockedCells = [new(15, 0), new(15, 1)];
    private DeploymentGrid _grid = null!;

    private static readonly WaveSpec[] Waves =
    [
        new(5, 12.0f, 72.0f, 1.15),
        new(7, 17.0f, 82.0f, 0.90),
        new(9, 23.0f, 92.0f, 0.72),
    ];

    private const int ArcherCount = 2;
    private int _remainingArchers = ArcherCount;

    private int _remainingWarriors = 1;
    private bool _battleStarted;
    private DeploymentGrid.PlacementType _selectedType;
    private readonly PackedScene _warriorScene = GD.Load<PackedScene>("res://scenes/warrior.tscn");

    private const int MaxBaseHealth = 5;
    private const double WaveGap = 2.0;

    private readonly PackedScene _enemyScene = GD.Load<PackedScene>("res://scenes/enemy.tscn");
    private readonly PackedScene _towerScene = GD.Load<PackedScene>("res://scenes/tower.tscn");

    private int _waveIndex = -1;
    private int _spawnedInWave;
    private int _activeEnemies;
    private int _defeatedEnemies;
    private int _escapedEnemies;
    private int _baseHealth = MaxBaseHealth;
    private bool _allWavesSpawned;
    private bool _battleEnded;

    private Path2D _enemyPath = null!;
    private Timer _spawnTimer = null!;
    private UI.CombatHud _hud = null!;

    public override void _Ready()
    {
        _enemyPath = GetNode<Path2D>("EnemyPath");
        _spawnTimer = GetNode<Timer>("SpawnTimer");
        _hud = GetNode<UI.CombatHud>("HUD");

        _grid = new DeploymentGrid { Name = "DeploymentGrid", Position = new Vector2(40, 120) };
        AddChild(_grid);
        _grid.Configure(PathCorners, BlockedCells);
        _grid.CellSelected += OnCellSelected;
        _hud.ArcherSelected += () => SelectPlacementType(DeploymentGrid.PlacementType.Ranged);
        _hud.WarriorSelected += () => SelectPlacementType(DeploymentGrid.PlacementType.Melee);
        BuildEnemyPath();
        _spawnTimer.Timeout += SpawnEnemy;
        UpdateHud();
        UpdatePlacementHud();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, 1280, 720), new Color("5b843e"));
    }

    private void BuildEnemyPath()
    {
        var curve = new Curve2D();
        foreach (Vector2I cell in PathCorners)
        {
            curve.AddPoint(_enemyPath.ToLocal(_grid.CellToGlobal(cell)));
        }
        _enemyPath.Curve = curve;
    }

    public bool SelectPlacementType(DeploymentGrid.PlacementType type)
    {
        if (_battleStarted || _battleEnded) return false;
        if (type == DeploymentGrid.PlacementType.Ranged ? _remainingArchers <= 0
            : type != DeploymentGrid.PlacementType.Melee || _remainingWarriors <= 0) return false;
        _selectedType = type;
        _grid.SetPlacementType(type);
        UpdatePlacementHud();
        return true;
    }

    private void UpdatePlacementHud() => _hud.UpdatePlacement(_remainingArchers, _remainingWarriors,
        _selectedType == DeploymentGrid.PlacementType.Melee);

    private void OnCellSelected(Vector2I cell)
    {
        int remaining = _selectedType == DeploymentGrid.PlacementType.Ranged ? _remainingArchers : _remainingWarriors;
        if (_battleStarted || _battleEnded || remaining <= 0 || !_grid.TryOccupy(cell, _selectedType)) return;
        if (_selectedType == DeploymentGrid.PlacementType.Ranged)
        {
            _remainingArchers--;
            Tower tower = _towerScene.Instantiate<Tower>();
            AddChild(tower);
            tower.GlobalPosition = _grid.CellToGlobal(cell);
            tower.Defeated += _ => _grid.ReleaseCell(cell);
        }
        else
        {
            _remainingWarriors--;
            Warrior warrior = _warriorScene.Instantiate<Warrior>();
            AddChild(warrior);
            warrior.GlobalPosition = _grid.CellToGlobal(cell);
            warrior.Setup(_grid, cell);
            warrior.Defeated += _ => _grid.ReleaseCell(cell);
        }
        if (_remainingArchers + _remainingWarriors == 0)
        {
            _battleStarted = true;
            _grid.SetPlacementEnabled(false);
            foreach (Node node in GetTree().GetNodesInGroup("warriors"))
                if (node is Warrior warrior) warrior.SetBattleActive(true);
            ScheduleNextWave(0.8);
        }
        else if (remaining == 1)
        {
            SelectPlacementType(_remainingArchers > 0 ? DeploymentGrid.PlacementType.Ranged : DeploymentGrid.PlacementType.Melee);
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
        if (_waveIndex >= Waves.Length)
        {
            _allWavesSpawned = true;
            CheckForVictory();
            return;
        }

        _spawnedInWave = 0;
        WaveSpec wave = Waves[_waveIndex];
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
        if (_battleEnded || _waveIndex < 0 || _waveIndex >= Waves.Length)
        {
            _spawnTimer.Stop();
            return;
        }

        WaveSpec wave = Waves[_waveIndex];
        Enemy enemy = _enemyScene.Instantiate<Enemy>();
        _enemyPath.AddChild(enemy);
        enemy.Setup(wave.Health, wave.Speed);
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
        if (_waveIndex == Waves.Length - 1)
        {
            _allWavesSpawned = true;
            CheckForVictory();
        }
        else
        {
            ScheduleNextWave(WaveGap);
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
            Waves.Length,
            _activeEnemies,
            _defeatedEnemies,
            _escapedEnemies,
            _baseHealth);
    }

    private readonly record struct WaveSpec(int Count, float Health, float Speed, double Interval);
}
