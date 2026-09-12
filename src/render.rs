//! Rendering: DirectWrite + Direct2D drawn into a 32-bit premultiplied DIB and composited onto the
//! layered window with per-pixel alpha through `UpdateLayeredWindow`.

use std::sync::atomic::{AtomicBool, AtomicI32, Ordering};
use std::sync::Mutex;

use windows::core::PCWSTR;
use windows::Win32::Foundation::{POINT, RECT, SIZE, HWND};
use windows::Win32::Graphics::Direct2D::Common::{
    D2D1_ALPHA_MODE_UNKNOWN, D2D1_COLOR_F, D2D1_PIXEL_FORMAT, D2D_RECT_F,
};
use windows::Win32::Graphics::Direct2D::{
    D2D1_ANTIALIAS_MODE_PER_PRIMITIVE, D2D1_DRAW_TEXT_OPTIONS_NONE, D2D1_FACTORY_TYPE_SINGLE_THREADED,
    D2D1_FEATURE_LEVEL_DEFAULT, D2D1_RENDER_TARGET_PROPERTIES, D2D1_RENDER_TARGET_TYPE_DEFAULT,
    D2D1_RENDER_TARGET_USAGE_NONE, D2D1_ROUNDED_RECT, D2D1CreateFactory, ID2D1Factory,
    ID2D1RenderTarget, ID2D1SolidColorBrush,
};
use windows::Win32::Graphics::DirectWrite::{
    DWRITE_FACTORY_TYPE_SHARED, DWRITE_FONT_STRETCH_NORMAL, DWRITE_FONT_STYLE_NORMAL,
    DWRITE_FONT_WEIGHT_NORMAL, DWRITE_MEASURING_MODE_NATURAL,
    DWRITE_PARAGRAPH_ALIGNMENT_CENTER, DWRITE_TEXT_ALIGNMENT_LEADING, DWRITE_TEXT_METRICS,
    DWRITE_WORD_WRAPPING_NO_WRAP, DWriteCreateFactory, IDWriteFactory, IDWriteTextFormat,
    IDWriteTextLayout,
};
use windows::Win32::Graphics::Dxgi::Common::DXGI_FORMAT_UNKNOWN;
use windows::Win32::Graphics::Gdi::{
    CreateCompatibleDC, CreateDIBSection, DeleteObject, SelectObject, AC_SRC_ALPHA, AC_SRC_OVER,
    BLENDFUNCTION, BITMAPINFO, BITMAPINFOHEADER, BI_RGB, DIB_RGB_COLORS, HDC, HBITMAP, HGDIOBJ,
    RGBQUAD,
};
use windows::Win32::Graphics::Imaging::{
    CLSID_WICImagingFactory1, GUID_WICPixelFormat32bppPBGRA, IWICBitmap, IWICImagingFactory,
    WICBitmapCacheOnDemand, WICRect,
};
use windows::Win32::System::Com::{CoCreateInstance, CLSCTX_INPROC_SERVER};
use windows::Win32::UI::WindowsAndMessaging::{
    GetWindowRect, UpdateLayeredWindow, ULW_ALPHA,
};

use crate::config::{self};
use crate::lyrics::{self, LyricsLine};

// ---- UI interaction state (set by the window proc, read by the renderer) ----
pub static IS_HOVERING: AtomicBool = AtomicBool::new(false);
pub static HOVER_BUTTON: AtomicI32 = AtomicI32::new(0);
pub static DOWN_BUTTON: AtomicI32 = AtomicI32::new(0);

// ---- drag-to-position state (set by the window proc, read by the renderer) ----
/// True while the user is dragging the lyric to a new position (double-click or long-press).
pub static ADJUSTING: AtomicBool = AtomicBool::new(false);
/// Live pixel delta accumulated during a drag, added on top of the persisted config offset.
pub static RT_OFFSET_X: AtomicI32 = AtomicI32::new(0);
pub static RT_OFFSET_Y: AtomicI32 = AtomicI32::new(0);
/// Screen-x center (client coords) of the main lyric line drawn last frame, so the hover
/// control panel can anchor to the lyric's actual position for any alignment.
static LYRIC_CENTER_X: Mutex<i32> = Mutex::new(i32::MIN);
/// Lyric block hotspot rects from the last non-hover frame; kept active while hovering so a
/// cursor over the lyric doesn't oscillate hover on/off (which caused visible flicker).
static LYRIC_HOT_RECTS: Mutex<Vec<(f32, f32, f32, f32)>> = Mutex::new(Vec::new());

pub fn set_lyric_center(x: i32) {
    if let Ok(mut c) = LYRIC_CENTER_X.lock() {
        *c = x;
    }
}
pub fn lyric_center_x() -> i32 {
    LYRIC_CENTER_X.lock().map(|c| *c).unwrap_or(i32::MIN)
}
pub fn set_lyric_hot(rects: Vec<(f32, f32, f32, f32)>) {
    if let Ok(mut v) = LYRIC_HOT_RECTS.lock() {
        *v = rects;
    }
}
pub fn lyric_hot_rects() -> Vec<(f32, f32, f32, f32)> {
    LYRIC_HOT_RECTS.lock().map(|v| v.clone()).unwrap_or_default()
}

pub fn set_hovering(b: bool) {
    IS_HOVERING.store(b, Ordering::Relaxed);
}
pub fn set_buttons(hover: i32, down: i32) {
    HOVER_BUTTON.store(hover, Ordering::Relaxed);
    DOWN_BUTTON.store(down, Ordering::Relaxed);
}
pub fn set_adjusting(b: bool) {
    ADJUSTING.store(b, Ordering::Relaxed);
}
pub fn adjusting() -> bool {
    ADJUSTING.load(Ordering::Relaxed)
}
pub fn add_rt_offset(dx: i32, dy: i32) {
    RT_OFFSET_X.fetch_add(dx, Ordering::Relaxed);
    RT_OFFSET_Y.fetch_add(dy, Ordering::Relaxed);
}
pub fn rt_offset() -> (i32, i32) {
    (RT_OFFSET_X.load(Ordering::Relaxed), RT_OFFSET_Y.load(Ordering::Relaxed))
}
pub fn reset_rt_offset() {
    RT_OFFSET_X.store(0, Ordering::Relaxed);
    RT_OFFSET_Y.store(0, Ordering::Relaxed);
}

// ---- click-through hit regions (filled each frame by the renderer, read by WM_NCHITTEST) ----
// Client-coord rects where the window should accept mouse input instead of letting it fall
// through to the taskbar: the lyric text block and, while hovering, the control panel. While
// drag-adjusting (`ADJUSTING`) the whole band is grabbable, so `point_in_hot` also honours it.
pub static HOT_RECTS: Mutex<Vec<(f32, f32, f32, f32)>> = Mutex::new(Vec::new());

/// Reset the hot regions; call before drawing a frame.
fn clear_hot() {
    if let Ok(mut v) = HOT_RECTS.lock() {
        v.clear();
    }
}

/// Add an interactive client-space rect (left, top, right, bottom).
fn add_hot(l: f32, t: f32, r: f32, b: f32) {
    if let Ok(mut v) = HOT_RECTS.lock() {
        v.push((l, t, r, b));
    }
}

/// Point-in-client-rect test used by the window proc for `WM_NCHITTEST`.
pub fn point_in_hot(x: i32, y: i32) -> bool {
    if ADJUSTING.load(Ordering::Relaxed) {
        return true;
    }
    // Give the text a little grab slack.
    const SLACK: f32 = 6.0;
    let (x, y) = (x as f32, y as f32);
    let Ok(v) = HOT_RECTS.lock() else { return true };
    v.iter().any(|&(l, t, r, b)| x >= l - SLACK && x <= r + SLACK && y >= t - SLACK && y <= b + SLACK)
}

// Karaoke highlight follows each SPL word's timing directly via `lyrics::word_progress`.

const BTN_SIZE: f32 = 30.0;
const BTN_GAP: f32 = 6.0;

type Color = (f32, f32, f32, f32);

pub struct Renderer {
    dw: IDWriteFactory,
    d2d: ID2D1Factory,
    wic: IWICImagingFactory,
    // Cached WIC bitmap + its D2D render target (sized to the current surface).
    wbmp: Option<(IWICBitmap, ID2D1RenderTarget, u32, u32)>,
    rt: Option<Surface>,
}

struct Surface {
    dc: HDC,
    dib: HBITMAP,
    old_dib: HGDIOBJ,
    bits: *mut u8,
    w: u32,
    h: u32,
}

fn to_color(c: Color) -> D2D1_COLOR_F {
    D2D1_COLOR_F { r: c.0, g: c.1, b: c.2, a: c.3 }
}

impl Renderer {
    pub fn new() -> windows::core::Result<Self> {
        let dw: IDWriteFactory = unsafe { DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED) }?;
        let d2d: ID2D1Factory =
            unsafe { D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, None) }?;
        let wic: IWICImagingFactory = unsafe {
            CoCreateInstance(&CLSID_WICImagingFactory1, None, CLSCTX_INPROC_SERVER)?
        };
        Ok(Self { dw, d2d, wic, wbmp: None, rt: None })
    }

    /// WIC/D2D bitmap render target properties: bind to the bitmap's own format.
    fn rt_props() -> D2D1_RENDER_TARGET_PROPERTIES {
        D2D1_RENDER_TARGET_PROPERTIES {
            r#type: D2D1_RENDER_TARGET_TYPE_DEFAULT,
            pixelFormat: D2D1_PIXEL_FORMAT {
                format: DXGI_FORMAT_UNKNOWN,
                alphaMode: D2D1_ALPHA_MODE_UNKNOWN,
            },
            dpiX: 96.0,
            dpiY: 96.0,
            usage: D2D1_RENDER_TARGET_USAGE_NONE,
            minLevel: D2D1_FEATURE_LEVEL_DEFAULT,
        }
    }

    fn ensure_surface(&mut self, w: u32, h: u32) -> Option<HDC> {
        if let Some(s) = &self.rt {
            if s.w == w && s.h == h {
                return Some(s.dc);
            }
        }
        if let Some(old) = self.rt.take() {
            unsafe {
                let _ = SelectObject(old.dc, old.old_dib);
                let _ = DeleteObject(old.dib);
            }
        }
        if w == 0 || h == 0 {
            return None;
        }
        let dc = unsafe { CreateCompatibleDC(None) };
        if dc.is_invalid() {
            return None;
        }
        let bmi = BITMAPINFO {
            bmiHeader: BITMAPINFOHEADER {
                biSize: std::mem::size_of::<BITMAPINFOHEADER>() as u32,
                biWidth: w as i32,
                biHeight: -(h as i32),
                biPlanes: 1,
                biBitCount: 32,
                biCompression: BI_RGB.0,
                ..Default::default()
            },
            bmiColors: [RGBQUAD::default(); 1],
        };
        let mut bits: *mut core::ffi::c_void = std::ptr::null_mut();
        let dib = unsafe { CreateDIBSection(dc, &bmi, DIB_RGB_COLORS, &mut bits, None, 0).ok()? };
        let old_dib = unsafe { SelectObject(dc, dib) };
        self.rt = Some(Surface { dc, dib, old_dib, bits: bits as *mut u8, w, h });
        Some(dc)
    }

    pub fn draw_frame(&mut self, hwnd: HWND) {
        let Some((w, h)) = client_size(hwnd) else { return };
        let (w, h) = (w.max(1) as u32, h.max(1) as u32);
        let Some(hdc) = self.ensure_surface(w, h) else { return };

        // (Re)create the WIC bitmap + full-featured D2D render target at this size.
        // Unlike ID2D1DCRenderTarget, a WIC bitmap render target supports text APIs.
        let want = (w, h);
        if self.wbmp.as_ref().map_or(true, |(_, _, bw, bh)| (*bw, *bh) != want) {
            self.wbmp.take();
            let bmp = unsafe {
                self.wic
                    .CreateBitmap(w, h, &GUID_WICPixelFormat32bppPBGRA, WICBitmapCacheOnDemand)
            };
            let Ok(bmp) = bmp else { return };
            let rt = unsafe { self.d2d.CreateWicBitmapRenderTarget(&bmp, &Self::rt_props()) };
            let Ok(rt) = rt else { return };
            self.wbmp = Some((bmp, rt, w, h));
        }
        let (bmp, render) = {
            // Cloned (AddRef'd) so the borrow of `self.wbmp` ends before we re-borrow
            // `self` mutably for draw_into.
            let (bmp, render, _, _) = self.wbmp.as_ref().unwrap();
            (bmp.clone(), render.clone())
        };

        let scene = self.build_scene();
        let cfg = config::lock().read().map(|g| g.clone()).unwrap_or_default();

        unsafe {
            render.BeginDraw();
            self.draw_into(&render, &scene, &cfg, w as f32, h as f32);
            let _ = render.EndDraw(None, None);
        }

        // Copy the rendered premultiplied BGRA pixels into the DIB backing bits.
        unsafe {
            let stride = w * 4;
            let wic = WICRect { X: 0, Y: 0, Width: w as i32, Height: h as i32 };
            let bits = self.rt.as_ref().unwrap().bits;
            let buf = std::slice::from_raw_parts_mut(bits, (stride * h) as usize);
            let _ = bmp.CopyPixels(&wic, stride, buf);
        }

        unsafe {
            let blend = BLENDFUNCTION {
                BlendOp: AC_SRC_OVER as u8,
                BlendFlags: 0,
                SourceConstantAlpha: 255,
                AlphaFormat: AC_SRC_ALPHA as u8,
            };
            // Update the layered surface at the window's *current* screen position/size.
            // Passing NULL for pptDst/psize would not redraw; passing the real rect both
            // positions the surface where MoveWindow put it and guarantees a redraw.
            let mut rc: RECT = std::mem::zeroed();
            let _ = GetWindowRect(hwnd, &mut rc);
            let x = rc.left;
            let y = rc.top;
            let r = UpdateLayeredWindow(
                hwnd,
                None,
                Some(&POINT { x, y }),
                Some(&SIZE { cx: w as i32, cy: h as i32 }),
                hdc,
                Some(&POINT { x: 0, y: 0 }),
                windows::Win32::Foundation::COLORREF(0),
                Some(&blend as *const BLENDFUNCTION),
                ULW_ALPHA,
            );
            // Only pay for the (allocating) error formatting when debug logging is on.
            if crate::debug_enabled() {
                let ulw_err = format!("{:?}", r.err()).replace("\r", "").replace("\n", " ");
                crate::dlog(
                    "draw",
                    format_args!("{w}x{h} line={} ulw={}", scene.line.is_some(), ulw_err),
                );
            }
        }
    }

    fn draw_into(
        &mut self,
        rt: &ID2D1RenderTarget,
        scene: &Scene,
        cfg: &crate::config::LyricsConfig,
        w: f32,
        h: f32,
    ) {
        unsafe {
            let clear = D2D1_COLOR_F { r: 0.0, g: 0.0, b: 0.0, a: 0.0 };
            rt.Clear(Some(&clear as *const D2D1_COLOR_F));
        }
        if let Some((r, g, b, a)) = crate::config::parse_color(&cfg.background_color) {
            if a > 0 {
                let bg = self.solid(rt, (r as f32 / 255.0, g as f32 / 255.0, b as f32 / 255.0, a as f32 / 255.0));
                unsafe {
                    let rect = D2D_RECT_F { left: 0.0, top: 0.0, right: w, bottom: h };
                    rt.FillRectangle(&rect, &bg);
                }
            }
        }
        // Refresh the click-through hot regions for this frame. While adjusting, the whole
        // band is grabbable (handled by `point_in_hot` via ADJUSTING); otherwise only the
        // lyric block / control panel are interactive.
        clear_hot();
        if scene.hovering {
            self.draw_control_panel(rt, scene, w, h);
            // Keep the lyric region hot while hovering so a cursor over the lyric (wide for
            // long lines) doesn't toggle hover on/off and cause visible flicker.
            for (l, t, r, b) in lyric_hot_rects() {
                add_hot(l, t, r, b);
            }
        } else if let Some(line) = &scene.line {
            self.draw_lyrics(rt, scene, cfg, line, w, h);
        }
        if ADJUSTING.load(Ordering::Relaxed) {
            self.draw_adjust_hint(rt, w);
        }
    }

    /// Small translucent banner shown while drag-adjusting so the user knows repositioning is live.
    fn draw_adjust_hint(&mut self, rt: &ID2D1RenderTarget, w: f32) {
        let text = "\u{21D4} 拖动调整位置，松开保存";
        let fmt = self.make_format("Microsoft YaHei", 12.0);
        let (tw, th) = self.measure("Microsoft YaHei", 12.0, text);
        let pad = 8.0f32;
        let bw = tw + pad * 2.0;
        let bh = th + pad * 2.0;
        let bx = (w - bw) / 2.0;
        let by = 4.0;
        let bg = self.solid(rt, (0.05, 0.05, 0.08, 0.85));
        let rounded = D2D1_ROUNDED_RECT {
            rect: D2D_RECT_F { left: bx, top: by, right: bx + bw, bottom: by + bh },
            radiusX: 4.0,
            radiusY: 4.0,
        };
        unsafe { rt.FillRoundedRectangle(&rounded, &bg); }
        let fg = self.solid(rt, (1.0, 1.0, 1.0, 0.95));
        let trect = D2D_RECT_F {
            left: bx + pad,
            top: by + pad,
            right: bx + bw - pad,
            bottom: by + bh - pad,
        };
        self.draw_plain(rt, &fmt, text, &trect, &fg);
    }

    fn solid(&mut self, rt: &ID2D1RenderTarget, c: Color) -> ID2D1SolidColorBrush {
        let color = to_color(c);
        unsafe { rt.CreateSolidColorBrush(&color, None).unwrap() }
    }

    fn draw_lyrics(
        &mut self,
        rt: &ID2D1RenderTarget,
        scene: &Scene,
        cfg: &crate::config::LyricsConfig,
        line: &LyricsLine,
        w: f32,
        h: f32,
    ) {
        let base_color = color01(&cfg.font_color, (1.0, 1.0, 1.0, 1.0));
        let hl_color = color01(&cfg.highlight_color, (0.0, 1.0, 1.0, 1.0));
        let tr_color = color01(&cfg.translation_font_color, (0.8, 0.8, 0.8, 1.0));
        let family = family_name(&cfg.font_family);
        let main_size = cfg.font_size.max(6) as f32;
        let has_tr = line.has_translation() && cfg.show_translation && !line.translation_text.is_empty();
        let tr_size = if cfg.translation_font_size > 0 {
            cfg.translation_font_size.max(6) as f32
        } else {
            (cfg.font_size - 2).max(8) as f32
        };

        let align = cfg.alignment.as_str();
        let max_right = w; // full bar width, so alignment matches the default full-width window

        // ---- main text metrics ----
        let ktv = line.is_word_timing;
        let mut cells: Vec<(usize, f32, f32)> = Vec::new(); // (idx into word_timings, x, w)
        let mut total_w = 0.0f32;
        let mut main_h = 0.0f32;
        if ktv {
            for (idx, wt) in line.word_timings.iter().enumerate() {
                if wt.text.is_empty() {
                    continue;
                }
                if wt.text.trim().is_empty() {
                    // DirectWrite can report a zero advance for a lone space (font-specific glyph
                    // fallback), so measure a placeholder glyph and fall back to a fixed half-em gap
                    // when that also yields nothing — never let the inter-word gap collapse to zero.
                    let mut sw = self.measure(&family, main_size, "\u{00a0}").0;
                    if sw <= 0.0 {
                        sw = main_size * 0.30;
                    }
                    if sw > 0.0 {
                        cells.push((idx, total_w, sw));
                        total_w += sw;
                    }
                    continue;
                }
                let (ww, wh) = self.measure(&family, main_size, &wt.text);
                if ww > 0.0 {
                    cells.push((idx, total_w, ww));
                    total_w += ww;
                    main_h = main_h.max(wh);
                }
            }
        } else {
            let (tw, th) = self.measure(&family, main_size, &line.original_text);
            total_w = tw;
            main_h = if th > 0.0 { th } else { main_size * 1.4 };
        }
        if main_h <= 0.0 {
            main_h = main_size * 1.4;
        }

        let (tr_w, tr_h) = if has_tr {
            self.measure(&family, tr_size, &line.translation_text)
        } else {
            (0.0, 0.0)
        };

        // `line_spacing` is the pixel gap inserted between the main line and the translation.
        let gap = cfg.line_spacing.max(0) as f32;
        let content_h = main_h + (if has_tr { gap + tr_h } else { 0.0 });
        // Position offset: persisted config value + any live drag delta, relative to the
        // chosen alignment origin (left edge / center / right edge).
        let off_x = cfg.position_offset_x as f32 + RT_OFFSET_X.load(Ordering::Relaxed) as f32;
        let off_y = cfg.position_offset_y as f32 + RT_OFFSET_Y.load(Ordering::Relaxed) as f32;
        let top_start = ((h - content_h) / 2.0).max(0.0) + off_y;
        let main_x = x_origin(align, max_right, total_w) + off_x;
        let tr_x = x_origin(align, max_right, tr_w) + off_x;

        // Remember where the main line actually landed so the hover panel can sit on it.
        set_lyric_center((main_x + total_w / 2.0).round() as i32);

        let mut lyric_rects = Vec::new();
        lyric_rects.push((main_x, top_start, main_x + total_w, top_start + main_h));
        if has_tr && tr_w > 0.0 {
            lyric_rects.push((tr_x, top_start + main_h + gap, tr_x + tr_w, top_start + main_h + gap + tr_h));
        }
        set_lyric_hot(lyric_rects);

        let base_br = self.solid(rt, base_color);

        add_hot(main_x, top_start, main_x + total_w, top_start + main_h);
        if has_tr && tr_w > 0.0 {
            add_hot(tr_x, top_start + main_h + gap, tr_x + tr_w, top_start + main_h + gap + tr_h);
        }

        if ktv {
            let hl_br = self.solid(rt, hl_color);
            for (idx, x, ww) in &cells {
                let rect = D2D_RECT_F {
                    left: main_x + x,
                    top: top_start,
                    right: main_x + x + ww + 8.0,
                    bottom: top_start + main_h,
                };
                self.draw_word(rt, &family, main_size, &line.word_timings[*idx].text, &rect, &base_br);
            }

            // Karaoke: highlight fills word-by-word using the parsed SPL per-word timings.
            let mut edge = 0.0f32;
            for (idx, _, ww) in &cells {
                let wt = &line.word_timings[*idx];
                edge += ww * lyrics::word_progress(wt.start_time, wt.end_time, scene.pos) as f32;
            }
            if edge > 0.5 && total_w > 0.0 {
                let clip_right = (main_x + edge.min(total_w)).max(main_x + 0.5);
                let clip = D2D_RECT_F {
                    left: main_x,
                    top: top_start,
                    right: clip_right,
                    bottom: top_start + main_h,
                };
                unsafe {
                    rt.PushAxisAlignedClip(&clip, D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
                }
                for (idx, x, ww) in &cells {
                    let rect = D2D_RECT_F {
                        left: main_x + x,
                        top: top_start,
                        right: main_x + x + ww + 8.0,
                        bottom: top_start + main_h,
                    };
                    self.draw_word(rt, &family, main_size, &line.word_timings[*idx].text, &rect, &hl_br);
                }
                unsafe {
                    rt.PopAxisAlignedClip();
                }
            }
        } else if total_w > 0.0 {
            let fmt = self.make_format(&family, main_size);
            let rect = D2D_RECT_F {
                left: main_x,
                top: top_start,
                right: main_x + total_w + 8.0,
                bottom: top_start + main_h,
            };
            self.draw_plain(rt, &fmt, &line.original_text, &rect, &base_br);
        }

        if has_tr && tr_w > 0.0 {
            let fmt = self.make_format(&family, tr_size);
            let tr_br = self.solid(rt, tr_color);
            let rect = D2D_RECT_F {
                left: tr_x,
                top: top_start + main_h + gap,
                right: tr_x + tr_w + 8.0,
                bottom: top_start + main_h + gap + tr_h,
            };
            self.draw_plain(rt, &fmt, &line.translation_text, &rect, &tr_br);
        }
    }

    fn draw_word(
        &mut self,
        rt: &ID2D1RenderTarget,
        family: &str,
        size: f32,
        text: &str,
        rect: &D2D_RECT_F,
        brush: &ID2D1SolidColorBrush,
    ) {
        let fmt = self.make_format(family, size);
        let txt: Vec<u16> = text.encode_utf16().collect();
        unsafe {
            let _ = rt.DrawText(
                &txt,
                &fmt,
                rect,
                brush,
                D2D1_DRAW_TEXT_OPTIONS_NONE,
                DWRITE_MEASURING_MODE_NATURAL,
            );
        }
    }

    fn draw_plain(
        &mut self,
        rt: &ID2D1RenderTarget,
        fmt: &IDWriteTextFormat,
        text: &str,
        rect: &D2D_RECT_F,
        brush: &ID2D1SolidColorBrush,
    ) {
        let txt: Vec<u16> = text.encode_utf16().collect();
        unsafe {
            let _ = rt.DrawText(
                &txt,
                fmt,
                rect,
                brush,
                D2D1_DRAW_TEXT_OPTIONS_NONE,
                DWRITE_MEASURING_MODE_NATURAL,
            );
        }
    }

    fn draw_control_panel(&mut self, rt: &ID2D1RenderTarget, scene: &Scene, w: f32, h: f32) {
        let panel_w = BTN_SIZE * 3.0 + BTN_GAP * 2.0;
        let off_x = crate::config::position_offset_x() as f32
            + RT_OFFSET_X.load(Ordering::Relaxed) as f32;
        let off_y = crate::config::position_offset_y() as f32
            + RT_OFFSET_Y.load(Ordering::Relaxed) as f32;

        // Anchor the panel on the main lyric line's actual position (recorded last non-hover
        // frame), so it follows left/center/right alignment and the drag offset together.
        // `lyric_center_x()` already includes both the base offset and any live drag offset.
        let cx = lyric_center_x();
        let x0 = if cx == i32::MIN {
            x_origin(crate::config::alignment().as_str(), w, panel_w) + off_x
        } else {
            (cx as f32 - panel_w / 2.0).clamp(0.0, (w - panel_w).max(0.0))
        };
        let y0 = (h - BTN_SIZE) / 2.0 + off_y;
        add_hot(x0, y0, x0 + panel_w, y0 + BTN_SIZE);
        let labels = ["\u{23EE}", if scene.playing { "\u{23F8}" } else { "\u{25B6}" }, "\u{23ED}"];
        let glyph_family = "Segoe UI Symbol";

        for i in 0..3_i32 {
            let x = x0 + i as f32 * (BTN_SIZE + BTN_GAP);
            let rect = D2D_RECT_F { left: x, top: y0, right: x + BTN_SIZE, bottom: y0 + BTN_SIZE };
            let (bg, border) = button_colors(scene.hover_btn == i, scene.down_btn == i);
            let bg_br = self.solid(rt, bg);
            let bo_br = self.solid(rt, border);
            let rounded = D2D1_ROUNDED_RECT {
                rect,
                radiusX: 3.0,
                radiusY: 3.0,
            };
            unsafe {
                rt.FillRoundedRectangle(&rounded, &bg_br);
                rt.DrawRoundedRectangle(&rounded, &bo_br, 1.0, None);
            }
            let fmt = self.make_format(glyph_family, 19.0);
            let tb = self.solid(rt, (1.0, 1.0, 1.0, 1.0));
            let (gw, gh) = self.measure(glyph_family, 19.0, labels[i as usize]);
            let crect = D2D_RECT_F {
                left: (rect.left + rect.right) / 2.0 - gw / 2.0,
                top: (rect.top + rect.bottom) / 2.0 - gh / 2.0 - 1.0,
                right: (rect.left + rect.right) / 2.0 + gw / 2.0,
                bottom: (rect.top + rect.bottom) / 2.0 + gh / 2.0 - 1.0,
            };
            self.draw_plain(rt, &fmt, labels[i as usize], &crect, &tb);
        }
    }

    fn make_format(&self, family: &str, size: f32) -> IDWriteTextFormat {
        let family_w = tow16(family);
        let locale_w = tow16("");
        let created = unsafe {
            self.dw.CreateTextFormat(
                PCWSTR(family_w.as_ptr()),
                None,
                DWRITE_FONT_WEIGHT_NORMAL,
                DWRITE_FONT_STYLE_NORMAL,
                DWRITE_FONT_STRETCH_NORMAL,
                size,
                PCWSTR(locale_w.as_ptr()),
            )
        };
        let tf = match created {
            Ok(tf) => tf,
            Err(_) => {
                let fall_w = tow16("Microsoft YaHei");
                unsafe {
                    self.dw
                        .CreateTextFormat(
                            PCWSTR(fall_w.as_ptr()),
                            None,
                            DWRITE_FONT_WEIGHT_NORMAL,
                            DWRITE_FONT_STYLE_NORMAL,
                            DWRITE_FONT_STRETCH_NORMAL,
                            size,
                            PCWSTR(locale_w.as_ptr()),
                        )
                        .unwrap()
                }
            }
        };
        unsafe {
            let _ = tf.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
            let _ = tf.SetTextAlignment(DWRITE_TEXT_ALIGNMENT_LEADING);
            let _ = tf.SetWordWrapping(DWRITE_WORD_WRAPPING_NO_WRAP);
        }
        tf
    }

    fn measure(&self, family: &str, size: f32, text: &str) -> (f32, f32) {
        let fmt = self.make_format(family, size);
        let txt: Vec<u16> = text.encode_utf16().collect();
        let layout: IDWriteTextLayout =
            unsafe { self.dw.CreateTextLayout(&txt, &fmt, 20000.0, 20000.0) }.unwrap();
        let mut m: DWRITE_TEXT_METRICS = unsafe { std::mem::zeroed() };
        unsafe { let _ = layout.GetMetrics(&mut m); }
        (m.width, m.height)
    }

    fn build_scene(&self) -> Scene {
        let pos = crate::effective_position();
        // Hide the hover control panel while drag-adjusting so the lyric stays visible and
        // clickable for repositioning.
        let hovering = IS_HOVERING.load(Ordering::Relaxed) && !ADJUSTING.load(Ordering::Relaxed);
        let hover_btn = HOVER_BUTTON.load(Ordering::Relaxed);
        let down_btn = DOWN_BUTTON.load(Ordering::Relaxed);

        // Lock only long enough to resolve the single active line (a single small clone) —
        // never clone the whole lyric vector on every 32ms frame, which stalls the message pump.
        let (line, playing) = {
            let s = crate::SHARED.lock().unwrap();
            let line = if hovering {
                None
            } else {
                lyrics::get_current_line(&s.lines, pos)
            };
            (line, s.is_playing)
        };

        Scene { line, hovering, hover_btn, down_btn, playing, pos }
    }
}

struct Scene {
    line: Option<LyricsLine>,
    hovering: bool,
    hover_btn: i32,
    down_btn: i32,
    playing: bool,
    pos: i64,
}

fn tow16(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0)).collect()
}

fn client_size(hwnd: HWND) -> Option<(i32, i32)> {
    unsafe {
        let mut r: RECT = Default::default();
        if GetWindowRect(hwnd, &mut r).is_ok() {
            return Some((r.right - r.left, r.bottom - r.top));
        }
    }
    None
}

fn color01(s: &str, fallback: Color) -> Color {
    crate::config::parse_color(s)
        .map(|(r, g, b, a)| (r as f32 / 255.0, g as f32 / 255.0, b as f32 / 255.0, a as f32 / 255.0))
        .unwrap_or(fallback)
}

fn family_name(cfg: &str) -> String {
    let raw = cfg.trim();
    if raw.is_empty() || raw.eq_ignore_ascii_case("default") {
        return "Microsoft YaHei".to_string();
    }
    if raw.eq_ignore_ascii_case("MicrosoftYaHei") {
        "Microsoft YaHei".to_string()
    } else {
        raw.to_string()
    }
}

fn x_origin(align: &str, max_right: f32, content_w: f32) -> f32 {
    match align {
        "left" => 0.0,
        "right" => (max_right - content_w).max(0.0),
        _ => ((max_right - content_w) / 2.0).max(0.0),
    }
}

fn button_colors(hover: bool, down: bool) -> (Color, Color) {
    let bg_a = if down { 0.376 } else if hover { 0.251 } else { 0.125 };
    let bo_a = if down { 0.627 } else if hover { 0.502 } else { 0.376 };
    // Colors are "black with alpha" and "white with alpha".
    ((0.0, 0.0, 0.0, bg_a), (1.0, 1.0, 1.0, bo_a))
}