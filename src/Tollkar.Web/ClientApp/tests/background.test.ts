import { test } from 'node:test'
import assert from 'node:assert/strict'
import { synchronizeBackground } from '../src/player/media.ts'

const state = { revision: 1, isPlaying: true, positionSeconds: 70, receivedAt: 1000 }
function backdrop() {
  return { currentTime: 0, duration: 30, readyState: 1, error: null, paused: true, pause() { this.paused = true } }
}

test('a short looping clip follows the song by wrapping around', () => {
  const media = backdrop()
  synchronizeBackground(media, state, 1000, true, () => { media.paused = false })
  assert.equal(media.currentTime, 10)
  assert.equal(media.paused, false)
})

test('a clip that does not loop holds its last frame instead of restarting', () => {
  const media = backdrop()
  synchronizeBackground(media, state, 1000, false, () => assert.fail('a finished backdrop must not play'))
  assert.equal(media.currentTime, 30)
  assert.equal(media.paused, true)
})

test('a paused song pauses the backdrop at the same position', () => {
  const media = backdrop()
  synchronizeBackground(media, { ...state, isPlaying: false, positionSeconds: 12 }, 1000, false,
    () => assert.fail('a paused song must not play its backdrop'))
  assert.equal(media.currentTime, 12)
  assert.equal(media.paused, true)
})

test('small drift is left alone so the backdrop is not restarted every second', () => {
  const media = { ...backdrop(), currentTime: 10.5 }
  synchronizeBackground(media, state, 1000, true, () => {})
  assert.equal(media.currentTime, 10.5)
})

test('a backdrop without metadata or duration is not touched', () => {
  const missing = { ...backdrop(), readyState: 0 }
  synchronizeBackground(missing, state, 1000, true, () => assert.fail('metadata unavailable'))
  assert.equal(missing.currentTime, 0)

  const empty = { ...backdrop(), duration: NaN }
  synchronizeBackground(empty, state, 1000, true, () => assert.fail('duration unavailable'))
  assert.equal(empty.currentTime, 0)
})
