export type VolumeSettings = {
  muted: boolean
  volume: number
}

export const defaultVolumeSettings: VolumeSettings = { muted: false, volume: 100 }

type VolumeMedia = {
  muted: boolean
  volume: number
}

function normalizeVolume(value: number) {
  return Math.round(Math.min(100, Math.max(0, value)))
}

export function parseVolumeSettings(value: string | null): VolumeSettings {
  if (!value) return defaultVolumeSettings

  try {
    const parsed: unknown = JSON.parse(value)
    if (!parsed || typeof parsed !== 'object') return defaultVolumeSettings
    const settings = parsed as Partial<VolumeSettings>
    if (typeof settings.muted !== 'boolean' || typeof settings.volume !== 'number' || !Number.isFinite(settings.volume))
      return defaultVolumeSettings

    return { muted: settings.muted, volume: normalizeVolume(settings.volume) }
  } catch {
    return defaultVolumeSettings
  }
}

export function isVolumeMuted(settings: VolumeSettings) {
  return settings.muted || settings.volume === 0
}

/** What the viewer actually hears: the browser has to allow sound before the settings matter. */
export function isSoundSilent(soundEnabled: boolean, settings: VolumeSettings) {
  return !soundEnabled || isVolumeMuted(settings)
}

/**
 * Pressing the sound button on a player the browser has not unblocked yet only unblocks it:
 * muting the settings then would silence the player the press was meant to make audible.
 */
export function pressVolumeButton(soundEnabled: boolean, settings: VolumeSettings): VolumeSettings {
  return !soundEnabled && !isVolumeMuted(settings) ? settings : toggleVolumeMute(settings)
}

/** A display is watched from across the room, so it always starts audible at its saved level. */
export function displayVolumeSettings(settings: VolumeSettings): VolumeSettings {
  return unmuteVolume(settings)
}

function unmuteVolume(settings: VolumeSettings): VolumeSettings {
  return { muted: false, volume: settings.volume || defaultVolumeSettings.volume }
}

export function changeVolumeSettings(value: number): VolumeSettings {
  const volume = normalizeVolume(value)
  return { muted: volume === 0, volume }
}

export function toggleVolumeMute(settings: VolumeSettings): VolumeSettings {
  return isVolumeMuted(settings) ? unmuteVolume(settings) : { ...settings, muted: true }
}

export function applyVolumeSettings(media: VolumeMedia, soundEnabled: boolean, settings: VolumeSettings) {
  media.volume = settings.volume / 100
  media.muted = isSoundSilent(soundEnabled, settings)
}
