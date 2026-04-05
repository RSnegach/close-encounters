## budget_bar.gd
## Displays the player's remaining build budget as a colour-coded progress
## bar with a textual amount underneath.  Turns green / yellow / red as the
## budget is consumed.
##
## Usage:
##   var bar = BudgetBar.new()
##   parent.add_child(bar)
##   bar.setup(3000)          # $3,000 total budget
##   bar.update_spent(800)    # player has spent $800 so far
##
## Built entirely in code — no .tscn dependency.
extends VBoxContainer
class_name BudgetBar

# ─── Theme constants ─────────────────────────────────────────────────────────
const COLOR_TEXT: Color   = Color("#eeeeee")
const COLOR_GREEN: Color  = Color("#4ecca3")
const COLOR_YELLOW: Color = Color("#f0c040")
const COLOR_RED: Color    = Color("#e94560")
const COLOR_BAR_BG: Color = Color("#222244")

# ─── State ───────────────────────────────────────────────────────────────────
var total_budget: int  = 0     ## Maximum budget for the match.
var spent: int         = 0     ## How much the player has spent so far.
var is_unlimited: bool = false ## True when budget <= 0 (unlimited mode).

# ─── UI nodes ────────────────────────────────────────────────────────────────
var budget_label: Label         ## "Budget" heading.
var progress_bar: ProgressBar   ## Visual remaining-funds bar.
var amount_label: Label         ## "$1,200 / $3,000" text.


## Create the label, progress bar, and amount text.
func _ready() -> void:
	add_theme_constant_override("separation", 4)

	# Heading
	budget_label = Label.new()
	budget_label.text = "Budget"
	budget_label.add_theme_font_size_override("font_size", 14)
	budget_label.add_theme_color_override("font_color", COLOR_TEXT)
	add_child(budget_label)

	# Progress bar
	progress_bar = ProgressBar.new()
	progress_bar.custom_minimum_size = Vector2(0, 22)
	progress_bar.show_percentage = false
	# Style the bar background
	var bg_style: StyleBoxFlat = StyleBoxFlat.new()
	bg_style.bg_color = COLOR_BAR_BG
	bg_style.corner_radius_top_left     = 4
	bg_style.corner_radius_top_right    = 4
	bg_style.corner_radius_bottom_left  = 4
	bg_style.corner_radius_bottom_right = 4
	progress_bar.add_theme_stylebox_override("background", bg_style)
	# Style the fill — will be re-coloured dynamically
	var fill_style: StyleBoxFlat = StyleBoxFlat.new()
	fill_style.bg_color = COLOR_GREEN
	fill_style.corner_radius_top_left     = 4
	fill_style.corner_radius_top_right    = 4
	fill_style.corner_radius_bottom_left  = 4
	fill_style.corner_radius_bottom_right = 4
	progress_bar.add_theme_stylebox_override("fill", fill_style)
	add_child(progress_bar)

	# Dollar-amount text
	amount_label = Label.new()
	amount_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	amount_label.add_theme_font_size_override("font_size", 14)
	amount_label.add_theme_color_override("font_color", COLOR_TEXT)
	add_child(amount_label)

	# Initial display
	_refresh_display()


## Initialise the bar with the total budget for this match.
## Pass 0 (or a negative number) for unlimited mode.
func setup(budget: int) -> void:
	total_budget = budget
	is_unlimited = (budget <= 0)
	spent = 0
	_refresh_display()


## Update how much has been spent so far.  The bar and text update to match.
func update_spent(new_spent: int) -> void:
	spent = new_spent
	_refresh_display()


## Recalculate the bar value, text, and colour.
func _refresh_display() -> void:
	if is_unlimited:
		# Unlimited budget — bar stays full, text says "Unlimited"
		progress_bar.max_value = 100
		progress_bar.value     = 100
		amount_label.text      = "Unlimited"
		_set_bar_color(COLOR_GREEN)
		return

	var remaining: int = total_budget - spent
	progress_bar.max_value = total_budget
	progress_bar.value     = maxi(remaining, 0)
	amount_label.text = "$%s / $%s" % [_format_number(remaining), _format_number(total_budget)]

	# Colour based on percentage remaining
	var pct: float = float(remaining) / float(total_budget) if total_budget > 0 else 0.0
	if pct > 0.50:
		_set_bar_color(COLOR_GREEN)
	elif pct > 0.25:
		_set_bar_color(COLOR_YELLOW)
	else:
		_set_bar_color(COLOR_RED)


## Change the fill colour of the progress bar.
func _set_bar_color(color: Color) -> void:
	var fill: StyleBoxFlat = progress_bar.get_theme_stylebox("fill") as StyleBoxFlat
	if fill:
		fill.bg_color = color


## Format an integer with comma thousands-separators (e.g. 3000 -> "3,000").
## Handles negative numbers by prepending a minus sign.
func _format_number(n: int) -> String:
	var negative: bool = n < 0
	var s: String = str(absi(n))
	var result: String = ""
	var count: int = 0
	# Walk backwards through the digit string
	for i: int in range(s.length() - 1, -1, -1):
		if count > 0 and count % 3 == 0:
			result = "," + result
		result = s[i] + result
		count += 1
	if negative:
		result = "-" + result
	return result
