//! Taskbar Lyrics (Rust globus port) — entry point.

mod api;
mod config;
mod lyrics;
mod render;
mod taskbar;
mod wndproc;

use std::sync::atomic::{AtomicBool, AtomicIsize, Ordering};
use std::sync::Mutex;
use std::time::Instant;

use windows::core::PCWSTR;
use windows::Win32::Foundation::{CloseHandle, HWND, STILL_ACTIVE};
use windows::Win32::System::Com::{CoInitializeEx, CoUninitialize, COINIT_APARTMENTTHREADED};
use windows::Win32::System::Threading::{GetExitCodeProcess, OpenProcess, PROCESS_QUERY_LIMITED_INFORMATION};
use windows::Win32::UI::WindowsAndMessaging::{
    DestroyWindow, DispatchMessageW, GetMessageW, MSG, SetProcessDPIAware, TranslateMessage,
};

use lyrics::LyricsLine;

/// Shared playback/lyric state, updated by the two HTTP worker threads and read by
/// the renderer on each frame (mirrors the .NET `_lyricsLines` / `_currentPosition` / `_isPlaying`).
pub struct SharedState {
    pub lines: Vec<LyricsLine>,
    pub pos: i64,
    pub is_playing: bool,
    pub last_lyrics_text: String,
    pub has_data: bool,
}

impl SharedState {
    const fn new() -> Self {
        Self {
            lines: Vec::new(),
            pos: 0,
            is_playing: false,
            last_lyrics_text: String::new(),
            has_data: false,
        }
    }
}

pub static SHARED: Mutex<SharedState> = Mutex::new(SharedState::new());

/// Set whenever something changed and a re-render is warranted (avoids 30fps idle redraws).
pub static DIRTY: AtomicBool = AtomicBool::new(true);
/// Whether the lyric band should be shown (`false` = user hid it via tray toggle).
pub static SHOWING: AtomicBool = AtomicBool::new(true);

/// Handle of the embedded lyric window (taskbar child). Stored so other modules can reach it.
pub static LYRIC_HWND: AtomicIsize = AtomicIsize::new(0);

/// Last playback base (position_ms, playing, speed_factor, wall-clock instant) delivered over
/// SSE, used to extrapolate a smooth continuous position for the karaoke animation.
pub static POS_BASE: Mutex<Option<(i64, bool, f64, Instant)>> = Mutex::new(None);

/// Current smoothed playback position: while playing, advance the position from the last SSE
/// push at real time × speed, so the highlight glides every frame.
pub fn effective_position() -> i64 {
    let mut guard = match POS_BASE.lock() {
        Ok(g) => g,
        Err(p) => p.into_inner(),
    };
    let Some((pos, playing, rate, t0)) = *guard else { return 0 };
    if !playing {
        return pos;
    }
    let dt_ms = t0.elapsed().as_millis() as f64;
    let ext = pos + (dt_ms * rate) as i64;
    // Rebase periodically so long playtimes can't overflow or produce one giant transient.
    if dt_ms > 10_000.0 {
        *guard = Some((ext, playing, rate, Instant::now()));
    }
    ext
}
pub fn set_lyric_hwnd(hwnd: HWND) {
    LYRIC_HWND.store(hwnd.0 as isize, Ordering::Relaxed);
}
pub fn lyric_hwnd() -> HWND {
    HWND(LYRIC_HWND.load(Ordering::Relaxed) as *mut core::ffi::c_void)
}
pub fn mark_dirty() {
    DIRTY.store(true, Ordering::Relaxed);
}

/// Whether event-log debug output is enabled.
pub fn debug_enabled() -> bool {
    static ON: std::sync::OnceLock<bool> = std::sync::OnceLock::new();
    *ON.get_or_init(|| std::env::var("TASKBAR_LYRICS_DEBUG").is_ok())
}

/// Minimal append-only diagnostic log (only written when DEBUG is enabled), so we can
/// diagnose why the band doesn't render without disturbing the GUI.
pub fn dlog(what: &str, v: impl std::fmt::Display) {
    use std::io::Write;
    if !debug_enabled() {
        return;
    }
    if let Ok(mut f) = std::fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(std::env::temp_dir().join("taskbar-lyrics-debug.log"))
    {
        let _ = writeln!(f, "{what}={v}");
    }
}

/// Fetch lyrics (metadata HTTP GET) and parse them into the shared state when they change.
fn fetch_and_update_lyrics() {
    if let Some(raw) = api::fetch_lyrics() {
        let text = raw.trim().to_string();
        let changed = {
            let s = SHARED.lock().unwrap();
            s.last_lyrics_text != text
        };
        if changed {
            let lines = lyrics::filter_lines(lyrics::parse_lyrics(&text), &config::lyric_filter_regex());
            {
                let mut s = SHARED.lock().unwrap();
                s.lines = lines;
                s.has_data = true;
                s.last_lyrics_text = text.clone();
            }
            mark_dirty();
        }
    }
}

/// Handle a single SSE payload (one "state" or "config" event) pushed by the plugin.
fn handle_stream_event(ev: &str) {
    let v: serde_json::Value = match serde_json::from_str(ev) {
        Ok(v) => v,
        Err(_) => return,
    };
    let ty = v.get("type").and_then(|x| x.as_str()).unwrap_or("");
    match ty {
        "config" => {
            if let Some(cfg) = v.get("config").map(|x| x.to_string()) {
                if config::apply_json(&cfg) {
                    mark_dirty();
                }
            }
        }
        "state" => {
            let pos = v.get("position").and_then(|x| x.as_i64()).unwrap_or(0);
            let playing = v.get("isPlaying").and_then(|x| x.as_bool()).unwrap_or(false);
            let rate = v.get("playbackFactor").and_then(|x| x.as_f64()).unwrap_or(1.0);
            let track_changed = v.get("trackChanged").and_then(|x| x.as_bool()).unwrap_or(false);

            if let Ok(mut base) = POS_BASE.lock() {
                *base = Some((pos, playing, rate, Instant::now()));
            }

            // The plugin embeds the new track's lyric in the *first* state push of a track switch
            // (sources: audio metadata then SPW). Apply it immediately so we never show the previous
            // song's opening while a separate /api/lyric GET round-trips (~1s lag).
            let mut lyric_applied = false;
            if track_changed {
                if let Some(lyric) = v.get("lyric").and_then(|x| x.as_str()) {
                    if !lyric.trim().is_empty() {
                        let lines = lyrics::filter_lines(lyrics::parse_lyrics(lyric.trim()), &config::lyric_filter_regex());
                        {
                            let mut s = SHARED.lock().unwrap();
                            s.lines = lines;
                            s.has_data = true;
                            s.last_lyrics_text = lyric.trim().to_string();
                        }
                        lyric_applied = true;
                    }
                }
            }

            let mut s = SHARED.lock().unwrap();
            let changed = s.pos != pos || s.is_playing != playing;
            s.pos = pos;
            s.is_playing = playing;
            let should_fetch =
                !lyric_applied && (track_changed || (s.has_data && s.last_lyrics_text.is_empty()));
            drop(s);

            // Redraw on any state/playback change (including pause) so the visible lyric and
            // controls always reflect the real playback state promptly.
            if changed {
                mark_dirty();
            }
            if should_fetch {
                fetch_and_update_lyrics();
            }
        }
        _ => {}
    }
}

/// One persistent SSE stream worker. Fetches config once, then stays connected to
/// `/api/stream`; any push updates state/config/lyrics. Reconnects on disconnect.
fn spawn_stream_worker() {
    std::thread::spawn(|| {
        loop {
            if let Some(cfg) = api::fetch_config_json() {
                if config::apply_json(&cfg) {
                    mark_dirty();
                }
            }
            let _ = api::run_stream_callback(|ev| handle_stream_event(ev));
            std::thread::sleep(std::time::Duration::from_millis(3000));
        }
    });
}

/// Watch the parent process (Salt Player, passed via TASKBAR_LYRICS_PARENT_PID). If it dies,
/// terminate ourselves so the lyric window doesn't outlive the player even when the plugin's
/// `stop()` is never invoked (e.g. abrupt exit). Uses a raw exit to guarantee teardown.
fn spawn_parent_watcher() {
    let Ok(pid_text) = std::env::var("TASKBAR_LYRICS_PARENT_PID") else { return };
    let Ok(pid) = pid_text.trim().parse::<u32>() else { return };
    std::thread::spawn(move || loop {
        std::thread::sleep(std::time::Duration::from_millis(1500));
        unsafe {
            let alive = match OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid) {
                Ok(h) => {
                    let mut code: u32 = 0;
                    let ok_code = GetExitCodeProcess(h, &mut code).ok();
                    let _ = CloseHandle(h);
                    matches!(ok_code, Some(()) if code == STILL_ACTIVE.0 as u32)
                }
                Err(_) => false,
            };
            if !alive {
                std::process::exit(0);
            }
        }
    });
}

fn main() {
    unsafe {
        // The app is DPI-aware (real pixels -> consistent embedding geometry).
        let _ = SetProcessDPIAware();
        // COM needed internally by DirectWrite/Direct2D.
        let _ = CoInitializeEx(None, COINIT_APARTMENTTHREADED);
    }
    // No local config file anymore: the plugin's official config is the single source of
    // truth, fetched via `/api/config` and pushed live over the SSE stream.
    config::reset_to_default();

    // Coordinator (hidden top-level message window): timers, re-draw loop,
    // TaskbarCreated re-embed. Also carries the renderer pointer in its user data.
    let coord = match wndproc::create_coordinator() {
        Some(h) => h,
        None => {
            unsafe { CoUninitialize(); }
            return;
        }
    };

    // Create the lyric window and embed it into the taskbar.
    let _ = wndproc::create_and_embed_lyric();

    // Watch for the Start menu coming to the foreground and re-raise the lyric window above it.
    taskbar::install_startmenu_hook();

    // One persistent stream connection to the plugin (state + config + lyrics).
    spawn_stream_worker();

    // Die with the parent (Salt Player) so lyrics never outlive the player.
    spawn_parent_watcher();

    unsafe {
        let mut msg = MSG::default();
        while GetMessageW(&mut msg, None, 0, 0).as_bool() {
            let _ = TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }

    let lyr = lyric_hwnd();
    unsafe {
        let _ = DestroyWindow(lyr);
        let _ = DestroyWindow(coord);
        CoUninitialize();
    }
}