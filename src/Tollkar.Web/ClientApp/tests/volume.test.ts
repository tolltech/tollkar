import assert from 'node:assert/strict'
import test from 'node:test'
import {
  applyVolumeSettings,
  changeVolumeSettings,
  defaultVolumeSettings,
  parseVolumeSettings,
  toggleVolumeMute,
} from '../src/player/volume.ts'

test('missing or invalid volume settings fall back to full volume', () => {
  assert.deepEqual(parseVolumeSettings(null), defaultVolumeSettings)
  assert.deepEqual(parseVolumeSettings('not json'), defaultVolumeSettings)
  assert.deepEqual(parseVolumeSettings('{"muted":false}'), defaultVolumeSettings)
})

test('stored volume settings are constrained to the supported range', () => {
  assert.deepEqual(parseVolumeSettings('{"muted":true,"volume":-20}'), { muted: true, volume: 0 })
  assert.deepEqual(parseVolumeSettings('{"muted":false,"volume":42.5}'), { muted: false, volume: 43 })
  assert.deepEqual(parseVolumeSettings('{"muted":false,"volume":200}'), { muted: false, volume: 100 })
})

test('volume changes mute at zero and restore sound above zero', () => {
  assert.deepEqual(changeVolumeSettings(0), { muted: true, volume: 0 })
  assert.deepEqual(changeVolumeSettings(37), { muted: false, volume: 37 })
})

test('mute toggle preserves an audible volume and restores a silent slider to full volume', () => {
  assert.deepEqual(toggleVolumeMute({ muted: false, volume: 37 }), { muted: true, volume: 37 })
  assert.deepEqual(toggleVolumeMute({ muted: true, volume: 37 }), { muted: false, volume: 37 })
  assert.deepEqual(toggleVolumeMute({ muted: true, volume: 0 }), { muted: false, volume: 100 })
})

test('volume settings respect both saved mute and browser audio activation', () => {
  const media = { muted: false, volume: 1 }
  applyVolumeSettings(media, true, { muted: true, volume: 37 })
  assert.deepEqual(media, { muted: true, volume: 0.37 })

  applyVolumeSettings(media, false, { muted: false, volume: 37 })
  assert.deepEqual(media, { muted: true, volume: 0.37 })

  applyVolumeSettings(media, true, { muted: false, volume: 37 })
  assert.deepEqual(media, { muted: false, volume: 0.37 })
})
