import assert from 'node:assert/strict'
import test from 'node:test'
import { remotePlaybackAction } from '../src/player/remote.ts'

test('TV playback keys work by name and legacy key code', () => {
  for (const [key, action] of [['MediaPlay', 'play'], ['MediaPause', 'pause'], ['MediaPlayPause', 'toggle']]) {
    assert.equal(remotePlaybackAction({ key, code: '', keyCode: 0 }), action)
  }
  assert.equal(remotePlaybackAction({ key: 'Unidentified', code: '', keyCode: 415 }), 'play')
  assert.equal(remotePlaybackAction({ key: '', code: '', keyCode: 19 }), 'pause')
  assert.equal(remotePlaybackAction({ key: '', code: '', keyCode: 10252 }), 'toggle')
  assert.equal(remotePlaybackAction({ key: '', code: 'MediaPlayPause', keyCode: 0 }), 'toggle')
  assert.equal(remotePlaybackAction({ key: 'Enter', code: 'Enter', keyCode: 13 }), null)
})
