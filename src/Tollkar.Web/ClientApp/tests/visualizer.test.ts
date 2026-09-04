import { test } from 'node:test'
import assert from 'node:assert/strict'
import { shouldPaintVisualizer, visualizerFrameIntervalMs, visualizerPixelRatio } from '../src/player/visualizer.ts'

test('the visualizer has a low fixed refresh rate', () => {
  assert.equal(visualizerFrameIntervalMs, 50)
})

test('the visualizer only paints while the track plays in a visible document', () => {
  assert.equal(shouldPaintVisualizer(true, false), true)
  assert.equal(shouldPaintVisualizer(false, false), false)
  assert.equal(shouldPaintVisualizer(true, true), false)
})

test('the visualizer caps its canvas resolution for a 4K television', () => {
  assert.equal(visualizerPixelRatio(3840, 2160, 1), 1 / 3)
  assert.equal(visualizerPixelRatio(1280, 720, 2), 1)
})
