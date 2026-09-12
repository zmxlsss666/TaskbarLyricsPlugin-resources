//! Lyric parsing (SPL).

#[derive(Clone, Debug)]
pub struct WordTiming {
    pub text: String,
    pub start_time: i64,
    pub end_time: i64,
}

#[derive(Clone, Debug)]
pub struct LyricsLine {
    pub original_text: String,
    pub translation_text: String,
    pub is_word_timing: bool,
    pub word_timings: Vec<WordTiming>,
    pub start_time: i64,
    pub end_time: i64,
}

impl LyricsLine {
    pub fn has_translation(&self) -> bool {
        !self.translation_text.is_empty()
    }
}

/// One raw token of a lyric line: either a timestamp or a text run.
enum RawPart {
    Ts(i64),
    Text(String),
}

/// Parse `[mm:ss(.xx)]` / `<mm:ss(.xx)>` beginning at `chars[i]` (caller checked the opener).
/// Returns (elapsed_ms, char index just after the closer).
fn parse_mmss_at(chars: &[char], i: usize, close: char) -> Option<(i64, usize)> {
    let mut j = i + 1;
    if j >= chars.len() || !chars[j].is_ascii_digit() {
        return None;
    }
    let min_start = j;
    while j < chars.len() && chars[j].is_ascii_digit() {
        j += 1;
    }
    if j >= chars.len() || chars[j] != ':' {
        return None;
    }
    let minutes: String = chars[min_start..j].iter().collect();
    j += 1;
    let sec_start = j;
    while j < chars.len() && chars[j].is_ascii_digit() {
        j += 1;
    }
    if j >= chars.len() {
        return None;
    }
    let seconds: String = chars[sec_start..j].iter().collect();
    let (frac, at) = if chars[j] == '.' {
        let f0 = j + 1;
        let mut k = f0;
        while k < chars.len() && chars[k].is_ascii_digit() {
            k += 1;
        }
        (chars[f0..k].iter().collect::<String>(), k)
    } else {
        (String::new(), j)
    };
    if at >= chars.len() || chars[at] != close {
        return None;
    }
    let m = minutes.parse::<i64>().ok()?;
    let s = seconds.parse::<i64>().ok()?;
    let ms = sec_frac_to_ms(frac.parse::<i64>().unwrap_or(0), frac.len());
    Some(((m * 60 + s) * 1000 + ms, at + 1))
}

fn sec_frac_to_ms(frac: i64, len: usize) -> i64 {
    // `22` -> 0.22s = 220ms; `500` -> 500ms. Treat the fraction as a fraction of a second
    // (centiseconds), otherwise `[00:05.50]` would be misread as 50ms.
    if len == 0 {
        return 0;
    }
    let div = 10f64.powi(len as i32);
    ((frac as f64) / div * 1000.0).round() as i64
}

/// Tokenize a lyric line into timestamps and text runs. `[` / `<` only become a timestamp
/// when followed by a digit, so bracketed non-timestamp text stays intact.
fn tokenize_spl(line: &str) -> Vec<RawPart> {
    let chars: Vec<char> = line.chars().collect();
    let n = chars.len();
    let mut parts = Vec::new();
    let mut text = String::new();
    let mut i = 0;
    while i < n {
        let c = chars[i];
        let ts_open = (c == '[' || c == '<') && i + 1 < n && chars[i + 1].is_ascii_digit();
        if ts_open {
            let close = if c == '[' { ']' } else { '>' };
            if let Some((ms, next)) = parse_mmss_at(&chars, i, close) {
                if !text.is_empty() {
                    parts.push(RawPart::Text(std::mem::take(&mut text)));
                }
                parts.push(RawPart::Ts(ms));
                i = next;
                continue;
            }
        }
        text.push(c);
        i += 1;
    }
    if !text.is_empty() {
        parts.push(RawPart::Text(text));
    }
    parts
}

/// Remove every timestamp token from a line, keeping only the visible text.
fn strip_timestamps(line: &str) -> String {
    let mut out = String::new();
    for p in tokenize_spl(line) {
        if let RawPart::Text(t) = p {
            out.push_str(&t);
        }
    }
    out
}

/// From tokenized parts, extract per-word timings. Returns (karaoke_flag, words). Karaoke is
/// true when the line splits into >= 2 timed words. A trailing word whose end is unknown gets
/// `end_time = 0`, filled in later once the line's real end is known.
fn spl_word_timings(parts: &[RawPart]) -> (bool, Vec<WordTiming>) {
    let Some(first) = parts.iter().position(|p| matches!(p, RawPart::Ts(_))) else {
        return (false, Vec::new());
    };
    // (start_ms, raw text following the timestamp, up to the next timestamp)
    let mut tss: Vec<(i64, String)> = Vec::new();
    for k in first..parts.len() {
        if let RawPart::Ts(ms) = parts[k] {
            let text = match parts.get(k + 1) {
                Some(RawPart::Text(t)) => t.clone(),
                _ => String::new(),
            };
            tss.push((ms, text));
        }
    }
    let mut words = Vec::new();
    // SPDX lyric writers place the inter-word space in three interchangeable spots:
    //  - standalone `[..] [..]` token (CJK gap), or
    //  - trailing whitespace of the previous word's run (`Say [t]my `), or
    //  - leading whitespace of the next word's run.
    // All three must collapse to a single visible gap between the neighbouring words. We carry a
    // `pending_gap` across runs and emit one `" "` word before a word whose boundary has whitespace.
    let mut pending_gap = false;
    for (ms, raw) in &tss {
        let trimmed = raw.trim().to_string();
        if trimmed.is_empty() {
            if !raw.is_empty() {
                pending_gap = true;
            }
            continue;
        }
        let has_leading_ws = raw.chars().next().map_or(false, |c| c.is_whitespace());
        if pending_gap || has_leading_ws {
            if !words.is_empty() {
                words.push(WordTiming { text: " ".to_string(), start_time: *ms, end_time: *ms });
            }
        }
        pending_gap = raw.chars().next_back().map_or(false, |c| c.is_whitespace());
        words.push(WordTiming { text: trimmed, start_time: *ms, end_time: 0 });
    }
    // Give every word (except the last) a real end = the next word's start.
    for i in 0..words.len().saturating_sub(1) {
        words[i].end_time = words[i + 1].start_time;
    }
    (words.len() >= 2, words)
}

/// Parse a single `[mm:ss.xx]` at the start of a line -> elapsed ms. None if not matching.
fn parse_first_timestamp(line: &str) -> Option<i64> {
    let b = line.as_bytes();
    if b.first() != Some(&b'[') {
        return None;
    }
    let close = line.find(']')?;
    let inside = &line[1..close];
    let (minutes_s, rest) = inside.split_once(':')?;
    let minutes = minutes_s.parse::<i64>().ok()?;
    let (seconds_s, frac_s) = rest.split_once('.').unwrap_or((rest, "0"));
    let seconds = seconds_s.parse::<i64>().ok()?;
    let frac = frac_s.parse::<i64>().unwrap_or(0);
    let ms = sec_frac_to_ms(frac, frac_s.len());
    Some((minutes * 60 + seconds) * 1000 + ms)
}

/// Parse the whole lyric document into sorted `LyricsLine`s (port of `ParseLyrics`).
pub fn parse_lyrics(lyrics_text: &str) -> Vec<LyricsLine> {
    if lyrics_text.is_empty() {
        return Vec::new();
    }
    // Group lines by their first timestamp; a translation line shares the main line's start.
    let mut groups: Vec<(i64, Vec<String>)> = Vec::new();
    for raw in lyrics_text.split('\n') {
        let line = raw.trim_end_matches('\r');
        let Some(t) = parse_first_timestamp(line) else { continue; };
        if let Some(g) = groups.iter_mut().find(|g| g.0 == t) {
            g.1.push(line.to_string());
        } else {
            groups.push((t, vec![line.to_string()]));
        }
    }

    let mut lines: Vec<LyricsLine> = Vec::new();
    for (start_time, raws) in groups {
        if raws.is_empty() {
            continue;
        }
        // First raw line is the main lyric (may be SPL karaoke); the rest same-start lines are translation.
        let orig_raw = &raws[0];
        let parts = tokenize_spl(orig_raw);
        let (is_ktv, word_timings) = spl_word_timings(&parts);
        let original = strip_timestamps(orig_raw).trim().to_string();

        let mut translation = Vec::new();
        for other in &raws[1..] {
            let c = strip_timestamps(other).trim().to_string();
            if !c.is_empty() {
                translation.push(c);
            }
        }

        lines.push(LyricsLine {
            original_text: original,
            translation_text: translation.join(" "),
            is_word_timing: is_ktv,
            word_timings: if is_ktv { word_timings } else { Vec::new() },
            start_time,
            end_time: 0,
        });
    }

    lines.sort_by_key(|l| l.start_time);
    for i in 0..lines.len() {
        lines[i].end_time = if i + 1 < lines.len() {
            lines[i + 1].start_time
        } else {
            lines[i].start_time + 5000
        };
    }
    for l in lines.iter_mut().filter(|l| l.is_word_timing) {
        if let Some(last) = l.word_timings.last_mut() {
            if last.end_time == 0 {
                last.end_time = l.end_time.max(l.start_time + 1);
            }
        }
    }
    lines
}

/// Drop any parsed line whose original text matches `pattern`. An empty (or invalid) pattern
/// keeps everything, so a typo can never blank the whole lyric. Filtering is applied to the
/// main lyric text only, not the translation.
pub fn filter_lines(lines: Vec<LyricsLine>, pattern: &str) -> Vec<LyricsLine> {
    let trimmed = pattern.trim();
    if trimmed.is_empty() {
        return lines;
    }
    let Ok(re) = regex_lite::Regex::new(trimmed) else {
        return lines;
    };
    lines.into_iter().filter(|l| !re.is_match(&l.original_text)).collect()
}

/// The line active at `position` (port of `GetCurrentLyricsLine`).
pub fn get_current_line(lines: &[LyricsLine], position: i64) -> Option<LyricsLine> {
    if lines.is_empty() {
        return None;
    }
    for (i, line) in lines.iter().enumerate() {
        let next_start = if i + 1 < lines.len() { lines[i + 1].start_time } else { i64::MAX };
        if position >= line.start_time && position < next_start {
            return Some(line.clone());
        }
    }
    if position < lines[0].start_time {
        return Some(lines[0].clone());
    }
    Some(lines[lines.len() - 1].clone())
}

/// Cubic-eased progress in 0..=1 of a [start, end) window at `position`.
pub fn word_progress(start_time: i64, end_time: i64, position: i64) -> f64 {
    if position < start_time {
        return 0.0;
    }
    if position >= end_time {
        return 1.0;
    }
    let total = end_time - start_time;
    if total <= 0 {
        return 1.0;
    }
    let p = ((position - start_time) as f64 / total as f64).clamp(0.0, 1.0);
    1.0 - (1.0 - p).powi(3)
}