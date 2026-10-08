using Godot;
using DefenseGame.Client.Data;
using DefenseGame.Client.Visuals;

namespace DefenseGame.Client.Combat;

public partial class Tower : Node2D
{
    [Signal] public delegate void DefeatedEventHandler(Tower unit);
    private UnitHealth _health = null!;
    public float CurrentHealth => _health.CurrentHealth;
    public float MaxHealth => _health.MaxHealth;
    public bool IsAlive => _health.IsAlive;

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

    public float AttackRange { get; set; }

    public float AttackDamage { get; set; }

    public float AttackInterval { get; set; }

    private float _cooldown = 0.15f;
    private bool _battleActive = true;
    private bool _attackInProgress;
    private bool _projectileReleased;
    private bool _facingLeft;
    private Enemy? _pendingTarget;
    private DirectionalAnimatedSprite _sprite = null!;

    private int _releaseFrame;
    private RangedAttack _rangedAttack = null!;

    public override void _Ready()
    {
        _health = GetNode<UnitHealth>("UnitHealth");
        _health.Depleted += OnDepleted;
        _rangedAttack = GetNode<RangedAttack>("RangedAttack");
        _sprite = GetNode<DirectionalAnimatedSprite>("AnimatedSprite");
        _sprite.FrameChanged += OnSpriteFrameChanged;
        _sprite.AnimationFinished += OnSpriteAnimationFinished;
        _sprite.Play("idle");
        QueueRedraw();
    }

    public void Setup(UnitDefinition definition)
    {
        SetupRanged(definition, new RangedAttackSettings());
    }

    public void SetupRanged(UnitDefinition definition, RangedAttackSettings settings)
    {
        _health.Setup(definition.MaxHealth);
        AttackRange = definition.RangePixels;
        AttackDamage = definition.ActionPower;
        AttackInterval = definition.ActionInterval;
        _releaseFrame = definition.ActionFrame;
        _rangedAttack.ConfigureWithSettings(definition, settings);
        QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_battleActive)
        {
            return;
        }

        _cooldown -= (float)delta;
        if (_attackInProgress)
        {
            return;
        }

        if (_cooldown > 0.0f)
        {
            return;
        }

        Enemy? target = FindFrontmostTarget();
        if (target is null)
        {
            return;
        }

        StartAttack(target);
        _cooldown = AttackInterval;
    }

    public void SetBattleActive(bool active)
    {
        _battleActive = active && IsAlive;
        if (!_battleActive)
        {
            ResetToIdle();
        }
    }

    private Enemy? FindFrontmostTarget(bool? facingLeft = null)
    {
        Enemy? bestTarget = null;
        float bestProgress = -1.0f;
        ulong bestInstanceId = ulong.MaxValue;
        float rangeSquared = AttackRange * AttackRange;

        foreach (Node candidateNode in GetTree().GetNodesInGroup("enemies"))
        {
            if (candidateNode is not Enemy candidate || !candidate.IsActive())
            {
                continue;
            }

            if (GlobalPosition.DistanceSquaredTo(candidate.GlobalPosition) > rangeSquared)
            {
                continue;
            }

            bool candidateIsLeft = candidate.GlobalPosition.X < GlobalPosition.X;
            if (facingLeft.HasValue && candidateIsLeft != facingLeft.Value)
            {
                continue;
            }

            ulong instanceId = candidate.GetInstanceId();
            bool isFurtherAhead = candidate.Progress > bestProgress;
            bool winsTie = Mathf.IsEqualApprox(candidate.Progress, bestProgress) && instanceId < bestInstanceId;
            if (!isFurtherAhead && !winsTie)
            {
                continue;
            }

            bestTarget = candidate;
            bestProgress = candidate.Progress;
            bestInstanceId = instanceId;
        }

        return bestTarget;
    }

    private void StartAttack(Enemy target)
    {
        _pendingTarget = target;
        _projectileReleased = false;
        _attackInProgress = true;
        _facingLeft = target.GlobalPosition.X < GlobalPosition.X;
        _sprite.SetFacingLeft(_facingLeft);

        int frameCount = _sprite.SpriteFrames.GetFrameCount("attack");
        double framesPerSecond = _sprite.SpriteFrames.GetAnimationSpeed("attack");
        double baseDuration = frameCount / framesPerSecond;
        _sprite.SpeedScale = (float)(baseDuration / Mathf.Max(0.01f, AttackInterval));
        _sprite.Play("attack");
    }

    private void OnSpriteFrameChanged()
    {
        if (!_battleActive || !IsAlive || _sprite.Animation != "attack" || _sprite.Frame < _releaseFrame || _projectileReleased)
        {
            return;
        }

        _projectileReleased = true;
        Enemy? target = _pendingTarget;
        if (target is null || !GodotObject.IsInstanceValid(target) || !target.IsActive())
        {
            target = FindFrontmostTarget(_facingLeft);
        }

        if (target is not null)
        {
            _rangedAttack.Fire(target, AttackDamage, _facingLeft);
        }
    }

    private void OnSpriteAnimationFinished()
    {
        if (_sprite.Animation == "attack")
        {
            ResetToIdle();
        }
    }

    private void ResetToIdle()
    {
        _attackInProgress = false;
        _projectileReleased = false;
        _pendingTarget = null;
        _sprite.SpeedScale = 1.0f;
        _sprite.Play("idle");
    }

    public override void _Draw()
    {
        DrawArc(Vector2.Zero, AttackRange, 0.0f, Mathf.Tau, 64, new Color(1.0f, 0.78f, 0.28f, 0.12f), 2.0f);
    }
}
