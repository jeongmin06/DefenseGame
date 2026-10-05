using Godot;

namespace DefenseGame.Client.Combat;

public partial class UnitHealth : Node
{
    [Signal] public delegate void DepletedEventHandler();
    public float MaxHealth { get; private set; }
    public float CurrentHealth { get; private set; }
    public bool IsAlive => CurrentHealth > 0;
    private ProgressBar _bar = null!;

    public override void _Ready()
    {
        _bar = GetNode<ProgressBar>("../HealthBar");
        UpdateBar();
    }

    public void Setup(float maxHealth)
    {
        MaxHealth = maxHealth;
        CurrentHealth = maxHealth;
        UpdateBar();
    }

    public void TakeDamage(float amount)
    {
        if (!IsAlive || !float.IsFinite(amount) || amount <= 0) return;
        CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
        UpdateBar();
        if (!IsAlive) EmitSignal(SignalName.Depleted);
    }

    public void Heal(float amount)
    {
        if (!IsAlive || !float.IsFinite(amount) || amount <= 0) return;
        CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + amount);
        UpdateBar();
    }

    private void UpdateBar()
    {
        _bar.MaxValue = MaxHealth;
        _bar.Value = CurrentHealth;
        _bar.Visible = CurrentHealth < MaxHealth;
    }
}
