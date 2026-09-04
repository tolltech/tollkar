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

export function changeVolumeSettings(value: number): VolumeSettings {
  const volume = normalizeVolume(value)
  return { muted: volume === 0, volume }
}

export function toggleVolumeMute(settings: VolumeSettings): VolumeSettings {
  return isVolumeMuted(settings)
    ? { muted: false, volume: settings.volume || defaultVolumeSettings.volume }
    : { ...settings, muted: true }
}

export function applyVolumeSettings(media: VolumeMedia, soundEnabled: boolean, settings: VolumeSettings) {
  media.volume = settings.volume / 100
  media.muted = !soundEnabled || isVolumeMuted(settings)
}
