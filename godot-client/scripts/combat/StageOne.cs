using Godot;

namespace DefenseGame.Client.Combat;

public partial class StageOne : Node2D
{
    private static readonly Vector2[] PathPoints =
    [
        new(-60.0f, 360.0f),
        new(180.0f, 360.0f),
        new(180.0f, 150.0f),
        new(520.0f, 150.0f),
        new(520.0f, 530.0f),
        new(900.0f, 530.0f),
        new(900.0f, 300.0f),
        new(1180.0f, 300.0f),
        new(1340.0f, 300.0f),
    ];

    private static readonly Vector2[] TowerPositions =
    [
        new(355.0f, 300.0f),
        new(745.0f, 390.0f),
    ];

    private static readonly WaveSpec[] Waves =
    [
        new(5, 12.0f, 72.0f, 1.15),
        new(7, 17.0f, 82.0f, 0.90),
        new(9, 23.0f, 92.0f, 0.72),
    ];

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

        BuildEnemyPath();
        SpawnTowers();
        _spawnTimer.Timeout += SpawnEnemy;
        UpdateHud();
        ScheduleNextWave(0.8);
    }

    public override void _Draw()
    {
        DrawGround();
        DrawRoad();
        DrawLandmarks();
    }

    private void BuildEnemyPath()
    {
        var curve = new Curve2D();
        foreach (Vector2 point in PathPoints)
        {
            curve.AddPoint(point);
        }

        _enemyPath.Curve = curve;
    }

    private void SpawnTowers()
    {
        foreach (Vector2 towerPosition in TowerPositions)
        {
            Tower tower = _towerScene.Instantiate<Tower>();
            AddChild(tower);
            tower.Position = towerPosition;
        }
    }

    private void StartNextWave()
    {
        if (_battleEnded)
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

        foreach (Node node in GetTree().GetNodesInGroup("towers"))
        {
            if (node is Tower tower)
            {
                tower.SetBattleActive(false);
            }
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

    private void DrawGround()
    {
        DrawRect(new Rect2(0.0f, 0.0f, 1280.0f, 720.0f), Rgb(20, 38, 29));
        for (int y = 0; y < 720; y += 32)
        {
            for (int x = 0; x < 1280; x += 32)
            {
                int tileIndex = x / 32 + y / 32;
                Color tint = tileIndex % 2 == 0 ? Rgb(25, 48, 36) : Rgb(23, 43, 33);
                DrawRect(new Rect2(x, y, 32, 32), tint);
            }
        }

        Vector2[] markers =
        [
            new(80, 90),
            new(320, 610),
            new(720, 90),
            new(1080, 610),
            new(1110, 130),
        ];

        foreach (Vector2 marker in markers)
        {
            DrawRect(new Rect2(marker - new Vector2(8, 8), new Vector2(16, 16)), Rgb(36, 70, 50));
            DrawRect(new Rect2(marker - new Vector2(3, 14), new Vector2(6, 28)), Rgb(49, 92, 63));
        }
    }

    private void DrawRoad()
    {
        DrawPolyline(PathPoints, Rgb(61, 45, 38), 82.0f);
        DrawPolyline(PathPoints, Rgb(116, 82, 59), 68.0f);
        DrawPolyline(PathPoints, Rgb(154, 112, 75), 4.0f);

        foreach (Vector2 point in PathPoints)
        {
            DrawCircle(point, 34.0f, Rgb(116, 82, 59));
        }
    }

    private void DrawLandmarks()
    {
        DrawRect(new Rect2(0, 326, 52, 68), Rgb(72, 40, 33));
        DrawRect(new Rect2(10, 336, 42, 48), Rgb(183, 71, 50));
        DrawRect(new Rect2(1190, 252, 90, 96), Rgb(36, 42, 49));
        DrawRect(new Rect2(1204, 266, 62, 68), Rgb(81, 88, 102));
        DrawRect(new Rect2(1218, 286, 34, 48), Rgb(24, 27, 33));
    }

    private static Color Rgb(byte red, byte green, byte blue, byte alpha = 255)
    {
        return new Color(red / 255.0f, green / 255.0f, blue / 255.0f, alpha / 255.0f);
    }

    private readonly record struct WaveSpec(int Count, float Health, float Speed, double Interval);
}
