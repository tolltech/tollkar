import assert from 'node:assert/strict'
import test from 'node:test'
import { canAccessAdmin } from '../src/auth/authorization.ts'

test('admin-only UI is available only when the server marks the user as admin', () => {
  assert.equal(canAccessAdmin({ isAdmin: true }), true)
  assert.equal(canAccessAdmin({ isAdmin: false }), false)
})
