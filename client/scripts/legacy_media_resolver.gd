extends RefCounted

# The original quests reference proprietary *.spr art and *.mp3 audio by
# filename. Converted local assets are opt-in; never resolve arbitrary paths
# received from a packet outside these explicit res:// roots.
const MEDIA_ROOTS = [
    "res://Assets/Quest/LegacyMedia/",
    "res://Assets/Quest/"
]
const IMAGE_EXTENSIONS = ["png", "webp", "jpg", "jpeg"]
const AUDIO_EXTENSIONS = ["mp3", "ogg", "wav"]


static func normalized_stem(raw_name: String) -> String:
    var filename := raw_name.strip_edges().replace("\\", "/")
    if filename.is_empty() or filename.to_lower() == "na":
        return ""
    if filename.begins_with("/") or filename.contains(":") or filename.contains(".."):
        return ""
    if filename.contains("//") or filename.contains("[") or filename.contains("]"):
        return ""

    for ch in filename:
        if not (ch in "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-/ ."):
            return ""

    var extension := filename.get_extension().to_lower()
    if extension not in ["spr", "png", "webp", "jpg", "jpeg", "mp3", "ogg", "wav"]:
        return ""
    return filename.left(filename.length() - extension.length() - 1)


static func find_texture_path(raw_name: String) -> String:
    var stem := normalized_stem(raw_name)
    if stem.is_empty():
        return ""
    for root in MEDIA_ROOTS:
        for extension in IMAGE_EXTENSIONS:
            var candidate := root + stem + "." + extension
            if ResourceLoader.exists(candidate):
                return candidate
    return ""


static func find_audio_path(raw_name: String) -> String:
    var stem := normalized_stem(raw_name)
    if stem.is_empty():
        return ""
    for root in MEDIA_ROOTS:
        for extension in AUDIO_EXTENSIONS:
            var candidate := root + stem + "." + extension
            if ResourceLoader.exists(candidate):
                return candidate

    # Some old cinematic scripts reference SE0136.mp3; converted local
    # clients may already have the corresponding sound-effect WAV.
    var simple_name := stem.get_file().to_upper()
    if simple_name.begins_with("SE") and simple_name.length() == 6:
        var se_path := "res://Assets/Audio/SE/" + simple_name + ".wav"
        if ResourceLoader.exists(se_path):
            return se_path
    return ""
