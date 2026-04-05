## main_menu.gd
## Main menu screen for Close Encounters.
## Provides Solo, Host, Join, Settings, and Quit buttons.
## The join flow pops up a dialog asking for a server IP address.
##
## Expected scene tree (built programmatically in _ready):
##   MainMenu (Control — this script)
##     └ VBoxContainer (centered on screen)
##         ├ TitleLabel
##         ├ SoloButton
##         ├ HostButton
##         ├ JoinButton
##         ├ SettingsButton
##         └ QuitButton
##     └ JoinDialog (ConfirmationDialog with an IP LineEdit)
extends Control

# ─── Theme constants ─────────────────────────────────────────────────────────
const COLOR_BG: Color       = Color("#1a1a2e")
const COLOR_ACCENT: Color   = Color("#e94560")
const COLOR_SECONDARY: Color = Color("#0f3460")
const COLOR_TEXT: Color     = Color("#eeeeee")

# ─── Node references (assigned after programmatic creation) ──────────────────
var vbox: VBoxContainer            ## Central column holding all buttons.
var title_label: Label             ## Game title at the top.
var solo_btn: Button               ## Start a solo match against AI.
var host_btn: Button               ## Host a multiplayer lobby.
var join_btn: Button               ## Open the join-by-IP dialog.
var settings_btn: Button           ## Open settings (placeholder).
var quit_btn: Button               ## Quit the application.
var join_dialog: ConfirmationDialog ## Popup asking for the server IP.
var ip_input: LineEdit             ## Text field inside the join dialog.


## Build the entire menu UI, wire up button signals, and tell GameManager
## we are on the main-menu screen.
func _ready() -> void:
	# -- Full-screen background colour --
	_set_background(COLOR_BG)

	# -- Central VBoxContainer --
	vbox = VBoxContainer.new()
	vbox.set_anchors_preset(Control.PRESET_CENTER)          # centre on screen
	vbox.grow_horizontal = Control.GROW_DIRECTION_BOTH
	vbox.grow_vertical   = Control.GROW_DIRECTION_BOTH
	vbox.custom_minimum_size = Vector2(320, 0)
	vbox.add_theme_constant_override("separation", 16)      # spacing between items
	add_child(vbox)

	# -- Title --
	title_label = Label.new()
	title_label.text = "CLOSE ENCOUNTERS"
	title_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title_label.add_theme_font_size_override("font_size", 48)
	title_label.add_theme_color_override("font_color", COLOR_ACCENT)
	vbox.add_child(title_label)

	# Small spacer between title and buttons
	var spacer: Control = Control.new()
	spacer.custom_minimum_size = Vector2(0, 24)
	vbox.add_child(spacer)

	# -- Menu buttons --
	solo_btn     = _create_menu_button("Solo Match")
	host_btn     = _create_menu_button("Host Game")
	join_btn     = _create_menu_button("Join Game")
	settings_btn = _create_menu_button("Settings")
	quit_btn     = _create_menu_button("Quit")

	# -- Join dialog (hidden until player clicks Join) --
	join_dialog = ConfirmationDialog.new()
	join_dialog.title = "Join Game"
	join_dialog.min_size = Vector2(360, 140)

	# Inner VBox so the LineEdit has a label above it
	var dialog_vbox: VBoxContainer = VBoxContainer.new()
	dialog_vbox.add_theme_constant_override("separation", 8)

	var ip_label: Label = Label.new()
	ip_label.text = "Server IP Address:"
	ip_label.add_theme_color_override("font_color", COLOR_TEXT)
	dialog_vbox.add_child(ip_label)

	ip_input = LineEdit.new()
	ip_input.placeholder_text = "e.g. 192.168.1.10"
	ip_input.custom_minimum_size = Vector2(300, 0)
	dialog_vbox.add_child(ip_input)

	join_dialog.add_child(dialog_vbox)
	add_child(join_dialog)

	# -- Signal connections --
	solo_btn.pressed.connect(_on_solo_pressed)
	host_btn.pressed.connect(_on_host_pressed)
	join_btn.pressed.connect(_on_join_pressed)
	settings_btn.pressed.connect(_on_settings_pressed)
	quit_btn.pressed.connect(_on_quit_pressed)
	join_dialog.confirmed.connect(_on_join_confirmed)

	# -- Tell GameManager we are at the main menu --
	GameManager.change_state(GameManager.current_state.MAIN_MENU if typeof(GameManager.current_state) == TYPE_INT else GameManager.MAIN_MENU)


## Start a solo match — set mode and move to the lobby scene.
func _on_solo_pressed() -> void:
	GameManager.match_settings["mode"] = "solo"
	GameManager.change_scene("res://scenes/lobby.tscn")


## Host a multiplayer game — tell NetworkManager, then open the lobby.
func _on_host_pressed() -> void:
	GameManager.match_settings["mode"] = "host"
	NetworkManager.host_game()
	GameManager.change_scene("res://scenes/lobby.tscn")


## Show the join dialog so the player can type an IP address.
func _on_join_pressed() -> void:
	ip_input.text = ""       # clear any previous entry
	join_dialog.popup_centered()


## Player confirmed the IP in the join dialog — connect and go to lobby.
func _on_join_confirmed() -> void:
	var ip: String = ip_input.text.strip_edges()
	if ip.is_empty():
		return                # ignore empty input
	GameManager.match_settings["mode"] = "join"
	NetworkManager.join_game(ip)
	GameManager.change_scene("res://scenes/lobby.tscn")


## Placeholder for a future settings screen.
func _on_settings_pressed() -> void:
	# TODO: open settings scene / popup
	pass


## Quit the game immediately.
func _on_quit_pressed() -> void:
	get_tree().quit()


# ─── Helpers ─────────────────────────────────────────────────────────────────

## Create a styled menu button, add it to the VBox, and return it.
func _create_menu_button(text: String) -> Button:
	var btn: Button = Button.new()
	btn.text = text
	btn.custom_minimum_size = Vector2(280, 48)
	btn.add_theme_font_size_override("font_size", 22)
	btn.add_theme_color_override("font_color", COLOR_TEXT)
	# StyleBoxFlat for normal state
	var style_normal: StyleBoxFlat = StyleBoxFlat.new()
	style_normal.bg_color = COLOR_SECONDARY
	style_normal.corner_radius_top_left     = 6
	style_normal.corner_radius_top_right    = 6
	style_normal.corner_radius_bottom_left  = 6
	style_normal.corner_radius_bottom_right = 6
	style_normal.content_margin_top    = 8
	style_normal.content_margin_bottom = 8
	btn.add_theme_stylebox_override("normal", style_normal)
	# Hover state — slightly lighter
	var style_hover: StyleBoxFlat = style_normal.duplicate()
	style_hover.bg_color = COLOR_SECONDARY.lightened(0.15)
	btn.add_theme_stylebox_override("hover", style_hover)
	# Pressed state — accent colour
	var style_pressed: StyleBoxFlat = style_normal.duplicate()
	style_pressed.bg_color = COLOR_ACCENT
	btn.add_theme_stylebox_override("pressed", style_pressed)
	vbox.add_child(btn)
	return btn


## Fill the root Control with a solid background colour.
func _set_background(color: Color) -> void:
	var bg: StyleBoxFlat = StyleBoxFlat.new()
	bg.bg_color = color
	add_theme_stylebox_override("panel", bg)
	# Also set a ColorRect behind everything as a fallback
	var rect: ColorRect = ColorRect.new()
	rect.color = color
	rect.set_anchors_preset(Control.PRESET_FULL_RECT)
	rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(rect)
	move_child(rect, 0)   # send to back
