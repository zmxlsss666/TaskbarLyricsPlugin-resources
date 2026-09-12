//! Window procedures & window creation (coordinator window + lyric band window).

use std::sync::atomic::Ordering;

use windows::Win32::Foundation::{HINSTANCE, HWND, LPARAM, LRESULT, RECT, WPARAM};
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::UI::Input::KeyboardAndMouse::{
    ReleaseCapture, SetCapture, TrackMouseEvent, TRACKMOUSEEVENT, TRACKMOUSEEVENT_FLAGS, TME_LEAVE,
};
use windows::Win32::UI::WindowsAndMessaging::{
    CreateWindowExW, DefWindowProcW, GetClientRect, GetWindowLongPtrW, GWLP_USERDATA,
    RegisterClassExW, RegisterWindowMessageW, KillTimer, GetWindowRect, SetTimer, SetWindowPos,
    SetWindowLongPtrW, ShowWindow, SWP_NOACTIVATE, SWP_NOSIZE, SWP_NOZORDER, SW_SHOWNA,
    WNDCLASSEXW, WNDCLASS_STYLES, WS_EX_LAYERED, WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW, WS_POPUP,
    HCURSOR, IDC_ARROW, LoadCursorW, SetCursor, WM_SETCURSOR, CS_DBLCLKS,
};
use windows::Win32::UI::WindowsAndMessaging::{
    HWND_TOP, WM_DISPLAYCHANGE, WM_ERASEBKGND, WM_LBUTTONDBLCLK, WM_LBUTTONDOWN, WM_LBUTTONUP,
    WM_MOUSEMOVE, WM_TIMER,
};

use crate::render::{self, Renderer};
use crate::{lyric_hwnd, mark_dirty, set_lyric_hwnd, SHOWING};
use crate::taskbar;

const WM_MOUSELEAVE: u32 = 0x02A3;
const WM_NCHITTEST: u32 = 0x0084;
// Hit-test result codes used by WM_NCHITTEST.
const HT_CLIENT: isize = 1;
const HT_TRANSPARENT: isize = -1;

// MK_LBUTTON = 0x0001 (not exported by the windows crate under either message module).
const MK_LBUTTON: u32 = 0x0001;

const T_REDRAW: usize = 1;
const T_VALIDATE: usize = 2;
// Long-press timer in the lyric window (drag-to-position). Values are per-window so 3 is free.
const T_ADJUST: usize = 3;
// How long to hold the button over the lyric before entering drag-to-position mode.
const LONG_PRESS_MS: u32 = 500;

// Clamp bounds for the position offset written back to config (matches the plugin slider range).
const OFFSET_X_MIN: i32 = -700;
const OFFSET_X_MAX: i32 = 700;
const OFFSET_Y_MIN: i32 = -300;
const OFFSET_Y_MAX: i32 = 300;

// Drag anchor (mouse position when drag began).
static DRAG_LAST_X: std::sync::atomic::AtomicI32 = std::sync::atomic::AtomicI32::new(0);
static DRAG_LAST_Y: std::sync::atomic::AtomicI32 = std::sync::atomic::AtomicI32::new(0);

// Control panel button geometry (keep in sync with render::BTN_SIZE/BTN_GAP).
const BTN_SIZE: i32 = 30;
const BTN_GAP: i32 = 6;

static TASKBAR_CREATED_MSG: std::sync::atomic::AtomicU32 = std::sync::atomic::AtomicU32::new(0);

fn x_lparam(v: isize) -> i32 {
    (v & 0xFFFF) as i16 as i32
}
fn y_lparam(v: isize) -> i32 {
    ((v >> 16) & 0xFFFF) as i16 as i32
}

/// The normal arrow cursor. Returning a real cursor from `WM_SETCURSOR` guarantees hovering
/// the band never shows the busy/hourglass sprite (a class whose `hCursor` is NULL inherits
/// whatever cursor was current and can fall back to the spinner).
fn arrow_cursor() -> HCURSOR {
    unsafe { LoadCursorW(None, IDC_ARROW).unwrap_or_default() }
}

// ---------------------------------------------------------------------------
// Coordinator window
// ---------------------------------------------------------------------------

pub unsafe extern "system" fn coord_proc(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    unsafe {
        let renderer_ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut Renderer;
        let taskbar_msg = TASKBAR_CREATED_MSG.load(Ordering::Relaxed);

        if msg == WM_DISPLAYCHANGE || (taskbar_msg != 0 && msg == taskbar_msg) {
            taskbar::re_embed(lyric_hwnd());
            mark_dirty();
            return LRESULT(0);
        }

        match msg {
            WM_TIMER if (wparam.0 as usize) == T_REDRAW => {
                // If a fullscreen app is in the foreground, hide the band (config-gated).
                taskbar::update_fullscreen_auto_hide();
                if SHOWING.load(Ordering::Relaxed) && !taskbar::fullscreen_hidden() {
                    // While playing, redraw every tick so the interpolated karaoke highlight
                    // animates at the timer rate; otherwise only when something changed.
                    let playing = crate::SHARED.lock().unwrap().is_playing;
                    let dirty = crate::DIRTY.swap(false, Ordering::Relaxed);
                    if playing || dirty {
                        if !renderer_ptr.is_null() {
                            let r = &mut *renderer_ptr;
                            let lw = lyric_hwnd();
                            if !lw.0.is_null() {
                                r.draw_frame(lw);
                            }
                        }
                    }
                }
                LRESULT(0)
            }
            WM_TIMER if (wparam.0 as usize) == T_VALIDATE => {
                // Refresh dock geometry (taskbar moved / display changed).
                taskbar::validate();
                LRESULT(0)
            }
            WM_TIMER => LRESULT(0),
            _ => DefWindowProcW(hwnd, msg, wparam, lparam),
        }
    }
}

/// Create the hidden coordinator window and return it.
pub fn create_coordinator() -> Option<HWND> {
    unsafe {
        let inst = HINSTANCE(GetModuleHandleW(None).ok()?.0);
        let class = windows::core::w!("TaskbarLyricsCoord");

        let wc = WNDCLASSEXW {
            cbSize: std::mem::size_of::<WNDCLASSEXW>() as u32,
            style: WNDCLASS_STYLES(0),
            lpfnWndProc: Some(coord_proc),
            cbClsExtra: 0,
            cbWndExtra: 0,
            hInstance: inst,
            hIcon: Default::default(),
            hCursor: Default::default(),
            hbrBackground: Default::default(),
            lpszMenuName: windows::core::PCWSTR::null(),
            lpszClassName: class,
            hIconSm: Default::default(),
        };
        let _ = RegisterClassExW(&wc);

        let hwnd = CreateWindowExW(
            Default::default(),
            class,
            windows::core::w!("TaskbarLyricsCoord"),
            WS_POPUP,
            0,
            0,
            0,
            0,
            HWND::default(),
            None,
            inst,
            None,
        )
        .ok()?;

        // Renderer held by the window; stored in user data (reads/writes on the pump thread).
        let renderer = Box::into_raw(Box::new(Renderer::new().ok()?));
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, renderer as isize);

        let _ = SetTimer(hwnd, T_REDRAW, 32, None);
        // Keep the window topmost: clicking the taskbar can make Explorer raise Shell_TrayWnd
        // above us (both are HWND_TOPMOST), hiding the band until we re-assert. Re-asserting
        // every 50 ms keeps the gap imperceptible; dock() skips any MoveWindow when geometry
        // is unchanged, so this is just a cheap SetWindowPos(HWND_TOPMOST).
        let _ = SetTimer(hwnd, T_VALIDATE, 50, None);

        // Register the TaskbarCreated message once (explorer restart notification).
        let m = RegisterWindowMessageW(windows::core::w!("TaskbarCreated"));
        TASKBAR_CREATED_MSG.store(m, Ordering::Relaxed);

        Some(hwnd)
    }
}

// ---------------------------------------------------------------------------
// Lyric band window
// ---------------------------------------------------------------------------

/// Given a client size and click position, return the button index (0 prev, 1 play/pause, 2 next).
/// The panel follows the lyric's adjusted position, so the hit-test uses the same offset.
fn hit_button(x: i32, y: i32, w: i32, h: i32) -> Option<i32> {
    let panel_w = BTN_SIZE * 3 + BTN_GAP * 2;
    let off_y = crate::config::position_offset_y();
    let center = render::lyric_center_x();
    // Same anchor the renderer uses for the panel; `lyric_center_x` already includes the
    // base + live drag off_x, so only keep the vertical offset here.
    let x0 = if center == i32::MIN {
        (w - panel_w) / 2
    } else {
        (center - panel_w / 2).clamp(0, w - panel_w)
    };
    let y0 = (h - BTN_SIZE) / 2 + off_y;
    for i in 0..3 {
        let bx = x0 + i * (BTN_SIZE + BTN_GAP);
        let by = y0;
        if x >= bx && x < bx + BTN_SIZE && y >= by && y < by + BTN_SIZE {
            return Some(i as i32);
        }
    }
    None
}

pub unsafe extern "system" fn lyric_proc(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    unsafe {
        match msg {
            WM_ERASEBKGND => LRESULT(1),
            WM_NCHITTEST => {
                // Make transparent (non-lyric/non-button) pixels pass clicks through to the
                // taskbar below, while lyric text and the hover control panel stay interactive.
                // lParam is the cursor's *screen* coords; convert to client before the rect test.
                let sx = x_lparam(lparam.0);
                let sy = y_lparam(lparam.0);
                let mut rc: RECT = std::mem::zeroed();
                GetWindowRect(hwnd, &mut rc).ok();
                let px = sx - rc.left;
                let py = sy - rc.top;
                if render::point_in_hot(px, py) {
                    LRESULT(HT_CLIENT)
                } else {
                    LRESULT(HT_TRANSPARENT)
                }
            }
            WM_MOUSEMOVE => {
                let mut tme = TRACKMOUSEEVENT {
                    cbSize: std::mem::size_of::<TRACKMOUSEEVENT>() as u32,
                    dwFlags: TRACKMOUSEEVENT_FLAGS(TME_LEAVE.0),
                    hwndTrack: hwnd,
                    dwHoverTime: 0,
                };
                let _ = TrackMouseEvent(&mut tme);

                render::set_hovering(true);
                let x = x_lparam(lparam.0);
                let y = y_lparam(lparam.0);

                // While drag-adjusting with the button down, translate the lyric by the mouse delta.
                if render::adjusting() && ((wparam.0 as u32) & MK_LBUTTON) != 0 {
                    let dx = x - DRAG_LAST_X.load(Ordering::Relaxed);
                    let dy = y - DRAG_LAST_Y.load(Ordering::Relaxed);
                    DRAG_LAST_X.store(x, Ordering::Relaxed);
                    DRAG_LAST_Y.store(y, Ordering::Relaxed);
                    render::add_rt_offset(dx, dy);
                    mark_dirty();
                    return LRESULT(0);
                }

                let mut rc: RECT = std::mem::zeroed();
                let (w, h) = if GetClientRect(hwnd, &mut rc).is_ok() {
                    (rc.right, rc.bottom)
                } else {
                    (0, 0)
                };
                let btn = hit_button(x, y, w, h).unwrap_or(-1);
                let down = render::DOWN_BUTTON.load(Ordering::Relaxed);
                if btn != render::HOVER_BUTTON.load(Ordering::Relaxed) || !render::IS_HOVERING.load(Ordering::Relaxed) {
                    render::set_buttons(btn, down);
                    mark_dirty();
                }
                LRESULT(0)
            }
            WM_SETCURSOR => {
                let _ = SetCursor(arrow_cursor());
                LRESULT(0)
            }
            WM_MOUSELEAVE => {
                render::set_hovering(false);
                render::set_buttons(-1, -1);
                mark_dirty();
                LRESULT(0)
            }
            WM_LBUTTONDBLCLK => {
                let x = x_lparam(lparam.0);
                let y = y_lparam(lparam.0);
                enter_adjust(hwnd, x, y);
                LRESULT(0)
            }
            WM_TIMER if (wparam.0 as usize) == T_ADJUST => {
                let _ = KillTimer(hwnd, T_ADJUST);
                let x = DRAG_LAST_X.load(Ordering::Relaxed);
                let y = DRAG_LAST_Y.load(Ordering::Relaxed);
                enter_adjust(hwnd, x, y);
                LRESULT(0)
            }
            WM_LBUTTONDOWN => {
                let x = x_lparam(lparam.0);
                let y = y_lparam(lparam.0);
                let mut rc: RECT = std::mem::zeroed();
                let (w, h) = if GetClientRect(hwnd, &mut rc).is_ok() {
                    (rc.right, rc.bottom)
                } else {
                    (0, 0)
                };
                // Already adjusting: keep the drag going.
                if render::adjusting() {
                    DRAG_LAST_X.store(x, Ordering::Relaxed);
                    DRAG_LAST_Y.store(y, Ordering::Relaxed);
                    return LRESULT(0);
                }
                let btn = hit_button(x, y, w, h).unwrap_or(-1);
                if btn >= 0 {
                    let down = render::DOWN_BUTTON.load(Ordering::Relaxed);
                    render::set_buttons(btn, btn.max(down));
                    SetCapture(hwnd);
                } else {
                    // Starting a potential long-press over the lyric text for drag-to-position.
                    DRAG_LAST_X.store(x, Ordering::Relaxed);
                    DRAG_LAST_Y.store(y, Ordering::Relaxed);
                    let _ = SetTimer(hwnd, T_ADJUST, LONG_PRESS_MS, None);
                }
                mark_dirty();
                LRESULT(0)
            }
            WM_LBUTTONUP => {
                let _ = ReleaseCapture();
                let _ = KillTimer(hwnd, T_ADJUST);
                if render::adjusting() {
                    finish_adjust();
                } else {
                    let over = render::HOVER_BUTTON.load(Ordering::Relaxed);
                    if over >= 0 {
                        fire_action(over);
                    }
                    render::set_buttons(over, -1);
                }
                mark_dirty();
                LRESULT(0)
            }
            _ => DefWindowProcW(hwnd, msg, wparam, lparam),
        }
    }
}

/// Dispatch a control to a worker thread so the message pump (and the actual action) never
/// blocks on the HTTP round-trip; the button state is reflected immediately and playback
/// syncs back over the SSE stream.
fn fire_action(btn: i32) {
    let path = match btn {
        0 => "/api/previous-track",
        1 => "/api/play-pause",
        2 => "/api/next-track",
        _ => return,
    };
    let path = path.to_owned();
    std::thread::spawn(move || {
        let _ = crate::api::control_get(&path);
    });
    mark_dirty();
}

/// Start drag-to-position mode: reset the live offset, hide the control panel, capture the
/// mouse, and remember the press anchor.
fn enter_adjust(hwnd: HWND, x: i32, y: i32) {
    // Cancel any pending long-press timer (e.g. set by the second click of a double-click) so
    // it can't fire mid-drag and reset the live offset.
    let _ = unsafe { KillTimer(hwnd, T_ADJUST) };
    DRAG_LAST_X.store(x, Ordering::Relaxed);
    DRAG_LAST_Y.store(y, Ordering::Relaxed);
    render::set_adjusting(true);
    render::reset_rt_offset();
    render::set_buttons(-1, -1);
    let _ = unsafe { SetCapture(hwnd) };
    mark_dirty();
}

/// End drag-to-position: persist the new offset (config base + live delta) to the plugin, then
/// leave adjust mode and reset the runtime offset.
fn finish_adjust() {
    let base_x = crate::config::position_offset_x();
    let base_y = crate::config::position_offset_y();
    let (rt_x, rt_y) = render::rt_offset();
    let new_x = (base_x + rt_x).clamp(OFFSET_X_MIN, OFFSET_X_MAX);
    let new_y = (base_y + rt_y).clamp(OFFSET_Y_MIN, OFFSET_Y_MAX);
    crate::config::set_position_offset(new_x, new_y);
    render::set_adjusting(false);
    render::reset_rt_offset();
    render::set_buttons(-1, -1);
    std::thread::spawn(move || {
        let _ = crate::api::set_config("position_offset_x", new_x);
        let _ = crate::api::set_config("position_offset_y", new_y);
    });
}

/// Create the lyric window, register the class, embed into the taskbar, then show.
pub fn create_and_embed_lyric() -> Result<(), ()> {
    unsafe {
        let inst = HINSTANCE(GetModuleHandleW(None).map_err(|_| ())?.0);
        let class = windows::core::w!("TaskbarLyricsBand");

        let wc = WNDCLASSEXW {
            cbSize: std::mem::size_of::<WNDCLASSEXW>() as u32,
            style: WNDCLASS_STYLES(CS_DBLCLKS.0),
            lpfnWndProc: Some(lyric_proc),
            cbClsExtra: 0,
            cbWndExtra: 0,
            hInstance: inst,
            hIcon: Default::default(),
            hCursor: arrow_cursor(),
            hbrBackground: Default::default(),
            lpszMenuName: windows::core::PCWSTR::null(),
            lpszClassName: class,
            hIconSm: Default::default(),
        };
        let _ = RegisterClassExW(&wc);

        // Layer + no-activate + tool-window (no taskbar button, never steals focus).
        let ex = WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        let hwnd = CreateWindowExW(
            ex,
            class,
            windows::core::w!("TaskbarLyrics"),
            WS_POPUP,
            0,
            0,
            600,
            48,
            HWND::default(),
            None,
            inst,
            None,
        )
        .map_err(|_| ())?;

        set_lyric_hwnd(hwnd);

        if taskbar::init_embed(hwnd) {
            let _ = ShowWindow(hwnd, SW_SHOWNA);
        } else {
            // Fallback: keep top-level near the bottom of the screen.
            let _ = ShowWindow(hwnd, SW_SHOWNA);
            let _ = SetWindowPos(
                hwnd,
                HWND_TOP,
                0,
                0,
                600,
                40,
                SWP_NOACTIVATE | SWP_NOSIZE | SWP_NOZORDER,
            );
        }
        Ok(())
    }
}