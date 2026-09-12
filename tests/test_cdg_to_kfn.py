import importlib.util
import io
import stat
import struct
import sys
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
from unittest.mock import patch


SCRIPT = Path(__file__).parents[1] / "scripts" / "cdg-to-kfn.py"
SPEC = importlib.util.spec_from_file_location("cdg_to_kfn", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
cdg_to_kfn = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = cdg_to_kfn
SPEC.loader.exec_module(cdg_to_kfn)


class CdgToKfnTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)

    def tearDown(self):
        self.temporary.cleanup()

    def make_file(self, relative_path, content=b""):
        path = self.root / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)
        return path

    def test_discovers_recursive_case_insensitive_and_single_file_pairs(self):
        exact_cdg = self.make_file("set/Artist - First/song.CDG")
        exact_mp3 = self.make_file("set/Artist - First/SONG.mp3")
        typo_cdg = self.make_file("set/Artist - Second/Second..cdg")
        typo_mp3 = self.make_file("set/Artist - Second/Second.mp3")
        orphan = self.make_file("set/Artist - Third/orphan.cdg")

        pairs, unmatched = cdg_to_kfn.discover_pairs(self.root)

        self.assertEqual(
            {(pair.cdg, pair.mp3) for pair in pairs},
            {(exact_cdg, exact_mp3), (typo_cdg, typo_mp3)},
        )
        self.assertEqual(unmatched, [orphan])

    def test_metadata_preserves_version_suffix(self):
        cdg = self.make_file("collection/Aerosmith - Amazing (SC)/track.cdg")
        mp3 = self.make_file("collection/Aerosmith - Amazing (SC)/track.mp3")

        metadata = cdg_to_kfn.metadata_for(cdg_to_kfn.SongPair(cdg, mp3))

        self.assertEqual(metadata.artist, "Aerosmith")
        self.assertEqual(metadata.title, "Amazing (SC)")

    def test_writes_kfn_with_unchanged_mp3_and_video(self):
        mp3_content = b"ID3 unchanged audio"
        video_content = b"\x00\x00\x00\x18ftypisom rendered video"
        mp3 = self.make_file("audio.mp3", mp3_content)
        video = self.make_file("background.mp4", video_content)
        output = self.root / "song.kfn"

        cdg_to_kfn.write_kfn(
            output,
            cdg_to_kfn.SongMetadata("Amazing (SC)", "Aerosmith"),
            mp3,
            video,
        )

        fields, entries = read_kfn(output)
        self.assertEqual(fields["TITL"], "Amazing (SC)".encode())
        self.assertEqual(fields["ARTS"], b"Aerosmith")
        self.assertEqual(entries["audio.mp3"], (2, mp3_content))
        self.assertEqual(entries["background.mp4"], (5, video_content))
        definition = entries["Song.ini"][1].decode()
        self.assertIn("VideoFile=background.mp4", definition)
        self.assertIn("TextCount=0", definition)

    def test_convert_pair_replaces_output_and_removes_staging_files(self):
        cdg = self.make_file("source/Artist - Song/song.cdg", b"cdg")
        mp3 = self.make_file("source/Artist - Song/song.mp3", b"mp3")
        output = self.root / "output" / "song.kfn"

        def fake_render(_ffmpeg, _cdg, background):
            background.write_bytes(b"\x00\x00\x00\x18ftypisom")

        with patch.object(cdg_to_kfn, "render_background", side_effect=fake_render):
            cdg_to_kfn.convert_pair(cdg_to_kfn.SongPair(cdg, mp3), output, "ffmpeg")

        self.assertTrue(output.is_file())
        self.assertEqual(stat.S_IMODE(output.stat().st_mode), 0o644)
        self.assertEqual(list(output.parent.glob(".*")), [])

    def test_failed_render_does_not_replace_existing_output(self):
        cdg = self.make_file("source/Artist - Song/song.cdg", b"cdg")
        mp3 = self.make_file("source/Artist - Song/song.mp3", b"mp3")
        output = self.make_file("output/song.kfn", b"existing")

        with patch.object(cdg_to_kfn, "render_background", side_effect=RuntimeError("bad CDG")):
            with self.assertRaisesRegex(RuntimeError, "bad CDG"):
                cdg_to_kfn.convert_pair(cdg_to_kfn.SongPair(cdg, mp3), output, "ffmpeg")

        self.assertEqual(output.read_bytes(), b"existing")
        self.assertEqual(list(output.parent.glob(".*")), [])

    def test_second_staging_failure_removes_first_temporary_file(self):
        cdg = self.make_file("source/Artist - Song/song.cdg", b"cdg")
        mp3 = self.make_file("source/Artist - Song/song.mp3", b"mp3")
        output = self.root / "output" / "song.kfn"
        real_temporary_path = cdg_to_kfn.temporary_path
        call_count = 0

        def fail_second_staging(directory, prefix, suffix):
            nonlocal call_count
            call_count += 1
            if call_count == 2:
                raise OSError("disk full")
            return real_temporary_path(directory, prefix, suffix)

        with patch.object(cdg_to_kfn, "temporary_path", side_effect=fail_second_staging):
            with self.assertRaisesRegex(OSError, "disk full"):
                cdg_to_kfn.convert_pair(cdg_to_kfn.SongPair(cdg, mp3), output, "ffmpeg")

        self.assertEqual(list(output.parent.glob(".*")), [])

    def test_dry_run_preserves_relative_tree_without_ffmpeg(self):
        self.make_file("collection/Artist - Song (SC)/track.cdg")
        self.make_file("collection/Artist - Song (SC)/track.mp3")
        output_dir = self.root / "output"

        output = io.StringIO()
        with redirect_stdout(output):
            result = cdg_to_kfn.convert_directory(
                self.root / "collection", output_dir, overwrite=False, dry_run=True, ffmpeg=None
            )

        self.assertEqual(result, 0)
        self.assertIn("planned=1 converted=0", output.getvalue())
        self.assertFalse(output_dir.exists())
        pair = cdg_to_kfn.discover_pairs(self.root / "collection")[0][0]
        self.assertEqual(
            cdg_to_kfn.output_for(pair, self.root / "collection", output_dir),
            output_dir / "Artist - Song (SC)" / "track.kfn",
        )

    def test_existing_output_is_skipped_unless_overwrite_is_requested(self):
        cdg = self.make_file("input/Artist - Song/track.cdg")
        mp3 = self.make_file("input/Artist - Song/track.mp3")
        output_dir = self.root / "output"
        output = self.make_file("output/Artist - Song/track.kfn", b"existing")

        with patch.object(cdg_to_kfn, "convert_pair") as convert:
            with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
                skipped = cdg_to_kfn.convert_directory(
                    self.root / "input", output_dir, False, False, "ffmpeg"
                )
                overwritten = cdg_to_kfn.convert_directory(
                    self.root / "input", output_dir, True, False, "ffmpeg"
                )

        self.assertEqual(skipped, 0)
        self.assertEqual(overwritten, 0)
        convert.assert_called_once_with(cdg_to_kfn.SongPair(cdg, mp3), output, "ffmpeg")

    def test_main_reports_missing_ffmpeg_before_conversion(self):
        self.make_file("input/Artist - Song/track.cdg")
        self.make_file("input/Artist - Song/track.mp3")

        errors = io.StringIO()
        with patch.object(cdg_to_kfn.shutil, "which", return_value=None):
            with redirect_stderr(errors):
                result = cdg_to_kfn.main([str(self.root / "input")])

        self.assertEqual(result, 2)
        self.assertIn("FFmpeg executable not found", errors.getvalue())

    def test_detects_case_insensitive_output_conflicts(self):
        pairs = [
            cdg_to_kfn.SongPair(Path("/input/A/track.cdg"), Path("/input/A/track.mp3")),
            cdg_to_kfn.SongPair(Path("/input/a/TRACK.CDG"), Path("/input/a/TRACK.MP3")),
        ]

        conflicts = cdg_to_kfn.conflicting_outputs(
            pairs, Path("/input"), Path("/output")
        )

        self.assertEqual(
            conflicts,
            {Path("/output/A/track.kfn"), Path("/output/a/TRACK.kfn")},
        )


def read_int32(content, position):
    return struct.unpack_from("<i", content, position)[0], position + 4


def read_kfn(path):
    content = path.read_bytes()
    position = 4
    fields = {}
    while True:
        name = content[position : position + 4].decode("ascii")
        position += 4
        field_type = content[position]
        position += 1
        value, position = read_int32(content, position)
        if name == "ENDH":
            break
        if field_type != 2:
            raise AssertionError(f"unexpected field type {field_type}")
        fields[name] = content[position : position + value]
        position += value

    count, position = read_int32(content, position)
    table = []
    for _ in range(count):
        name_length, position = read_int32(content, position)
        name = content[position : position + name_length].decode("cp1251")
        position += name_length
        kind, position = read_int32(content, position)
        length, position = read_int32(content, position)
        offset, position = read_int32(content, position)
        stored_length, position = read_int32(content, position)
        _encrypted, position = read_int32(content, position)
        table.append((name, kind, length, offset, stored_length))

    data_origin = position
    entries = {}
    for name, kind, length, offset, stored_length in table:
        if stored_length != length:
            raise AssertionError("plain test entry has different stored length")
        payload = content[data_origin + offset : data_origin + offset + length]
        entries[name] = (kind, payload)
    return fields, entries


if __name__ == "__main__":
    unittest.main()
