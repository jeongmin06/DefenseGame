using Godot;
using System.Collections.Generic;
using DefenseGame.Client.Visuals;

namespace DefenseGame.Client.Combat;

public partial class Warrior : Node2D
{
    [Export] public float Damage { get; set; } = 6;
    [Export] public float AttackInterval { get; set; } = 0.8f;
    [Export] public int AttackRangeCells { get; set; } = 1;
    [Export] public int TargetLimit { get; set; } = 1;
    [Export] public int BlockCount { get; set; } = 1;
    // Empty uses the Manhattan radius; custom offsets support later attack shapes.
    [Export] public Godot.Collections.Array<Vector2I> AttackCellOffsets { get; set; } = new();

    private readonly List<Enemy> _blocked = new();
    private DeploymentGrid _grid = null!;
    private Vector2I _cell;
    private DirectionalAnimatedSprite _sprite = null!;
    private bool _battleActive;
    private bool _attacking;
    private bool _hitApplied;
    private float _cooldown;
    private const int HitFrame = 4;

    public override void _Ready()
    {
        _sprite = GetNode<DirectionalAnimatedSprite>("AnimatedSprite");
        _sprite.FrameChanged += OnFrameChanged;
        _sprite.AnimationFinished += ResetAnimation;
        _sprite.Play("idle");
    }

    public void Setup(DeploymentGrid grid, Vector2I cell)
    {
        _grid = grid;
        _cell = cell;
    }

    public void SetBattleActive(bool active)
    {
        _battleActive = active;
        if (!active)
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
        if (!_battleActive || !_attacking || _hitApplied || _sprite.Frame < HitFrame) return;
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
