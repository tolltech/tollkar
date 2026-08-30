import { test } from 'node:test'
import assert from 'node:assert/strict'
import { synchronizeMedia } from '../src/player/media.ts'

const state = { revision: 4, isPlaying: true, positionSeconds: 42, receivedAt: 1000 }
function media() {
  return { currentTime: 0, duration: 30, readyState: 1, error: null, paused: true, pause() { this.paused = true } }
}

test('a decode failure after metadata does not silently advance the queue', () => {
  const video = { ...media(), error: { code: 3 } as MediaError }
  synchronizeMedia(video, state, 1000, {
    play() { assert.fail('failed media must not restart') },
    ended() { assert.fail('failed media requires manual next') },
  })
  assert.equal(video.currentTime, 0)
})

test('missing metadata does not play or advance an unavailable file', () => {
  synchronizeMedia({ ...media(), readyState: 0 }, state, 1000, {
    play() { assert.fail('metadata unavailable') }, ended() { assert.fail('metadata unavailable') },
  })
})

test('a restored song past its duration advances without waiting for an ended event', () => {
  let transitions = 0
  const video = media()
  synchronizeMedia(video, state, 1000, { play() { assert.fail('must not restart finished song') }, ended() { transitions++ } })
  assert.equal(transitions, 1)
})

test('a skipped ended command is retried on synchronization until state changes', () => {
  let busy = true
  let transitions = 0
  const video = media()
  const actions = { play() {}, ended() { if (!busy) transitions++ } }
  synchronizeMedia(video, state, 1000, actions)
  busy = false
  synchronizeMedia(video, state, 2000, actions)
  assert.equal(transitions, 1)
})

test('paused timeline at the end does not advance and seeks stay synchronized', () => {
  const video = media()
  synchronizeMedia(video, { ...state, isPlaying: false }, 1000, {
    play() { assert.fail('paused') }, ended() { assert.fail('paused at end') },
  })
  assert.equal(video.currentTime, 30)
  assert.equal(video.paused, true)
})
