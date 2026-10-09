using Godot;
using DefenseGame.Client.Data;
using System.Collections.Generic;
using DefenseGame.Client.Visuals;
using DefenseGame.Client.Skills;

namespace DefenseGame.Client.Combat;

public partial class Warrior : Node2D
{
    [Signal] public delegate void DefeatedEventHandler(Warrior unit);
    private UnitHealth _health = null!;
    public float CurrentHealth => _health.CurrentHealth;
    public float MaxHealth => _health.MaxHealth;
    public bool IsAlive => _health.IsAlive;
    public string CharacterId { get; private set; } = "";

    public void TakeDamage(float amount)
    {
        if (_battleActive) _health.TakeDamage(amount);
    }

    private void OnDepleted()
    {
        SetBattleActive(false);
        EmitSignal(SignalName.Defeated, this);
        QueueFree();
    }

    public float Damage { get; set; }
    public float AttackInterval { get; set; }
    public int AttackRangeCells { get; set; }
    public int TargetLimit { get; set; }
    public int BlockCount { get; set; }
    // Empty uses the Manhattan radius; custom offsets support later attack shapes.
    public Godot.Collections.Array<Vector2I> AttackCellOffsets { get; set; } = new();

    private readonly List<Enemy> _blocked = new();
    private DeploymentGrid _grid = null!;
    private Vector2I _cell;
    private DirectionalAnimatedSprite _sprite = null!;
    private bool _battleActive;
    private bool _attacking;
    private bool _hitApplied;
    private float _cooldown;
    private int _hitFrame;

    public override void _Ready()
    {
        _health = GetNode<UnitHealth>("UnitHealth");
        _health.Depleted += OnDepleted;
        _sprite = GetNode<DirectionalAnimatedSprite>("AnimatedSprite");
        _sprite.FrameChanged += OnFrameChanged;
        _sprite.AnimationFinished += ResetAnimation;
        _sprite.Play("idle");
    }

    public void Setup(UnitDefinition definition, DeploymentGrid grid, Vector2I cell)
    {
        Setup(definition, grid, cell, "", null);
    }

    public void Setup(UnitDefinition definition, DeploymentGrid grid, Vector2I cell, string characterId, SkillLoadout? loadout)
    {
        CharacterId = characterId;
        UnitSkillEffectSettings effects = new UnitSkillEffectComposer().Compose(loadout);
        _health.Setup(definition.MaxHealth);
        Damage = definition.ActionPower * effects.DamageMultiplier + effects.AddedHitDamage;
        AttackInterval = definition.ActionInterval;
        AttackRangeCells = definition.RangeCells;
        AttackCellOffsets = new Godot.Collections.Array<Vector2I>(definition.AttackCellOffsets);
        TargetLimit = definition.TargetLimit;
        BlockCount = definition.BlockCount;
        _hitFrame = definition.ActionFrame;
        _grid = grid;
        _cell = cell;
    }

    public void SetBattleActive(bool active)
    {
        _battleActive = active && IsAlive;
        if (!_battleActive)
        {
            ReleaseAllBlocks();
            ResetAnimation();
        }
    }

    public bool IsInAttackRange(Vector2I cell)
    {
        Vector2I offset = cell - _cell;
        return AttackCellOffsets.Count > 0 ? AttackCellOffsets.Contains(offset)
            : Mathf.Abs(offset.X) + Mathf.Abs(offset.Y) <= AttackRangeCells;
    }

    private List<Enemy> FindTargets()
    {
        var targets = new List<Enemy>();
        foreach (Node node in GetTree().GetNodesInGroup("enemies"))
            if (node is Enemy enemy && enemy.IsActive() && !enemy.IsQueuedForDeletion()
                && IsInAttackRange(_grid.GlobalToCell(enemy.GlobalPosition))) targets.Add(enemy);
        targets.Sort((a, b) =>
        {
            int progress = b.Progress.CompareTo(a.Progress);
            return progress != 0 ? progress : a.GetInstanceId().CompareTo(b.GetInstanceId());
        });
        return targets;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_battleActive || _grid is null) return;
        List<Enemy> targets = FindTargets();
        if (_grid.GetTileType(_cell) == DeploymentGrid.TileType.EnemyPath)
        {
            foreach (Enemy enemy in targets)
            {
                if (_blocked.Count >= BlockCount) break;
                if (_grid.GlobalToCell(enemy.GlobalPosition) == _cell && enemy.TryBlock(this))
                    _blocked.Add(enemy);
            }
        }
        _cooldown -= (float)delta;
        if (_attacking || _cooldown > 0 || targets.Count == 0) return;
        _sprite.SetFacingLeft(targets[0].GlobalPosition.X < GlobalPosition.X);
        _attacking = true;
        _hitApplied = false;
        _cooldown = Mathf.Max(0.01f, AttackInterval);
        _sprite.SpeedScale = (float)(_sprite.SpriteFrames.GetFrameCount("attack")
            / _sprite.SpriteFrames.GetAnimationSpeed("attack") / _cooldown);
        _sprite.Play("attack");
    }

    private void OnFrameChanged()
    {
        if (!_battleActive || !_attacking || _hitApplied || _sprite.Frame < _hitFrame) return;
        _hitApplied = true;
        List<Enemy> targets = FindTargets();
        int count = TargetLimit == 0 ? targets.Count : Mathf.Min(Mathf.Max(0, TargetLimit), targets.Count);
        for (int i = 0; i < count && _battleActive; i++) targets[i].TakeDamage(Damage);
    }

    private void ResetAnimation()
    {
        _attacking = false;
        _hitApplied = false;
        _sprite.SpeedScale = 1;
        _sprite.Play("idle");
    }

    internal void ForgetBlocked(Enemy enemy) => _blocked.Remove(enemy);

    private void ReleaseAllBlocks()
    {
        foreach (Enemy enemy in _blocked.ToArray())
            if (GodotObject.IsInstanceValid(enemy)) enemy.ReleaseBlock(this);
        _blocked.Clear();
    }

    public override void _ExitTree() => ReleaseAllBlocks();
}
