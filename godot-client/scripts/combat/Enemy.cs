using Godot;
using DefenseGame.Client.Data;
using DefenseGame.Client.Visuals;

namespace DefenseGame.Client.Combat;

public partial class Enemy : PathFollow2D
{
    [Signal]
    public delegate void DefeatedEventHandler(Enemy enemy);

    [Signal]
    public delegate void ReachedGoalEventHandler(Enemy enemy);

    public float AttackDamage { get; set; }
    public float AttackInterval { get; set; }
    private double _attackCooldown;
    private bool _battleActive = true;

    private float _maxHealth;
    private float _health;
    private float _moveSpeed;
    private bool _resolved;
    private Warrior? _blocker;
    private DirectionalAnimatedSprite _sprite = null!;
    private ProgressBar _healthBar = null!;

    public override void _Ready()
    {
        _sprite = GetNode<DirectionalAnimatedSprite>("Sprite");
        _healthBar = GetNode<ProgressBar>("HealthBar");
        _sprite.Play("move");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_resolved || !_battleActive)
        {
            return;
        }

        if (_blocker is not null)
        {
            if (GodotObject.IsInstanceValid(_blocker) && !_blocker.IsQueuedForDeletion() && _blocker.IsAlive)
            {
                _attackCooldown -= delta;
                if (_attackCooldown <= 0.000001)
                {
                    _attackCooldown = Mathf.Max(0.01f, AttackInterval);
                    _blocker.TakeDamage(AttackDamage);
                }
                // If the hit kills the blocker, movement resumes next physics frame.
                return;
            }
            ClearBlock();
        }

        Vector2 previousPosition = GlobalPosition;
        Progress += _moveSpeed * (float)delta;
        Vector2 movement = GlobalPosition - previousPosition;
        _sprite.SetFacingFromMovement(movement);
        if (ProgressRatio < 0.999f)
        {
            return;
        }

        ClearBlock();
        _resolved = true;
        EmitSignal(SignalName.ReachedGoal, this);
        QueueFree();
    }

    public void Setup(UnitDefinition definition, float healthOverride = 0, float speedOverride = 0)
    {
        AttackDamage = definition.ActionPower;
        AttackInterval = definition.ActionInterval;
        _maxHealth = healthOverride > 0 ? healthOverride : definition.MaxHealth;
        _health = _maxHealth;
        _moveSpeed = speedOverride > 0 ? speedOverride : definition.MoveSpeed;
        UpdateHealthBar();
    }

    public void TakeDamage(float amount)
    {
        if (_resolved || amount <= 0.0f)
        {
            return;
        }

        _health = Mathf.Max(0.0f, _health - amount);
        UpdateHealthBar();

        if (_health > 0.0f)
        {
            return;
        }

        ClearBlock();
        _resolved = true;
        EmitSignal(SignalName.Defeated, this);
        QueueFree();
    }

    public bool IsActive()
    {
        return !_resolved && _health > 0.0f;
    }

    public void Despawn()
    {
        if (_resolved)
        {
            return;
        }

        ClearBlock();
        _resolved = true;
        QueueFree();
    }

    public bool TryBlock(Warrior warrior)
    {
        if (!_battleActive || !IsActive() || IsQueuedForDeletion() || _blocker is not null
            || !GodotObject.IsInstanceValid(warrior) || warrior.IsQueuedForDeletion() || !warrior.IsAlive) return false;
        _blocker = warrior;
        _attackCooldown = Mathf.Max(0.01f, AttackInterval);
        return true;
    }

    public bool IsBlocked() => _blocker is not null && GodotObject.IsInstanceValid(_blocker);

    public void ReleaseBlock(Warrior warrior)
    {
        if (_blocker == warrior) ClearBlock();
    }

    private void ClearBlock()
    {
        Warrior? owner = _blocker;
        _blocker = null;
        _attackCooldown = 0;
        if (owner is not null && GodotObject.IsInstanceValid(owner)) owner.ForgetBlocked(this);
    }

    public void SetBattleActive(bool active)
    {
        _battleActive = active;
        if (!active) ClearBlock();
    }

    public override void _ExitTree() => ClearBlock();

    private void UpdateHealthBar()
    {
        if (!GodotObject.IsInstanceValid(_healthBar))
        {
            return;
        }

        _healthBar.MaxValue = _maxHealth;
        _healthBar.Value = _health;
        _healthBar.Visible = _health < _maxHealth;
    }
}
