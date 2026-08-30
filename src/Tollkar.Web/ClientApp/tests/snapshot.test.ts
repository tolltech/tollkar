import { test } from 'node:test'
import assert from 'node:assert/strict'
import { SnapshotState } from '../src/queue/snapshot.ts'

test('a delayed snapshot cannot overwrite a newer event', () => {
  const state = new SnapshotState()
  const generation = state.reset()
  assert.equal(state.accept({ version: 3, items: [] }, generation), true)
  assert.equal(state.accept({ version: 2, items: [] }, generation), false)
  assert.equal(state.accept({ version: 3, items: [] }, generation), false)
  assert.equal(state.accept({ version: 4, items: [] }, generation), true)
})

test('reconnect accepts a restarted server and rejects responses from the old connection', () => {
  const state = new SnapshotState()
  const old = state.reset()
  assert.equal(state.accept({ version: 100, items: [] }, old), true)
  const current = state.reset()
  assert.equal(state.accept({ version: 101, items: [] }, old), false)
  assert.equal(state.accept({ version: 0, items: [] }, current), true)
})
