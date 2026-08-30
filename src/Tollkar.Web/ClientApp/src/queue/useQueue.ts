import { useEffect, useState } from 'react'
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { SnapshotState, type QueueSnapshot } from './snapshot'

export function useQueue(userId: string) {
  const [snapshot, setSnapshot] = useState<QueueSnapshot | null>(null)
  const [connected, setConnected] = useState(false)

  useEffect(() => {
    let disposed = false
    let retry: ReturnType<typeof setTimeout> | undefined
    let restoring: number | undefined
    let refreshRequested = false
    const state = new SnapshotState()
    let generation = state.reset()
    const connection = new HubConnectionBuilder()
      .withUrl('/api/karaoke')
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: () => 2000 })
      .configureLogging(LogLevel.Warning)
      .build()

    function apply(value: QueueSnapshot, sourceGeneration: number) {
      if (!disposed && state.accept(value, sourceGeneration)) setSnapshot({ ...value, playback: value.playback ? { ...value.playback, receivedAt: performance.now() } : null })
    }

    async function restore() {
      if (disposed || restoring === generation || connection.state !== HubConnectionState.Connected) return
      clearTimeout(retry)
      refreshRequested = false
      const sourceGeneration = generation
      restoring = sourceGeneration
      try {
        const value = await connection.invoke<QueueSnapshot>('GetSnapshot')
        if (disposed || sourceGeneration !== generation) return
        apply(value, sourceGeneration)
        setConnected(true)
      } catch {
        if (!disposed && sourceGeneration === generation) retry = setTimeout(restore, 2000)
      } finally {
        if (restoring === sourceGeneration) restoring = undefined
        if (refreshRequested && sourceGeneration === generation) void restore()
      }
    }

    async function start() {
      if (disposed) return
      try {
        await connection.start()
        if (disposed) { await connection.stop(); return }
        await restore()
      } catch {
        if (!disposed) retry = setTimeout(start, 2000)
      }
    }

    connection.on('QueueChanged', (value: QueueSnapshot) => apply(value, generation))
    connection.on('QueueInvalidated', () => {
      refreshRequested = true
      void restore()
    })
    connection.onreconnecting(() => {
      if (disposed) return
      clearTimeout(retry)
      generation = state.reset()
      setConnected(false)
    })
    connection.onreconnected(restore)
    connection.onclose(() => {
      if (!disposed) {
        generation = state.reset()
        setConnected(false)
        retry = setTimeout(start, 2000)
      }
    })
    void start()
    // Recover even if an individual publication failed while the transport stayed connected.
    const refresh = setInterval(() => { void restore() }, 30000)
    return () => {
      disposed = true
      clearTimeout(retry)
      clearInterval(refresh)
      void connection.stop()
    }
  }, [userId])

  return { snapshot, connected }
}
