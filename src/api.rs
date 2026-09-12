//! control endpoints, and a **Server-Sent Events** client for `/api/stream`.

use serde::Deserialize;
use std::io::{Read, Write};
use std::net::TcpStream;
use std::time::Duration;

// Connect to the loopback IP directly instead of resolving the "localhost" hostname.
// The plugin's server binds `InetAddress.getLoopbackAddress()` (127.0.0.1). Control buttons
// open a *fresh* connection on every click; going straight to the IP skips any hostname/DNS
// work that could add a fixed ~1s to each (the instant lyric path rides the persistent SSE
// connection, so it never hits this).
const HOST: &str = "127.0.0.1";
const PORT: u16 = 35374;

#[derive(Clone, Debug, Deserialize)]
pub struct LyricsResponse {
    #[serde(default)]
    pub status: String,
    #[serde(default)]
    pub lyric: String,
}

fn http_get(path: &str) -> Option<String> {
    let mut stream = TcpStream::connect((HOST, PORT)).ok()?;
    let _ = stream.set_read_timeout(Some(Duration::from_secs(2)));
    let _ = stream.set_write_timeout(Some(Duration::from_secs(2)));
    let req = format!(
        "GET {path} HTTP/1.1\r\nHost: {HOST}:{PORT}\r\nConnection: close\r\nUser-Agent: taskbar-lyrics-rs\r\n\r\n"
    );
    if stream.write_all(req.as_bytes()).is_err() {
        return None;
    }
    let mut buf = Vec::new();
    if stream.read_to_end(&mut buf).is_err() {
        return None;
    }
    let text = String::from_utf8_lossy(&buf);
    match text.find("\r\n\r\n") {
        Some(idx) => {
            let body = &text[idx + 4..];
            Some(body.trim().to_string())
        }
        None => Some(text.trim().to_string()),
    }
}

pub fn fetch_lyrics() -> Option<String> {
    if let Some(raw) = http_get("/api/lyric") {
        if let Some(parsed) = serde_json::from_str::<LyricsResponse>(&raw).ok() {
            if parsed.status == "success" && !parsed.lyric.is_empty() {
                return Some(parsed.lyric);
            }
        }
    }
    if let Some(raw) = http_get("/api/lyricfile") {
        if let Ok(parsed) = serde_json::from_str::<LyricsResponse>(&raw) {
            if parsed.status == "success" {
                return Some(parsed.lyric);
            }
        }
    }
    None
}

/// Fetch the plugin's current config object (`/api/config` -> `{ "config": { ... } }`) as the
/// inner JSON string.
pub fn fetch_config_json() -> Option<String> {
    let raw = http_get("/api/config?forceRefresh=false")?;
    let v: serde_json::Value = serde_json::from_str(&raw).ok()?;
    v.get("config").map(|c| c.to_string())
}

/// A line that consists solely of hex digits is an HTTP/1.1 chunk-size frame, which Jetty
/// interleaves between SSE `data:` payloads. We skip those and keep the `data:` lines.
fn is_chunk_size(line: &str) -> bool {
    !line.is_empty() && line.bytes().all(|b| b.is_ascii_hexdigit())
}

/// Blocking loop over the SSE stream. Tws one connection; calls `on_event` with each
/// `data:` payload as it arrives, and returns on disconnect so the caller can reconnect.
pub fn run_stream_callback(mut on_event: impl FnMut(&str)) -> std::io::Result<()> {
    let mut stream = TcpStream::connect((HOST, PORT))?;
    // Long read timeout: the connection stays up, only 0 == disconnect matters.
    let _ = stream.set_read_timeout(Some(Duration::from_secs(600)));
    let _ = stream.set_write_timeout(Some(Duration::from_secs(10)));
    let req = format!(
        "GET /api/stream HTTP/1.1\r\nHost: {HOST}:{PORT}\r\nAccept: text/event-stream\r\nConnection: keep-alive\r\nUser-Agent: taskbar-lyrics-rs\r\n\r\n"
    );
    stream.write_all(req.as_bytes())?;

    let mut buf = [0u8; 8192];
    let mut acc: Vec<u8> = Vec::new();
    let mut in_headers = true;
    let mut payload = String::new();

    loop {
        let n = stream.read(&mut buf)?;
        if n == 0 {
            break; // peer closed -> reconnect from caller
        }
        acc.extend_from_slice(&buf[..n]);

        loop {
            let Some(nl) = acc.iter().position(|&b| b == b'\n') else {
                break;
            };
            let mut line: Vec<u8> = acc.drain(..=nl).collect();
            line.pop();
            if line.last() == Some(&b'\r') {
                line.pop();
            }
            let text = String::from_utf8_lossy(&line);

            if in_headers {
                if text.is_empty() {
                    in_headers = false;
                }
                continue;
            }

            let trimmed = text.trim();
            if is_chunk_size(trimmed) {
                if trimmed == "0" {
                    let ev = std::mem::take(&mut payload);
                    if !ev.is_empty() {
                        on_event(&ev);
                    }
                    return Ok(());
                }
                continue;
            }
            if let Some(d) = text.strip_prefix("data:") {
                payload.push_str(d.trim_start());
            } else if trimmed.is_empty() {
                let ev = std::mem::take(&mut payload);
                if !ev.is_empty() {
                    on_event(&ev);
                }
            }
        }
    }

    let ev = std::mem::take(&mut payload);
    if !ev.is_empty() {
        on_event(&ev);
    }
    Ok(())
}

pub fn set_config(key: &str, value: i32) -> bool {
    let path = format!("/api/config/set?key={key}&value={value}");
    control_get(&path)
}

pub fn control_get(path: &str) -> bool {
    let t0 = std::time::Instant::now();
    let ok = http_get(path).map(|_| true).unwrap_or(false);
    crate::dlog("control", format_args!("{path} -> ok={ok} in {}ms", t0.elapsed().as_millis()));
    ok
}