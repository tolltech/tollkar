import type { PlaybackAnchor } from '../player/timeline'

export type QueueItem = { id: string; songId: string; title: string; artist: string | null; capabilities: number; position: number; providerId?: string | null }
export type QueueSnapshot = { version: number; items: QueueItem[]; currentItemId?: string | null; playback?: PlaybackAnchor | null }

/** Mirrors SongCapabilities.SyncedLyrics: the song is sung over its own timed text. */
const SYNCED_LYRICS = 1 << 2

// CDG imports have video lyrics but no SyncedLyrics bit; their KFN script still selects the backdrop.
export const isKaraoke = (item: QueueItem | undefined) => item?.providerId === 'kfn'
  || ((item?.capabilities ?? 0) & SYNCED_LYRICS) !== 0

export class SnapshotState {
  private generation = 0
  private version = -1

  reset() {
    this.version = -1
    return ++this.generation
  }

  accept(snapshot: QueueSnapshot, generation: number) {
    if (generation !== this.generation || snapshot.version <= this.version) return false
    this.version = snapshot.version
    return true
  }
}
