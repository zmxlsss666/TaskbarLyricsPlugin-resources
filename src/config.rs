//! Configuration model.

use serde::{Deserialize, Deserializer, Serialize};
use std::sync::RwLock;

/// Converts a JSON number (seekbar stores Float, e.g. `16.0`) into an integer, rounding the value.
fn de_int<'de, D>(d: D) -> Result<i32, D::Error>
where
    D: Deserializer<'de>,
{
    let v = f64::deserialize(d)?;
    Ok(v.round() as i32)
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(default)]
pub struct LyricsConfig {
    #[serde(rename = "font_family")]
    pub font_family: String,
    #[serde(rename = "font_size", deserialize_with = "de_int")]
    pub font_size: i32,
    #[serde(rename = "font_color")]
    pub font_color: String,
    #[serde(rename = "background_color")]
    pub background_color: String,
    #[serde(rename = "alignment")]
    pub alignment: String,
    #[serde(rename = "show_translation")]
    pub show_translation: bool,
    #[serde(rename = "translation_font_size", deserialize_with = "de_int")]
    pub translation_font_size: i32,
    #[serde(rename = "translation_font_color")]
    pub translation_font_color: String,
    #[serde(rename = "line_spacing", deserialize_with = "de_int")]
    pub line_spacing: i32,
    #[serde(rename = "highlight_color")]
    pub highlight_color: String,
    #[serde(rename = "auto_hide_fullscreen")]
    pub auto_hide_fullscreen: bool,
    #[serde(rename = "position_offset_x", deserialize_with = "de_int")]
    pub position_offset_x: i32,
    #[serde(rename = "position_offset_y", deserialize_with = "de_int")]
    pub position_offset_y: i32,
    #[serde(rename = "lyric_filter_regex")]
    pub lyric_filter_regex: String,
}

impl Default for LyricsConfig {
    fn default() -> Self {
        Self {
            font_family: "MicrosoftYaHei".to_string(),
            font_size: 16,
            font_color: "#FFFFFF".to_string(),
            background_color: "#00000000".to_string(),
            alignment: "center".to_string(),
            show_translation: true,
            translation_font_size: 14,
            translation_font_color: "#CCCCCC".to_string(),
            line_spacing: 2,
            highlight_color: "#00FFFF".to_string(),
            auto_hide_fullscreen: true,
            position_offset_x: 0,
            position_offset_y: 0,
            lyric_filter_regex: String::new(),
        }
    }
}

/// Application-wide configuration guarded by a read-write lock.
/// Lazy-initialized with defaults; only the stream worker mutates it.
pub fn lock() -> &'static RwLock<LyricsConfig> {
    static C: std::sync::OnceLock<RwLock<LyricsConfig>> = std::sync::OnceLock::new();
    C.get_or_init(|| RwLock::new(LyricsConfig::default()))
}

/// Reset to built-in defaults.
pub fn reset_to_default() {
    if let Ok(mut g) = lock().write() {
        *g = LyricsConfig::default();
    }
    crate::mark_dirty();
}

/// Apply a JSON object (the plugin's `getAllConfig()` payload) into the in-memory config.
/// Returns true if any value changed. Does NOT touch the filesystem.
pub fn apply_json(raw: &str) -> bool {
    let mut cfg = match serde_json::from_str::<LyricsConfig>(raw.trim()) {
        Ok(c) => c,
        Err(_) => return false,
    };
    // Normalize alignment to a canonical lowercase token.
    match cfg.alignment.trim().to_lowercase().as_str() {
        "left" => cfg.alignment = "left".to_string(),
        "right" => cfg.alignment = "right".to_string(),
        _ => cfg.alignment = "center".to_string(),
    }
    let changed = !same_enough(&lock().read().map(|g| g.clone()).unwrap_or_default(), &cfg);
    if changed {
        if let Ok(mut g) = lock().write() {
            *g = cfg;
        }
    }
    changed
}

fn same_enough(a: &LyricsConfig, b: &LyricsConfig) -> bool {
    a.font_family == b.font_family
        && a.font_size == b.font_size
        && a.font_color == b.font_color
        && a.background_color == b.background_color
        && a.alignment == b.alignment
        && a.show_translation == b.show_translation
        && a.translation_font_size == b.translation_font_size
        && a.translation_font_color == b.translation_font_color
        && a.line_spacing == b.line_spacing
        && a.highlight_color == b.highlight_color
        && a.auto_hide_fullscreen == b.auto_hide_fullscreen
        && a.position_offset_x == b.position_offset_x
        && a.position_offset_y == b.position_offset_y
        && a.lyric_filter_regex == b.lyric_filter_regex
}

/// The current lyric filter regex (empty means "no filter").
pub fn lyric_filter_regex() -> String {
    lock().read().map(|g| g.lyric_filter_regex.clone()).unwrap_or_default()
}

/// Whether fullscreen auto-hide is enabled.
pub fn auto_hide_fullscreen() -> bool {
    lock().read().map(|g| g.auto_hide_fullscreen).unwrap_or(true)
}

/// Current alignment token ("left" | "center" | "right").
pub fn alignment() -> String {
    lock().read().map(|g| g.alignment.clone()).unwrap_or_else(|_| "center".to_string())
}

/// Current horizontal position offset (base value from config, before any live drag).
pub fn position_offset_x() -> i32 {
    lock().read().map(|g| g.position_offset_x).unwrap_or(0)
}

/// Current vertical position offset (base value from config, before any live drag).
pub fn position_offset_y() -> i32 {
    lock().read().map(|g| g.position_offset_y).unwrap_or(0)
}

/// Update the in-memory position offset.
pub fn set_position_offset(x: i32, y: i32) {
    if let Ok(mut g) = lock().write() {
        g.position_offset_x = x;
        g.position_offset_y = y;
    }
}

// ---- Color parsing: "#RRGGBB" / "#RGB" / "#AARRGGBB" -> (r,g,b,a) in 0..=255 ----
// Tolerates leading '#', surrounding whitespace, and 3-digit shorthand.

pub fn parse_color(s: &str) -> Option<(u8, u8, u8, u8)> {
    let s = s.trim().trim_start_matches('#');
    match s.len() {
        3 => {
            // "#RGB".
            let v = u32::from_str_radix(s, 16).ok()?;
            let r = ((v >> 8) & 0xF) as u8 * 17;
            let g = ((v >> 4) & 0xF) as u8 * 17;
            let b = (v & 0xF) as u8 * 17;
            Some((r, g, b, 0xFF))
        }
        6 => {
            let v = u32::from_str_radix(s, 16).ok()?;
            let r = ((v >> 16) & 0xFF) as u8;
            let g = ((v >> 8) & 0xFF) as u8;
            let b = (v & 0xFF) as u8;
            Some((r, g, b, 0xFF))
        }
        8 => {
            // "#AARRGGBB".
            let v = u32::from_str_radix(s, 16).ok()?;
            let a = ((v >> 24) & 0xFF) as u8;
            let r = ((v >> 16) & 0xFF) as u8;
            let g = ((v >> 8) & 0xFF) as u8;
            let b = (v & 0xFF) as u8;
            Some((r, g, b, a))
        }
        _ => None,
    }
}