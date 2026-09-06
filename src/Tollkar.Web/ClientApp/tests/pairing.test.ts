import assert from 'node:assert/strict'
import test from 'node:test'
import { formatUserCode, nextPairingStep, pollIntervalMs } from '../src/auth/pairing.ts'

const now = Date.parse('2026-09-03T12:00:00Z')
const at = (seconds: number) => ({ expiresAt: new Date(now + seconds * 1000).toISOString() })

test('a display without a code asks for one immediately', () => {
  assert.deepEqual(nextPairingStep(undefined, now), { action: 'renew', delayMs: 0 })
})

test('a valid code is polled at the regular interval', () => {
  assert.deepEqual(nextPairingStep(at(300), now), { action: 'poll', delayMs: pollIntervalMs })
})

test('the last poll before expiration is not scheduled past it', () => {
  assert.deepEqual(nextPairingStep(at(1), now), { action: 'poll', delayMs: 1000 })
})

test('an expired or unreadable code is replaced instead of polled', () => {
  assert.deepEqual(nextPairingStep(at(0), now), { action: 'renew', delayMs: 0 })
  assert.deepEqual(nextPairingStep(at(-60), now), { action: 'renew', delayMs: 0 })
  assert.deepEqual(nextPairingStep({ expiresAt: 'never' }, now), { action: 'renew', delayMs: 0 })
})

test('the confirmation code is grouped for comparison across two screens', () => {
  assert.equal(formatUserCode('dec248565ab4d0fe'), 'dec2 4856 5ab4 d0fe')
  assert.equal(formatUserCode('abc'), 'abc')
  assert.equal(formatUserCode(''), '')
})
