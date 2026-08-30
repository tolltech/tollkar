import type { PlaybackAnchor } from '../player/timeline'

export type QueueItem = { id: string; songId: string; title: string; artist: string | null; position: number }
export type QueueSnapshot = { version: number; items: QueueItem[]; currentItemId?: string | null; playback?: PlaybackAnchor | null }

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
