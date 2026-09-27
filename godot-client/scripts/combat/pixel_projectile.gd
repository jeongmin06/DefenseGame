extends Node2D

var _target = null
var _damage := 1.0
var _speed := 620.0


func setup(target, damage: float) -> void:
	_target = target
	_damage = damage
	queue_redraw()


func _process(delta: float) -> void:
	if not is_instance_valid(_target) or not _target.is_active():
		queue_free()
		return

	global_position = global_position.move_toward(_target.global_position, _speed * delta)
	if global_position.distance_squared_to(_target.global_position) <= 100.0:
		_target.take_damage(_damage)
		queue_free()


func _draw() -> void:
	draw_rect(Rect2(-5.0, -2.0, 10.0, 4.0), Color("ffd166"))
	draw_rect(Rect2(-2.0, -4.0, 4.0, 8.0), Color("fff1a8"))
