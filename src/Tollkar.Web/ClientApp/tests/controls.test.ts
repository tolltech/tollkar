import assert from 'node:assert/strict'
import test from 'node:test'
import { controlsIdleDelayMs, holdsControls, idleRemainingMs, takesControlsFocus } from '../src/player/controls.ts'

test('a remote key wakes the hidden panel and takes its focus', () => {
  assert.equal(takesControlsFocus('key', { visible: false, holdsFocus: false }), true)
})

test('a remote key claims the open panel only while the focus is somewhere else', () => {
  assert.equal(takesControlsFocus('key', { visible: true, holdsFocus: false }), true)
  assert.equal(takesControlsFocus('key', { visible: true, holdsFocus: true }), false)
})

test('a pointer never takes the focus away from the viewer', () => {
  assert.equal(takesControlsFocus('pointer', { visible: false, holdsFocus: false }), false)
  assert.equal(takesControlsFocus('pointer', { visible: true, holdsFocus: true }), false)
})

test('a hovered panel and a dragged seek slider hold the controls open', () => {
  assert.equal(holdsControls({ hovered: false, seeking: false }), false)
  assert.equal(holdsControls({ hovered: true, seeking: false }), true)
  assert.equal(holdsControls({ hovered: false, seeking: true }), true)
})

test('the idle delay counts from the last activity and never runs negative', () => {
  assert.equal(idleRemainingMs(1000, 1000), controlsIdleDelayMs)
  assert.equal(idleRemainingMs(1000, 2000), controlsIdleDelayMs - 1000)
  assert.equal(idleRemainingMs(1000, 1000 + controlsIdleDelayMs), 0)
  assert.equal(idleRemainingMs(1000, 100000), 0)
})

test('activity reported ahead of the clock still waits the full delay', () => {
  assert.equal(idleRemainingMs(5000, 1000), controlsIdleDelayMs)
})
