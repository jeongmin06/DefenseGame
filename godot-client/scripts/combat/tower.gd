extends Node2D

const ProjectileScript = preload("res://scripts/combat/pixel_projectile.gd")

@export var attack_range := 230.0
@export var attack_damage := 4.0
@export var attack_interval := 0.55

var _cooldown := 0.15
var _battle_active := true


func _ready() -> void:
	queue_redraw()


func _process(delta: float) -> void:
	if not _battle_active:
		return

	_cooldown -= delta
	if _cooldown > 0.0:
		return

	var target := _find_nearest_target()
	if target == null:
		return

	_fire(target)
	_cooldown = attack_interval


func set_battle_active(active: bool) -> void:
	_battle_active = active


func _find_nearest_target() -> Node2D:
	var best_target: Node2D
	var best_distance_squared := attack_range * attack_range

	for candidate in get_tree().get_nodes_in_group("enemies"):
		if not candidate.is_active():
			continue

		var distance_squared := global_position.distance_squared_to(candidate.global_position)
		if distance_squared <= best_distance_squared:
			best_distance_squared = distance_squared
			best_target = candidate

	return best_target


func _fire(target: Node2D) -> void:
	var projectile := ProjectileScript.new()
	var projectile_layer := get_tree().current_scene.get_node_or_null("Projectiles")
	if projectile_layer == null:
		projectile_layer = get_tree().current_scene

	projectile_layer.add_child(projectile)
	projectile.global_position = global_position + Vector2(0.0, -28.0)
	projectile.setup(target, attack_damage)


func _draw() -> void:
	draw_arc(Vector2.ZERO, attack_range, 0.0, TAU, 64, Color(1.0, 0.78, 0.28, 0.12), 2.0)
