import { test } from 'node:test'
import assert from 'node:assert/strict'
import { playbackPosition } from '../src/player/timeline.ts'

test('restored playing position advances from receipt without depending on device clock', () => {
  const state = { revision: 4, isPlaying: true, positionSeconds: 42, receivedAt: 1000 }
  assert.equal(playbackPosition(state, 4500), 45.5)
  assert.equal(playbackPosition(state, 100000, 60), 60)
  assert.equal(playbackPosition(state, 500), 42)
})

test('pause and seek snapshots remain fixed while local time advances', () => {
  const state = { revision: 5, isPlaying: false, positionSeconds: 12, receivedAt: 1000 }
  assert.equal(playbackPosition(state, 100000), 12)
  assert.equal(playbackPosition({ ...state, revision: 6, positionSeconds: 80 }, 100000), 80)
})
