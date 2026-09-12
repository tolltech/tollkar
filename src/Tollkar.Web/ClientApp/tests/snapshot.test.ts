import { test } from 'node:test'
import assert from 'node:assert/strict'
import { isKaraoke, SnapshotState } from '../src/queue/snapshot.ts'

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

test('KFN without separate lyrics still loads its karaoke backdrop', () => {
  const item = { id: '1', songId: '2', title: 'CDG', artist: null, capabilities: 3, position: 0, providerId: 'kfn' }
  assert.equal(isKaraoke(item), true)
  assert.equal(isKaraoke({ ...item, providerId: 'video' }), false)
  assert.equal(isKaraoke({ ...item, capabilities: 1 }), true)
  assert.equal(isKaraoke(undefined), false)
})
