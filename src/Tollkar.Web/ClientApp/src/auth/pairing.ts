export type PairingRequest = {
  userCode: string
  deviceCode: string
  imageUrl: string
  expiresAt: string
}

export type PairingOutcome = 'pending' | 'approved' | 'expired' | 'unknown' | 'forbidden' | 'conflict'
export type PairingStep = { action: 'renew' | 'poll'; delayMs: number }

export const pollIntervalMs = 3000

/**
 * Drives the display loop: poll while the shown code can still be confirmed,
 * and issue a new one as soon as it expires so the screen never shows a dead QR.
 */
export function nextPairingStep(request: { expiresAt: string } | undefined, now: number,
  intervalMs = pollIntervalMs): PairingStep {
  if (!request) return { action: 'renew', delayMs: 0 }
  const remaining = new Date(request.expiresAt).getTime() - now
  if (!Number.isFinite(remaining) || remaining <= 0) return { action: 'renew', delayMs: 0 }
  return { action: 'poll', delayMs: Math.min(intervalMs, remaining) }
}

/** Codes are compared by eye across two screens, so they are shown in short groups. */
export function formatUserCode(userCode: string) {
  return (userCode.match(/.{1,4}/g) ?? [userCode]).join(' ')
}
