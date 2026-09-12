#!/usr/bin/env python3
"""Convert a recursive tree of matching MP3/CDG files into Tollkar KFN files."""

from __future__ import annotations

import argparse
import os
import shutil
import struct
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, Optional, Sequence, Union


DEFAULT_INPUT_DIR = Path("/Volumes/tollcloud/klavish")
DEFAULT_OUTPUT_DIR = Path("/Volumes/tollcloud/karaoke/klavish")
INT32_MAX = 2_147_483_647
SONG_DEFINITION = 1
AUDIO = 2
VIDEO = 5


@dataclass(frozen=True)
class SongPair:
    cdg: Path
    mp3: Path


@dataclass(frozen=True)
class SongMetadata:
    title: str
    artist: str


@dataclass(frozen=True)
class KfnEntry:
    name: str
    kind: int
    content: Union[bytes, Path]


def path_sort_key(path: Path) -> str:
    return str(path).casefold()


def discover_pairs(input_dir: Path) -> tuple[list[SongPair], list[Path]]:
    directories: dict[Path, dict[str, list[Path]]] = {}
    for path in sorted(input_dir.rglob("*"), key=path_sort_key):
        if not path.is_file():
            continue
        suffix = path.suffix.casefold()
        if suffix not in {".cdg", ".mp3"}:
            continue
        files = directories.setdefault(path.parent, {".cdg": [], ".mp3": []})
        files[suffix].append(path)

    pairs: list[SongPair] = []
    unmatched_cdg: list[Path] = []
    for files in directories.values():
        cdg_files = files[".cdg"]
        mp3_files = files[".mp3"]
        unused_mp3 = set(mp3_files)
        unmatched_in_directory: list[Path] = []

        for cdg in cdg_files:
            matches = [mp3 for mp3 in unused_mp3 if mp3.stem.casefold() == cdg.stem.casefold()]
            if len(matches) == 1:
                mp3 = matches[0]
                pairs.append(SongPair(cdg, mp3))
                unused_mp3.remove(mp3)
            else:
                unmatched_in_directory.append(cdg)

        # Some collections contain a single obvious pair whose names differ by a typo.
        if (
            len(cdg_files) == 1
            and len(mp3_files) == 1
            and len(unmatched_in_directory) == 1
            and len(unused_mp3) == 1
        ):
            pairs.append(SongPair(unmatched_in_directory[0], next(iter(unused_mp3))))
        else:
            unmatched_cdg.extend(unmatched_in_directory)

    pairs.sort(key=lambda pair: path_sort_key(pair.cdg))
    unmatched_cdg.sort(key=path_sort_key)
    return pairs, unmatched_cdg


def meaningful_ini_value(value: str) -> str:
    return " ".join(value.replace("\x00", " ").splitlines()).strip()


def metadata_for(pair: SongPair) -> SongMetadata:
    directory_name = pair.cdg.parent.name
    if " - " in directory_name:
        artist, title = directory_name.split(" - ", 1)
        artist = meaningful_ini_value(artist)
        title = meaningful_ini_value(title)
        if artist and title:
            return SongMetadata(title, artist)
    return SongMetadata(meaningful_ini_value(pair.cdg.stem), "")


def song_definition(metadata: SongMetadata) -> bytes:
    content = [
        "[General]",
        f"Title={metadata.title}",
        f"Artist={metadata.artist}",
        "GlobalShift=0",
        "Source=1,I,audio.mp3",
        "",
        "[EffN]",
        "VideoFile=background.mp4",
        "LoopVideo=0",
        "TextCount=0",
    ]
    return ("\n".join(content) + "\n").encode("utf-8")


def entry_length(entry: KfnEntry) -> int:
    length = len(entry.content) if isinstance(entry.content, bytes) else entry.content.stat().st_size
    if length > INT32_MAX:
        raise ValueError(f"entry '{entry.name}' is too large for a KFN container")
    return length


def copy_entry(target, content: Union[bytes, Path]) -> None:
    if isinstance(content, bytes):
        target.write(content)
        return
    with content.open("rb") as source:
        shutil.copyfileobj(source, target, length=1024 * 1024)


def write_kfn(output: Path, metadata: SongMetadata, mp3: Path, background: Path) -> None:
    entries = [
        KfnEntry("Song.ini", SONG_DEFINITION, song_definition(metadata)),
        KfnEntry("audio.mp3", AUDIO, mp3),
        KfnEntry("background.mp4", VIDEO, background),
    ]
    lengths = [entry_length(entry) for entry in entries]
    if sum(lengths) > INT32_MAX:
        raise ValueError("combined KFN payload is too large")

    with output.open("wb") as target:
        target.write(b"KFNB")
        for field, value in (("TITL", metadata.title), ("ARTS", metadata.artist)):
            encoded = value.encode("utf-8")
            target.write(field.encode("ascii"))
            target.write(b"\x02")
            target.write(struct.pack("<i", len(encoded)))
            target.write(encoded)
        target.write(b"ENDH\x01")
        target.write(struct.pack("<i", -1))
        target.write(struct.pack("<i", len(entries)))

        offset = 0
        for entry, length in zip(entries, lengths):
            encoded_name = entry.name.encode("cp1251")
            target.write(struct.pack("<i", len(encoded_name)))
            target.write(encoded_name)
            target.write(struct.pack("<iiiii", entry.kind, length, offset, length, 0))
            offset += length

        for entry in entries:
            copy_entry(target, entry.content)


def render_background(ffmpeg: str, cdg: Path, output: Path) -> None:
    command = [
        ffmpeg,
        "-hide_banner",
        "-loglevel",
        "error",
        "-nostdin",
        "-y",
        "-i",
        str(cdg),
        "-an",
        "-vf",
        "fps=30,scale=600:432:flags=neighbor,format=yuv420p",
        "-c:v",
        "libx264",
        "-preset",
        "veryfast",
        "-crf",
        "18",
        "-tune",
        "animation",
        "-movflags",
        "+faststart",
        str(output),
    ]
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        detail = result.stderr.strip() or f"FFmpeg exited with code {result.returncode}"
        raise RuntimeError(detail)
    with output.open("rb") as background:
        background.seek(4)
        if background.read(4) != b"ftyp":
            raise RuntimeError("FFmpeg did not produce an MP4 container")


def temporary_path(directory: Path, prefix: str, suffix: str) -> Path:
    descriptor, name = tempfile.mkstemp(dir=directory, prefix=prefix, suffix=suffix)
    os.close(descriptor)
    return Path(name)


def convert_pair(pair: SongPair, output: Path, ffmpeg: str) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    background: Optional[Path] = None
    staged_kfn: Optional[Path] = None
    try:
        background = temporary_path(output.parent, f".{output.stem}.", ".mp4")
        staged_kfn = temporary_path(output.parent, f".{output.name}.", ".tmp")
        render_background(ffmpeg, pair.cdg, background)
        write_kfn(staged_kfn, metadata_for(pair), pair.mp3, background)
        os.chmod(staged_kfn, 0o644)
        os.replace(staged_kfn, output)
    finally:
        if background is not None:
            background.unlink(missing_ok=True)
        if staged_kfn is not None:
            staged_kfn.unlink(missing_ok=True)


def output_for(pair: SongPair, input_dir: Path, output_dir: Path) -> Path:
    relative_parent = pair.cdg.parent.relative_to(input_dir)
    return output_dir / relative_parent / f"{pair.cdg.stem}.kfn"


def conflicting_outputs(
    pairs: Iterable[SongPair], input_dir: Path, output_dir: Path
) -> set[Path]:
    by_name: dict[str, list[Path]] = {}
    for pair in pairs:
        output = output_for(pair, input_dir, output_dir)
        by_name.setdefault(str(output).casefold(), []).append(output)
    return {path for paths in by_name.values() if len(paths) > 1 for path in paths}


def convert_directory(
    input_dir: Path,
    output_dir: Path,
    overwrite: bool,
    dry_run: bool,
    ffmpeg: Optional[str],
) -> int:
    pairs, unmatched_cdg = discover_pairs(input_dir)
    conflicts = conflicting_outputs(pairs, input_dir, output_dir)
    converted = 0
    planned = 0
    skipped = 0
    failures = 0

    for cdg in unmatched_cdg:
        print(f"SKIP {cdg}: matching MP3 not found", file=sys.stderr)
        skipped += 1

    for pair in pairs:
        output = output_for(pair, input_dir, output_dir)
        if output in conflicts:
            print(f"FAIL {pair.cdg}: conflicting output path {output}", file=sys.stderr)
            failures += 1
            continue
        if output.exists() and not overwrite:
            print(f"SKIP {output}: already exists (use --overwrite)", file=sys.stderr)
            skipped += 1
            continue
        if dry_run:
            print(f"PLAN {pair.cdg} + {pair.mp3} -> {output}")
            planned += 1
            continue
        try:
            if ffmpeg is None:
                raise RuntimeError("FFmpeg executable not found")
            convert_pair(pair, output, ffmpeg)
            print(f"OK   {output}")
            converted += 1
        except (OSError, RuntimeError, UnicodeError, ValueError) as error:
            print(f"FAIL {pair.cdg}: {error}", file=sys.stderr)
            failures += 1

    print(
        f"SUMMARY pairs={len(pairs)} planned={planned} converted={converted} "
        f"skipped={skipped} failed={failures}"
    )
    return 1 if failures else 0


def parse_args(arguments: Optional[Sequence[str]] = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "input_dir",
        nargs="?",
        type=Path,
        default=DEFAULT_INPUT_DIR,
        help=f"source tree (default: {DEFAULT_INPUT_DIR})",
    )
    parser.add_argument(
        "-o",
        "--output-dir",
        type=Path,
        default=DEFAULT_OUTPUT_DIR,
        help=f"destination tree (default: {DEFAULT_OUTPUT_DIR})",
    )
    parser.add_argument("--overwrite", action="store_true", help="replace existing KFN files")
    parser.add_argument("--dry-run", action="store_true", help="show conversions without writing files")
    return parser.parse_args(arguments)


def main(arguments: Optional[Sequence[str]] = None) -> int:
    args = parse_args(arguments)
    input_dir = args.input_dir.expanduser().resolve()
    output_dir = args.output_dir.expanduser().resolve()
    if not input_dir.is_dir():
        print(f"ERROR input directory does not exist: {input_dir}", file=sys.stderr)
        return 2
    ffmpeg = shutil.which("ffmpeg")
    if ffmpeg is None and not args.dry_run:
        print("ERROR FFmpeg executable not found in PATH", file=sys.stderr)
        return 2
    return convert_directory(input_dir, output_dir, args.overwrite, args.dry_run, ffmpeg)


if __name__ == "__main__":
    raise SystemExit(main())
