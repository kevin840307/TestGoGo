extends RefCounted

# Visual-only focus policy. Minimized windows ALWAYS use reduced rendering,
# regardless of whether optional Alt+Tab/Unfocus savings are enabled.
# Never inspect/pause gameplay, WebSocket, physics, attack or loot processing.
static func should_background(os_focused: bool, minimized: bool, unfocus_enabled: bool) -> bool:
	return minimized or (not os_focused and unfocus_enabled)
