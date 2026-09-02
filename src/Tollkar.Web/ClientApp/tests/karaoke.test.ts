import { test } from 'node:test'
import assert from 'node:assert/strict'
import { activeLineIndex, lineText, syllableFill, type LyricLine } from '../src/player/karaoke.ts'

function line(startMs: number, ...syllables: [string, number, number][]): LyricLine {
  return {
    startMs,
    endMs: syllables[syllables.length - 1][2],
    syllables: syllables.map(([text, start, end]) => ({ text, startMs: start, endMs: end })),
  }
}

const lines = [
  line(1000, ['КА', 1000, 1300], ['ЖЕТ', 1300, 1600], ['СЯ', 1600, 2000]),
  line(5000, ['ГЛА', 5000, 5400], ['ЗА', 5400, 6000]),
]

test('the first line is shown before the song reaches its opening mark', () => {
  assert.equal(activeLineIndex(lines, 0), 0)
})

test('the line stays current through an instrumental gap until the next one starts', () => {
  assert.equal(activeLineIndex(lines, 2500), 0)
  assert.equal(activeLineIndex(lines, 4999), 0)
  assert.equal(activeLineIndex(lines, 5000), 1)
})

test('the last line stays current to the end of the song', () => {
  assert.equal(activeLineIndex(lines, 600000), 1)
})

test('a line reads back exactly as written, with word spacing kept', () => {
  assert.equal(lineText(line(0, ['ЧТО ', 0, 1], ['ВСЁ ', 1, 2], ['БЛИЗ', 2, 3], ['КО', 3, 4])),
    'ЧТО ВСЁ БЛИЗКО')
})

test('a syllable is empty before it starts and full once it ends', () => {
  const [syllable] = lines[0].syllables
  assert.equal(syllableFill(syllable, 0), 0)
  assert.equal(syllableFill(syllable, 1000), 0)
  assert.equal(syllableFill(syllable, 1300), 1)
  assert.equal(syllableFill(syllable, 600000), 1)
})

test('a syllable fills evenly across its own span', () => {
  assert.equal(syllableFill(lines[0].syllables[1], 1450), 0.5)
  assert.equal(syllableFill(lines[0].syllables[1], 1375), 0.25)
})

test('a syllable with no span fills at once instead of dividing by zero', () => {
  assert.equal(syllableFill({ text: 'РАЗ', startMs: 100, endMs: 100 }, 100), 1)
})
