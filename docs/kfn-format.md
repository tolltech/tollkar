# KFN containers

A KFN (KaraFun) file is a container, not a media file: it holds the backing track, an optional
backdrop clip and a script that says when every syllable is sung. The catalog indexes `.kfn`
alongside `.mp4`, and the web player assembles the three parts itself. This note records the layout
because it is undocumented by its vendor and was recovered by reading real files.

## Layout

```
"KFNB"
header fields   <4-byte signature><type:1>
                type 1 -> int32 value;  type 2 -> int32 length + bytes
                repeated until the "ENDH" signature
int32           entry count
entries         int32 name length, name (Windows-1251),
                int32 kind, int32 length, int32 offset, int32 stored length, int32 encryption
payloads        written back to back; offsets are relative to the end of the entry table
```

Entry kinds are `1` song definition, `2` audio, `3` image, `4` font, `5` video, `6` visualization
preset. `length` is the size after decryption and `stored length` the size occupied in the file;
they differ only for encrypted entries, which are padded to whole cipher blocks.

Encryption `1` means AES-128-ECB keyed by the 16 raw bytes of the `FLID` header field. In practice
only `Song.ini` is ever encrypted, so `KfnArchive.OpenEntry` streams plain entries straight from the
file and buffers encrypted ones in memory.

Encodings are mixed inside one file: entry names and the `Source`/`VideoFile` references are
Windows-1251, while `TITL`, `ARTS` and the lyrics are UTF-8. `KfnText` tells them apart by trying
strict UTF-8 first, which a Cyrillic Windows-1251 string practically never satisfies.

## Song.ini

`[General]` carries `Title`, `Artist`, `GlobalShift` and `Source`, written as `1,I,<file name>`.
`Source` names the track to play, which matters for files shipping both a backing track and a guide
vocal. Placeholder values such as `-` are common and are treated as absent.

Lyrics live in the `[EffN]` section that declares `TextCount`. `TextN` is one line with syllables
separated by `/`; `SyncN` holds start marks in centiseconds. A sung token is the line split on both
`/` and space, so a syllable that ends a word keeps its trailing space and a line reads back
unchanged. The numbered `SyncN` keys are one flat sequence, split only for line length, and are
matched to tokens by position. Blank `TextN` values are layout and consume no marks.

Marks give starts only, so a syllable ends at the next mark, capped so a held note cannot keep the
highlight running through an instrumental break. Real files are not always consistent: of twenty
sampled files nineteen matched token for token and one carried surplus marks, so the parser matches
what it can, ignores the rest and never fails a song over its script.

The backdrop is named by `VideoFile` in whichever effect section declares it, with `LoopVideo`
alongside. The sentinel `UseMusicSource` means a generated visualization and is not a clip.

## What the player gets

Backdrop clips are routinely named `.avi` while actually being MP4, and browsers play those
byte for byte. `KfnSong` therefore accepts a backdrop only when its payload really starts with an
MP4 `ftyp` box; genuine AVI clips are reported as no backdrop rather than served as a broken video.
The backdrop is always muted — the sound comes from the track, and some clips carry their own.

`KfnSongFormatProvider` takes the title from `TITL`, then `Song.ini`, then the file name, and the
artist from `ARTS`, then `Song.ini`, then the containing folder: KFN files are usually filed under a
folder named after the artist with only the title in the file name.
