import assert from 'node:assert/strict'
import test from 'node:test'
import { connectVisualizerAudio } from '../src/player/visualizerAudio.ts'

test('blocked Web Audio preserves native sound and connects only once after activation', () => {
  let sources = 0
  const analyzer = { connect() {}, fftSize: 0, smoothingTimeConstant: 0 }
  const source = { connect() {} }
  const context = {
    state: 'suspended',
    destination: {},
    createAnalyser: () => analyzer,
    createMediaElementSource() { sources++; return source },
  }
  const media = {} as HTMLMediaElement
  assert.equal(connectVisualizerAudio(context as unknown as AudioContext, media, null), null)
  assert.equal(sources, 0)
  context.state = 'running'
  const connection = connectVisualizerAudio(context as unknown as AudioContext, media, null)
  assert.ok(connection)
  assert.equal(sources, 1)
  assert.equal(connectVisualizerAudio(context as unknown as AudioContext, media, connection), connection)
  assert.equal(sources, 1)
})
