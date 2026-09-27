extends Node2D

const PATH_POINTS := [
	Vector2(-60.0, 360.0),
	Vector2(180.0, 360.0),
	Vector2(180.0, 150.0),
	Vector2(520.0, 150.0),
	Vector2(520.0, 530.0),
	Vector2(900.0, 530.0),
	Vector2(900.0, 300.0),
	Vector2(1180.0, 300.0),
	Vector2(1340.0, 300.0),
]

const TOWER_POSITIONS := [
	Vector2(355.0, 300.0),
	Vector2(745.0, 390.0),
]

const WAVES := [
	{"count": 5, "health": 12.0, "speed": 72.0, "interval": 1.15},
	{"count": 7, "health": 17.0, "speed": 82.0, "interval": 0.90},
	{"count": 9, "health": 23.0, "speed": 92.0, "interval": 0.72},
]

const MAX_BASE_HEALTH := 5
const WAVE_GAP := 2.0

@export var enemy_scene: PackedScene
@export var tower_scene: PackedScene

var _wave_index := -1
var _spawned_in_wave := 0
var _active_enemies := 0
var _defeated_enemies := 0
var _escaped_enemies := 0
var _base_health := MAX_BASE_HEALTH
var _all_waves_spawned := false
var _battle_ended := false

@onready var enemy_path: Path2D = $EnemyPath
@onready var spawn_timer: Timer = $SpawnTimer
@onready var hud = $HUD


func _ready() -> void:
	_build_enemy_path()
	_spawn_towers()
	spawn_timer.timeout.connect(_spawn_enemy)
	_update_hud()

	await get_tree().create_timer(0.8).timeout
	_start_next_wave()


func _draw() -> void:
	_draw_ground()
	_draw_road()
	_draw_landmarks()


func _build_enemy_path() -> void:
	var curve := Curve2D.new()
	for point in PATH_POINTS:
		curve.add_point(point)
	enemy_path.curve = curve


func _spawn_towers() -> void:
	for tower_position in TOWER_POSITIONS:
		var tower := tower_scene.instantiate() as Node2D
		add_child(tower)
		tower.position = tower_position


func _start_next_wave() -> void:
	if _battle_ended:
		return

	_wave_index += 1
	if _wave_index >= WAVES.size():
		_all_waves_spawned = true
		_check_for_victory()
		return

	_spawned_in_wave = 0
	var wave: Dictionary = WAVES[_wave_index]
	spawn_timer.wait_time = wave["interval"]
	_spawn_enemy()
	if not _battle_ended and _spawned_in_wave < int(wave["count"]):
		spawn_timer.start()
	_update_hud()


func _spawn_enemy() -> void:
	if _battle_ended or _wave_index < 0 or _wave_index >= WAVES.size():
		spawn_timer.stop()
		return

	var wave: Dictionary = WAVES[_wave_index]
	var enemy = enemy_scene.instantiate()
	enemy_path.add_child(enemy)
	enemy.setup(wave["health"], wave["speed"])
	enemy.defeated.connect(_on_enemy_defeated)
	enemy.reached_goal.connect(_on_enemy_reached_goal)

	_spawned_in_wave += 1
	_active_enemies += 1
	_update_hud()

	if _spawned_in_wave >= int(wave["count"]):
		spawn_timer.stop()
		if _wave_index == WAVES.size() - 1:
			_all_waves_spawned = true
			_check_for_victory()
		else:
			_schedule_next_wave()


func _schedule_next_wave() -> void:
	await get_tree().create_timer(WAVE_GAP).timeout
	if not _battle_ended:
		_start_next_wave()


func _on_enemy_defeated(_enemy: Node) -> void:
	_active_enemies = max(0, _active_enemies - 1)
	_defeated_enemies += 1
	_update_hud()
	_check_for_victory()


func _on_enemy_reached_goal(_enemy: Node) -> void:
	_active_enemies = max(0, _active_enemies - 1)
	_escaped_enemies += 1
	_base_health = max(0, _base_health - 1)
	_update_hud()

	if _base_health <= 0:
		_finish_battle(false)
	else:
		_check_for_victory()


func _check_for_victory() -> void:
	if _all_waves_spawned and _active_enemies == 0 and not _battle_ended:
		_finish_battle(true)


func _finish_battle(victory: bool) -> void:
	if _battle_ended:
		return

	_battle_ended = true
	spawn_timer.stop()
	get_tree().call_group("towers", "set_battle_active", false)

	if not victory:
		for enemy in get_tree().get_nodes_in_group("enemies"):
			enemy.despawn()
		_active_enemies = 0

	_update_hud()
	hud.show_result(victory, _defeated_enemies, _escaped_enemies)


func _update_hud() -> void:
	hud.update_status(
		max(0, _wave_index + 1),
		WAVES.size(),
		_active_enemies,
		_defeated_enemies,
		_escaped_enemies,
		_base_health
	)


func _draw_ground() -> void:
	draw_rect(Rect2(0.0, 0.0, 1280.0, 720.0), Color("14261d"))
	for y in range(0, 720, 32):
		for x in range(0, 1280, 32):
			var tile_index := int(x / 32 + y / 32)
			var tint := Color("193024") if tile_index % 2 == 0 else Color("172b21")
			draw_rect(Rect2(x, y, 32, 32), tint)

	for marker in [Vector2(80, 90), Vector2(320, 610), Vector2(720, 90), Vector2(1080, 610), Vector2(1110, 130)]:
		draw_rect(Rect2(marker - Vector2(8, 8), Vector2(16, 16)), Color("244632"))
		draw_rect(Rect2(marker - Vector2(3, 14), Vector2(6, 28)), Color("315c3f"))


func _draw_road() -> void:
	var points := PackedVector2Array(PATH_POINTS)
	draw_polyline(points, Color("3d2d26"), 82.0, false)
	draw_polyline(points, Color("74523b"), 68.0, false)
	draw_polyline(points, Color("9a704b"), 4.0, false)

	for point in PATH_POINTS:
		draw_circle(point, 34.0, Color("74523b"))


func _draw_landmarks() -> void:
	draw_rect(Rect2(0.0, 326.0, 52.0, 68.0), Color("482821"))
	draw_rect(Rect2(10.0, 336.0, 42.0, 48.0), Color("b74732"))
	draw_rect(Rect2(1190.0, 252.0, 90.0, 96.0), Color("242a31"))
	draw_rect(Rect2(1204.0, 266.0, 62.0, 68.0), Color("515866"))
	draw_rect(Rect2(1218.0, 286.0, 34.0, 48.0), Color("181b21"))
