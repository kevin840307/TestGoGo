
extends Control


signal emotion_selected(anim_name: String)

@onready var button_container: Control = $ButtonContainer


const EMOTIONS: Array[String] = [
	"emote_a_8", "emote_b_8", "emote_c_8", "emote_d_8", 
	"emote_e_8", "emote_f_8", "emote_g_8", "emote_h_8", 
	"emote_i_8", "emote_j_8", "emote_k_8", "emote_l_8"
]

func _ready() -> void :

	var buttons = button_container.get_children()


	for i in range(min(buttons.size(), EMOTIONS.size())):
		var btn = buttons[i]
		if btn is Button:


			btn.pressed.connect(_on_emotion_button_pressed.bind(EMOTIONS[i]))

func _on_emotion_button_pressed(anim_name: String) -> void :
	emotion_selected.emit(anim_name)
