extends CanvasLayer

@onready var wave_label: Label = $TopPanel/WaveLabel
@onready var enemy_label: Label = $TopPanel/EnemyLabel
@onready var score_label: Label = $TopPanel/ScoreLabel
@onready var base_label: Label = $TopPanel/BaseLabel
@onready var result_panel: Panel = $ResultPanel
@onready var result_title: Label = $ResultPanel/ResultTitle
@onready var result_detail: Label = $ResultPanel/ResultDetail
@onready var restart_button: Button = $ResultPanel/RestartButton


func _ready() -> void:
	restart_button.pressed.connect(_restart_stage)


func update_status(
	wave_number: int,
	total_waves: int,
	alive: int,
	defeated: int,
	escaped: int,
	base_health: int
) -> void:
	wave_label.text = "WAVE %02d / %02d" % [wave_number, total_waves]
	enemy_label.text = "ENEMIES  %02d" % alive
	score_label.text = "DEFEATED  %02d   ESCAPED  %02d" % [defeated, escaped]
	base_label.text = "GATE  %02d" % base_health


func show_result(victory: bool, defeated: int, escaped: int) -> void:
	result_panel.visible = true
	result_title.text = "STAGE CLEAR" if victory else "GATE LOST"
	result_title.modulate = Color("ffd166") if victory else Color("ff6b5e")
	result_detail.text = "Defeated %d  /  Escaped %d" % [defeated, escaped]
	restart_button.grab_focus()


func _restart_stage() -> void:
	get_tree().reload_current_scene()
