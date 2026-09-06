#!/usr/bin/env python3
"""Convert matching .lrc/.mp3 pairs into KFN files understood by Tollkar.

The converter intentionally keeps the MP3 bytes unchanged. LRC files normally time
whole lines, so word marks are approximated by evenly distributing each line's
duration across its words.
"""

from __future__ import annotations

import argparse
import re
import struct
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Optional


TIMESTAMP = re.compile(r"\[(\d+):(\d{2})(?:[.:](\d{1,3}))?\]")
TAG = re.compile(r"\[([A-Za-z]+):([^]]*)\]")


@dataclass(frozen=True)
class LrcLine:
    start_ms: int
    text: str


@dataclass(frozen=True)
class LrcSong:
    title: Optional[str]
    artist: Optional[str]
    lines: tuple[LrcLine, ...]


def read_lrc(path: Path) -> LrcSong:
    raw = path.read_bytes()
    try:
        text = raw.decode("utf-8-sig")
    except UnicodeDecodeError:
        text = raw.decode("cp1251")

    title: Optional[str] = None
    artist: Optional[str] = None
    lines: list[LrcLine] = []
    for source_line in text.splitlines():
        timestamps = list(TIMESTAMP.finditer(source_line))
        if not timestamps:
            for tag, value in TAG.findall(source_line):
                if tag.lower() == "ti" and value.strip():
                    title = value.strip()
                elif tag.lower() == "ar" and value.strip():
                    artist = value.strip()
            continue

        lyric = TIMESTAMP.sub("", source_line).strip()
        if not lyric:
            continue
        for timestamp in timestamps:
            minutes = int(timestamp.group(1))
            seconds = int(timestamp.group(2))
            fraction = timestamp.group(3) or "0"
            fraction_ms = int((fraction + "000")[:3])
            lines.append(LrcLine((minutes * 60 + seconds) * 1000 + fraction_ms, lyric))

    lines.sort(key=lambda line: line.start_ms)
    return LrcSong(title, artist, tuple(lines))


def clean_words(text: str) -> list[str]:
    # KFN's reader treats spaces and slashes as token separators. Normalising
    # whitespace keeps the generated token count and visible text predictable.
    return re.findall(r"\S+", text)


def centisecond_marks(song: LrcSong, duration_ms: Optional[int]) -> tuple[list[str], list[int]]:
    text_lines: list[str] = []
    marks: list[int] = []
    for index, line in enumerate(song.lines):
        words = clean_words(line.text)
        if not words:
            continue
        next_start = song.lines[index + 1].start_ms if index + 1 < len(song.lines) else None
        end = next_start
        if end is None and duration_ms is not None:
            end = max(duration_ms, line.start_ms + 1000)
        if end is None or end <= line.start_ms:
            end = line.start_ms + max(1000, len(words) * 250)

        span = max(len(words), end - line.start_ms)
        for word_index in range(len(words)):
            mark_ms = line.start_ms + (span * word_index // len(words))
            marks.append(max(0, mark_ms // 10))
        text_lines.append(" ".join(words))
    return text_lines, marks


def probe_duration(path: Path) -> Optional[int]:
    try:
        result = subprocess.run(
            ["ffprobe", "-v", "error", "-show_entries", "format=duration",
             "-of", "default=noprint_wrappers=1:nokey=1", str(path)],
            check=True,
            capture_output=True,
            text=True,
        )
        return round(float(result.stdout.strip()) * 1000)
    except (FileNotFoundError, ValueError, subprocess.CalledProcessError):
        return None


def song_ini(song: LrcSong, audio_name: str, duration_ms: Optional[int]) -> bytes:
    text_lines, marks = centisecond_marks(song, duration_ms)
    title = song.title or ""
    artist = song.artist or ""
    chunks: list[str] = [
        "[General]",
        f"Title={title}",
        f"Artist={artist}",
        "GlobalShift=0",
        f"Source=1,I,{audio_name}",
        "",
        "[EffN]",
        f"TextCount={len(text_lines)}",
    ]
    chunks.extend(f"Text{index}={line}" for index, line in enumerate(text_lines))
    # Keep Sync values reasonably small, although the reader accepts a flat sequence
    # split over any number of numbered Sync keys.
    for index in range(0, len(marks), 64):
        chunks.append(f"Sync{index // 64}={','.join(map(str, marks[index:index + 64]))}")
    return ("\n".join(chunks) + "\n").encode("utf-8")


def write_kfn(output: Path, song: LrcSong, mp3: Path, duration_ms: int | None) -> None:
    audio_name = "audio.mp3"
    definition = song_ini(song, audio_name, duration_ms)
    entries = [("Song.ini", 1, definition), (audio_name, 2, None)]
    output.parent.mkdir(parents=True, exist_ok=True)

    with output.open("wb") as target:
        target.write(b"KFNB")
        for name, value in (("TITL", song.title or ""), ("ARTS", song.artist or "")):
            encoded = value.encode("utf-8")
            target.write(name.encode("ascii"))
            target.write(b"\x02")
            target.write(struct.pack("<i", len(encoded)))
            target.write(encoded)
        target.write(b"ENDH\x01")
        target.write(struct.pack("<i", -1))
        target.write(struct.pack("<i", len(entries)))

        payload_offset = 0
        payloads: list[bytes | None] = []
        for name, kind, content in entries:
            encoded_name = name.encode("ascii")
            payload_length = len(content) if content is not None else mp3.stat().st_size
            target.write(struct.pack("<i", len(encoded_name)))
            target.write(encoded_name)
            target.write(struct.pack("<iiiii", kind, payload_length, payload_offset,
                                     payload_length, 0))
            payloads.append(content)
            payload_offset += payload_length

        target.write(definition)
        with mp3.open("rb") as source:
            while chunk := source.read(1024 * 1024):
                target.write(chunk)


def convert_directory(input_dir: Path, output_dir: Path, overwrite: bool) -> int:
    converted = 0
    failures = 0
    for lrc in sorted(input_dir.glob("*.lrc")):
        mp3 = lrc.with_suffix(".mp3")
        if not mp3.is_file():
            print(f"SKIP {lrc.name}: matching MP3 not found", file=sys.stderr)
            continue
        output = output_dir / f"{lrc.stem}.kfn"
        if output.exists() and not overwrite:
            print(f"SKIP {output.name}: already exists (use --overwrite)", file=sys.stderr)
            continue
        try:
            song = read_lrc(lrc)
            if not song.lines:
                raise ValueError("no timed lyric lines")
            write_kfn(output, song, mp3, probe_duration(mp3))
            print(f"OK   {output}")
            converted += 1
        except (OSError, UnicodeError, ValueError) as error:
            print(f"FAIL {lrc.name}: {error}", file=sys.stderr)
            failures += 1
    return 1 if failures else 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input_dir", type=Path, help="directory containing .lrc/.mp3 pairs")
    parser.add_argument("-o", "--output-dir", type=Path,
                        help="destination directory (default: input directory)")
    parser.add_argument("--overwrite", action="store_true", help="replace existing .kfn files")
    args = parser.parse_args()
    input_dir = args.input_dir.expanduser().resolve()
    output_dir = (args.output_dir or input_dir).expanduser().resolve()
    if not input_dir.is_dir():
        parser.error(f"input directory does not exist: {input_dir}")
    return convert_directory(input_dir, output_dir, args.overwrite)


if __name__ == "__main__":
    raise SystemExit(main())
