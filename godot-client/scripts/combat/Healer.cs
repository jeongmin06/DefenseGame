using Godot;
using DefenseGame.Client.Data;
using DefenseGame.Client.Visuals;

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
    private UnitHealth _health = null!;
    private DirectionalAnimatedSprite _sprite = null!;
    private bool _battleActive;
    private bool _casting;
    private bool _applied;
    private float _cooldown;
    private int _healFrame;

    public override void _Ready()
    {
        _health = GetNode<UnitHealth>("UnitHealth");
        _health.Depleted += OnDepleted;
        _sprite = GetNode<DirectionalAnimatedSprite>("AnimatedSprite");
        _sprite.FrameChanged += OnFrameChanged;
        _sprite.AnimationFinished += ResetAnimation;
        _sprite.Play("idle");
    }

    public void Setup(UnitDefinition definition)
    {
        _health.Setup(definition.MaxHealth);
        HealRange = definition.RangePixels;
        HealAmount = definition.ActionPower;
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
            if (node is not Node2D ally || ally.IsQueuedForDeletion()
                || GlobalPosition.DistanceSquaredTo(ally.GlobalPosition) > HealRange * HealRange) continue;
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

    public override void _PhysicsProcess(double delta)
    {
        if (!_battleActive) return;
        _cooldown -= (float)delta;
        if (_casting || _cooldown > 0) return;
        Node2D? target = FindHealingTarget();
        if (target is null) return;
        _sprite.SetFacingLeft(target.GlobalPosition.X < GlobalPosition.X);
        _casting = true;
        _applied = false;
        _cooldown = Mathf.Max(0.01f, HealInterval);
        _sprite.SpeedScale = (float)(_sprite.SpriteFrames.GetFrameCount("cast")
            / _sprite.SpriteFrames.GetAnimationSpeed("cast") / _cooldown);
        _sprite.Play("cast");
    }

    private void OnFrameChanged()
    {
        if (!_battleActive || !_casting || _applied || _sprite.Frame < _healFrame) return;
        _applied = true;
        Node2D? target = FindHealingTarget();
        if (target is null) return;
        _sprite.SetFacingLeft(target.GlobalPosition.X < GlobalPosition.X);
        target.GetNode<UnitHealth>("UnitHealth").Heal(HealAmount);
    }

    private void ResetAnimation()
    {
        _casting = false;
        _applied = false;
        _sprite.SpeedScale = 1;
        _sprite.Play("idle");
    }
}
