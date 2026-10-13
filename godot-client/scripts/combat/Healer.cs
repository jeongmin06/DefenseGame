using Godot;
using DefenseGame.Client.Data;
using DefenseGame.Client.Visuals;
using DefenseGame.Client.Skills;

namespace DefenseGame.Client.Combat;

public partial class Healer : Node2D
{
    [Signal] public delegate void DefeatedEventHandler(Healer unit);
    public float HealRange { get; set; }
    public float HealAmount { get; set; }
    public float HealInterval { get; set; }
    public float CurrentHealth => _health.CurrentHealth;
    public float MaxHealth => _health.MaxHealth;
    public bool IsAlive => _health.IsAlive;
    public string CharacterId { get; private set; } = "";
    public UnitFacing Facing { get; private set; } = UnitFacing.Right;
    public Godot.Collections.Array<Vector2I> AttackCellOffsets { get; private set; } = new();
    private UnitHealth _health = null!;
    private DirectionalAnimatedSprite _sprite = null!;
    private bool _battleActive;
    private bool _casting;
    private bool _applied;
    private float _cooldown;
    private int _healFrame;
    private DeploymentGrid? _grid;
    private Vector2I _cell;

    public override void _Ready()
    {
        _health = GetNode<UnitHealth>("UnitHealth");
        _health.Depleted += OnDepleted;
        _sprite = GetNode<DirectionalAnimatedSprite>("AnimatedSprite");
        _sprite.FrameChanged += OnFrameChanged;
        _sprite.AnimationFinished += ResetAnimation;
    }

    public void Setup(UnitDefinition definition)
    {
        Setup(definition, "", null);
    }

    public void Setup(UnitDefinition definition, string characterId, SkillLoadout? loadout)
    {
        SetupCore(definition, characterId, loadout, null, Vector2I.Zero, UnitFacing.Right);
    }

    public void Setup(
        UnitDefinition definition,
        string characterId,
        SkillLoadout? loadout,
        DeploymentGrid grid,
        Vector2I cell,
        UnitFacing facing)
    {
        SetupCore(definition, characterId, loadout, grid, cell, facing);
    }

    private void SetupCore(
        UnitDefinition definition,
        string characterId,
        SkillLoadout? loadout,
        DeploymentGrid? grid,
        Vector2I cell,
        UnitFacing facing)
    {
        CharacterId = characterId;
        Facing = facing;
        _grid = grid;
        _cell = cell;
        AttackCellOffsets = new Godot.Collections.Array<Vector2I>(definition.AttackCellOffsets);
        UnitVisualConfigurator.Apply(_sprite, definition.Visual);
        _sprite.SetFacingLeft(facing == UnitFacing.Left);
        _sprite.Play(UnitVisualConfigurator.IdleAnimation);
        UnitSkillEffectSettings effects = new UnitSkillEffectComposer().Compose(loadout);
        _health.Setup(definition.MaxHealth);
        HealRange = definition.RangePixels;
        HealAmount = definition.ActionPower * effects.HealingMultiplier;
        HealInterval = definition.ActionInterval;
        _healFrame = definition.ActionFrame;
    }

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

    public void SetBattleActive(bool active)
    {
        _battleActive = active && IsAlive;
        if (!_battleActive) ResetAnimation();
    }

    public Node2D? FindHealingTarget()
    {
        Node2D? best = null;
        float lowestHealth = float.MaxValue;
        foreach (Node node in GetTree().GetNodesInGroup("allies"))
        {
            if (node is not Node2D ally || ally.IsQueuedForDeletion() || !IsInHealingRange(ally)) continue;
            UnitHealth? health = ally.GetNodeOrNull<UnitHealth>("UnitHealth");
            if (health is null || !health.IsAlive || health.CurrentHealth >= health.MaxHealth) continue;
            if (health.CurrentHealth < lowestHealth || (health.CurrentHealth == lowestHealth
                && (best is null || ally.GetInstanceId() < best.GetInstanceId())))
            {
                best = ally;
                lowestHealth = health.CurrentHealth;
            }
        }
        return best;
    }

    public bool IsInHealingRange(Node2D ally) => _grid is not null && AttackCellOffsets.Count > 0
        ? GridAttackRange.Contains(_cell, _grid.GlobalToCell(ally.GlobalPosition), AttackCellOffsets, Facing)
        : GlobalPosition.DistanceSquaredTo(ally.GlobalPosition) <= HealRange * HealRange;

    public override void _PhysicsProcess(double delta)
    {
        if (!_battleActive) return;
        _cooldown -= (float)delta;
        if (_casting || _cooldown > 0) return;
        Node2D? target = FindHealingTarget();
        if (target is null) return;
        _sprite.SetFacingLeft(Facing == UnitFacing.Left);
        _casting = true;
        _applied = false;
        _cooldown = Mathf.Max(0.01f, HealInterval);
        _sprite.SpeedScale = (float)(_sprite.SpriteFrames.GetFrameCount(UnitVisualConfigurator.ActionAnimation)
            / _sprite.SpriteFrames.GetAnimationSpeed(UnitVisualConfigurator.ActionAnimation) / _cooldown);
        _sprite.Play(UnitVisualConfigurator.ActionAnimation);
    }

    private void OnFrameChanged()
    {
        if (!_battleActive || !_casting || _applied || _sprite.Frame < _healFrame) return;
        _applied = true;
        Node2D? target = FindHealingTarget();
        if (target is null) return;
        target.GetNode<UnitHealth>("UnitHealth").Heal(HealAmount);
    }

    private void ResetAnimation()
    {
        _casting = false;
        _applied = false;
        _sprite.SpeedScale = 1;
        _sprite.Play(UnitVisualConfigurator.IdleAnimation);
    }
}
