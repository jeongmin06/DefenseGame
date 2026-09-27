extends PathFollow2D

signal defeated(enemy: Node)
signal reached_goal(enemy: Node)

var _max_health := 1.0
var _health := 1.0
var _move_speed := 60.0
var _resolved := false

@onready var health_bar: ProgressBar = $HealthBar


func setup(max_health: float, move_speed: float) -> void:
	_max_health = max(1.0, max_health)
	_health = _max_health
	_move_speed = max(1.0, move_speed)
	_update_health_bar()


func _physics_process(delta: float) -> void:
	if _resolved:
		return

	progress += _move_speed * delta
	if progress_ratio >= 0.999:
		_resolved = true
		reached_goal.emit(self)
		queue_free()


func take_damage(amount: float) -> void:
	if _resolved or amount <= 0.0:
		return

	_health = max(0.0, _health - amount)
	_update_health_bar()

	if _health <= 0.0:
		_resolved = true
		defeated.emit(self)
		queue_free()


func is_active() -> bool:
	return not _resolved and _health > 0.0


func despawn() -> void:
	if _resolved:
		return

	_resolved = true
	queue_free()


func _update_health_bar() -> void:
	if not is_instance_valid(health_bar):
		return

	health_bar.max_value = _max_health
	health_bar.value = _health
	health_bar.visible = _health < _max_health
