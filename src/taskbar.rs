//! Taskbar lyric placement.
//!
//! The lyric window is a **top-level `WS_EX_LAYERED` tool window** pinned to the taskbar's
//! **entire screen rect** and kept `HWND_TOPMOST` so it always sits over the bar. Text is
//! rendered with per-pixel alpha, so the taskbar's icons stay visible and click-through in
//! transparent areas; spanning the full bar is what lets left/center/right alignment match
//! the original .NET behavior (which used a full-width window).
//!
//! **Why not a child of `Shell_TrayWnd` / a rebar band?** Both fail in practice for a
//! per-pixel-alpha layered window:
//!  - hosting it as a `ReBarWindow32` band child makes explorer repaint it with a DC it
//!    doesn't own and **crashes Explorer** on startup;
//!  - a `SetParent` child of `Shell_TrayWnd` is owned by our process but rendered inside
//!    explorer's top-level tree, where a foreign `WS_EX_LAYERED` child is **not composited**
//!    — the window reports success but never shows.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Mutex;

use windows::core::PCWSTR;
use windows::Win32::Foundation::{HWND, RECT};
use windows::Win32::Graphics::Gdi::{GetMonitorInfoW, MonitorFromWindow, MONITOR_DEFAULTTONEAREST, MONITORINFO};
use windows::Win32::UI::Shell::{
    ABM_GETTASKBARPOS, APPBARDATA, SHAppBarMessage,
};
use windows::Win32::UI::WindowsAndMessaging::{
    FindWindowW, GetClassNameW, GetForegroundWindow,
    GetWindowRect, MoveWindow, SetWindowPos,
    ShowWindow, SW_HIDE, SW_SHOWNA, SWP_NOACTIVATE, SWP_NOMOVE, SWP_NOSIZE,
    HWND_TOPMOST,
};

use crate::{lyric_hwnd, mark_dirty, SHOWING};

static LAST_TRAY: std::sync::atomic::AtomicIsize = std::sync::atomic::AtomicIsize::new(0);

// Last geometry we actually applied, so `validate()` (every 2 s) and `position_in_tray()` can
// skip the redundant MoveWindow/ShowWindow that forces a repaint/re-show and makes the window
// blink when it coincides with taskbar interaction. Always re-asserting TOPMOST below keeps it
// above the taskbar even if explorer briefly raises the bar, without repositioning.
static LAST_GEOM: Mutex<Option<(i32, i32, i32, i32)>> = Mutex::new(None);

/// Set when a fullscreen app is in the foreground and the user has the auto-hide switch on.
/// Independent of the user's own `SHOWING` toggle so we can hide without losing it.
static FULLSCREEN_HIDDEN: AtomicBool = AtomicBool::new(false);

pub fn fullscreen_hidden() -> bool {
    FULLSCREEN_HIDDEN.load(Ordering::Relaxed)
}

/// True when the foreground window fills its monitor (a borderless fullscreen app / game).
fn is_fullscreen_foreground() -> bool {
    let fg = unsafe { GetForegroundWindow() };
    if fg.0.is_null() {
        return false;
    }
    // Ignore our own lyric/coordinator windows so we never hide on our own messages.
    if fg == lyric_hwnd() {
        return false;
    }
    let cls = class_name_of(fg);
    // The desktop ("Progman" host / "WorkerW" wallpaper layer) and the Start menu span/cover the
    // whole monitor but are *not* fullscreen apps; never auto-hide for them, otherwise clicking the
    // desktop would make the lyric band vanish until the taskbar is clicked again.
    if cls == "Progman" || cls == "WorkerW" || cls == "SHELLDLL_DefView"
        || cls == "Windows.UI.Core.CoreWindow" || cls == "Start"
    {
        return false;
    }
    let mut rc: RECT = unsafe { std::mem::zeroed() };
    if unsafe { GetWindowRect(fg, &mut rc) }.is_err() {
        return false;
    }
    let hmon = unsafe { MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST) };
    let mut mi: MONITORINFO = unsafe { std::mem::zeroed() };
    mi.cbSize = std::mem::size_of::<MONITORINFO>() as u32;
    if unsafe { GetMonitorInfoW(hmon, &mut mi) }.as_bool() != true {
        return false;
    }
    let mr = mi.rcMonitor;
    rc.left <= mr.left && rc.top <= mr.top && rc.right >= mr.right && rc.bottom >= mr.bottom
}

/// Return the window class name of the given HWND (empty on failure).
fn class_name_of(hwnd: HWND) -> String {
    let mut buf = [0u16; 256];
    let n = unsafe { GetClassNameW(hwnd, &mut buf) };
    if n == 0 {
        return String::new();
    }
    let len = buf.iter().position(|&c| c == 0).unwrap_or(buf.len());
    String::from_utf16_lossy(&buf[..len])
}

/// Re-evaluate fullscreen hiding. Called from the redraw timer each tick; cheap (a couple of
/// Win32 calls) and only repositions/hides when the state actually flips.
pub fn update_fullscreen_auto_hide() {
    let enabled = crate::config::auto_hide_fullscreen();
    if !enabled {
        if FULLSCREEN_HIDDEN.swap(false, Ordering::Relaxed) {
            position_in_tray();
        }
        return;
    }
    let hidden = is_fullscreen_foreground();
    if FULLSCREEN_HIDDEN.load(Ordering::Relaxed) != hidden {
        FULLSCREEN_HIDDEN.store(hidden, Ordering::Relaxed);
        position_in_tray();
    }
}

pub fn ensure_topmost() {
    if !crate::SHOWING.load(Ordering::Relaxed) || FULLSCREEN_HIDDEN.load(Ordering::Relaxed) {
        return;
    }
    let h = crate::lyric_hwnd();
    if h.0.is_null() {
        return;
    }
    // Cheap: re-assert TOPMOST without moving/resizing. Only touches atomics, so it is safe to
    // call from the WinEvent hook even if it fires mid-message-dispatch.
    unsafe {
        let _ = SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}

/// Watch for the Start menu / shell coming to the foreground and immediately re-raise the lyric
/// window above the taskbar, so opening Start never buries our band. Uses `WINEVENT_OUTOFCONTEXT`,
/// so the callbacks are dispatched on our own message-pump thread and stay serialized with the
/// timer-driven topmost keep. Hooks are global and remain until the process exits.
pub fn install_startmenu_hook() {
    use windows::Win32::UI::Accessibility::SetWinEventHook;
    use windows::Win32::UI::WindowsAndMessaging::WINEVENT_OUTOFCONTEXT;
    // EVENT_SYSTEM_FOREGROUND (0x0003): fires whenever the foreground window changes.
    let _ = unsafe {
        SetWinEventHook(0x0003, 0x0003, None, Some(start_menu_hook), 0, 0, WINEVENT_OUTOFCONTEXT)
    };
    // EVENT_OBJECT_CREATE (0x8000): catches the Start menu window being created.
    let _ = unsafe {
        SetWinEventHook(0x8000, 0x8000, None, Some(start_menu_hook), 0, 0, WINEVENT_OUTOFCONTEXT)
    };
}

unsafe extern "system" fn start_menu_hook(
    _hook: windows::Win32::UI::Accessibility::HWINEVENTHOOK,
    _event: u32,
    hwnd: HWND,
    _id_object: i32,
    _id_child: i32,
    _id_event_thread: u32,
    _dwms_event_time: u32,
) {
    if hwnd.0.is_null() {
        return;
    }
    let mut buf = [0u16; 128];
    let n = unsafe { GetClassNameW(hwnd, &mut buf) };
    if n <= 0 {
        return;
    }
    let cls = String::from_utf16_lossy(&buf[..n as usize]);
    let is_start = cls == "Windows.UI.Core.CoreWindow"
        || cls == "Start"
        || cls == "Shell_TrayWnd";
    if is_start {
        ensure_topmost();
    }
}

fn wide_z(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0)).collect()
}

fn tray_hwnd() -> HWND {
    let cls = wide_z("Shell_TrayWnd");
    unsafe { FindWindowW(PCWSTR(cls.as_ptr()), PCWSTR::null()) }
        .unwrap_or(HWND(std::ptr::null_mut()))
}

/// Keep the lyric window a normal top-level popup, shown and topmost (absolute coords).
fn dock(hwnd: HWND, w: i32, h: i32, x: i32, y: i32) -> bool {
    unsafe {
        // If the geometry is unchanged, don't MoveWindow/ShowWindow again (that forces a
        // repaint/re-show and is the source of the blink when it lands on a taskbar click).
        // Still re-assert TOPMOST so the window stays above the taskbar.
        let same = {
            let mut g = match LAST_GEOM.lock() {
                Ok(g) => g,
                Err(p) => p.into_inner(),
            };
            let is_same = g.map_or(false, |(lx, ly, lw, lh)| lx == x && ly == y && lw == w && lh == h);
            if is_same {
                let _ = SetWindowPos(
                    hwnd,
                    HWND_TOPMOST,
                    0,
                    0,
                    0,
                    0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE,
                );
            } else {
                *g = Some((x, y, w, h));
            }
            is_same
        };
        if same {
            return false;
        }

        let _ = MoveWindow(hwnd, x, y, w, h, true);
        let _ = ShowWindow(hwnd, SW_SHOWNA);
        let _ = SetWindowPos(
            hwnd,
            HWND_TOPMOST,
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE,
        );
    }
    true
}

/// Taskbar rectangle from SHAppBarMessage (absolute screen coords); returns None if the
/// taskbar is unavailable/auto-hidden in a weird state.
fn taskbar_info() -> Option<RECT> {
    unsafe {
        let mut abd: APPBARDATA = std::mem::zeroed();
        abd.cbSize = std::mem::size_of::<APPBARDATA>() as u32;
        if SHAppBarMessage(ABM_GETTASKBARPOS, &mut abd) == 0 {
            return None;
        }
        Some(abd.rc)
    }
}

/// Recompute and apply docked geometry. The window spans the **entire taskbar rect**
/// (like the original .NET version) so left/center/right alignment works across the full
/// bar width. The surface is per-pixel transparent except where text is drawn, so the
/// taskbar's own icons stay visible and remain click-through.
pub fn position_in_tray() {
    let hwnd = lyric_hwnd();
    if hwnd.0.is_null() {
        return;
    }
    if !SHOWING.load(Ordering::Relaxed) || FULLSCREEN_HIDDEN.load(Ordering::Relaxed) {
        unsafe {
            let _ = ShowWindow(hwnd, SW_HIDE);
        }
        if let Ok(mut g) = LAST_GEOM.lock() {
            *g = None;
        }
        return;
    }

    let Some(rc) = taskbar_info() else { return };
    let bar_w = (rc.right - rc.left).max(1);
    let bar_h = (rc.bottom - rc.top).max(1);
    crate::dlog("dock", format_args!("{bar_w}x{bar_h} @({},{})", rc.left, rc.top));
    if dock(hwnd, bar_w, bar_h, rc.left, rc.top) {
        mark_dirty();
    }
}

/// Entry point: position the top-level lyric window over the taskbar gap and show it.
pub fn init_embed(_hwnd: HWND) -> bool {
    let tray = tray_hwnd();
    if tray.0.is_null() {
        return false;
    }
    LAST_TRAY.store(tray.0 as isize, Ordering::Relaxed);
    position_in_tray();
    true
}

/// Re-home after a taskbar handle change (explorer restart, display change).
pub fn re_embed(_hwnd: HWND) -> bool {
    mark_dirty();
    position_in_tray();
    true
}

/// Refresh position each poll; a changed taskbar handle triggers a full re-home.
pub fn validate() {
    let hwnd = lyric_hwnd();
    if hwnd.0.is_null() {
        return;
    }
    let tray = tray_hwnd();
    let last = LAST_TRAY.load(Ordering::Relaxed);
    if last != 0 && tray.0 as isize != last {
        re_embed(hwnd);
        return;
    }
    position_in_tray();
}