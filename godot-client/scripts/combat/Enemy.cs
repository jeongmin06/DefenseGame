using Godot;
using DefenseGame.Client.Visuals;

namespace DefenseGame.Client.Combat;

public partial class Enemy : PathFollow2D
{
    [Signal]
    public delegate void DefeatedEventHandler(Enemy enemy);

    [Signal]
    public delegate void ReachedGoalEventHandler(Enemy enemy);

    private float _maxHealth = 1.0f;
    private float _health = 1.0f;
    private float _moveSpeed = 60.0f;
    private bool _resolved;
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
        if (_resolved)
        {
            return;
        }

        Vector2 previousPosition = GlobalPosition;
        Progress += _moveSpeed * (float)delta;
        Vector2 movement = GlobalPosition - previousPosition;
        _sprite.SetFacingFromMovement(movement);
        if (ProgressRatio < 0.999f)
        {
            return;
        }

        _resolved = true;
        EmitSignal(SignalName.ReachedGoal, this);
        QueueFree();
    }

    public void Setup(float maxHealth, float moveSpeed)
    {
        _maxHealth = Mathf.Max(1.0f, maxHealth);
        _health = _maxHealth;
        _moveSpeed = Mathf.Max(1.0f, moveSpeed);
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

        _resolved = true;
        QueueFree();
    }

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
