import { useEffect, useEffectEvent, useRef, useState } from 'react'
import { useOutletContext } from 'react-router-dom'
import { mutate } from '../api/request'
import { QueueState } from '../queue/QueueState'
import { isKaraoke } from '../queue/snapshot'
import type { useQueue } from '../queue/useQueue'
import { Lyrics } from './Lyrics'
import { useKaraokeScript } from './karaoke'
import { formatTime, playbackPosition } from './timeline'
import { synchronizeBackground, synchronizeMedia } from './media'
import './player.css'

export function PlayerPage() {
  const { snapshot, connected } = useOutletContext<ReturnType<typeof useQueue>>()
  const current = snapshot?.items.find(item => item.id === snapshot.currentItemId)
  const currentId = current?.id
  const playback = snapshot?.playback
  const karaoke = useKaraokeScript(isKaraoke(current) ? current?.songId : undefined)
  const backdrop = karaoke?.background
  const video = useRef<HTMLVideoElement>(null)
  const background = useRef<HTMLVideoElement>(null)
  const stage = useRef<HTMLDivElement>(null)
  const activated = useRef(false)
  const busy = useRef(false)
  const [pending, setPending] = useState(false)
  const [soundEnabled, setSoundEnabled] = useState(false)
  const [blocked, setBlocked] = useState(false)
  const [error, setError] = useState('')
  const [duration, setDuration] = useState(0)
  const [position, setPosition] = useState(0)
  const [seek, setSeek] = useState<number | null>(null)
  const disabled = !connected || !current || !playback || pending

  const advance = useEffectEvent(() => { void command('ended') })

  useEffect(() => {
    const media = video.current
    return () => media?.pause()
  }, [])

  // Keep one media element across songs: browser audio permission belongs to the element.
  useEffect(() => {
    const media = video.current
    const backdropMedia = background.current
    if (!media) return
    let disposed = false
    let starting = false
    setSeek(null)
    setBlocked(false)

    function synchronize() {
      if (!media || disposed) return
      if (!connected || !playback || !currentId) { media.pause(); backdropMedia?.pause(); return }
      synchronizeMedia(media, playback, performance.now(), {
        ended: advance,
        play() {
          if (starting) return
          starting = true
          void media.play().then(() => {
            if (!disposed) setBlocked(false)
          }).catch(reason => {
            if (!disposed && reason instanceof DOMException && reason.name === 'NotAllowedError') setBlocked(true)
          }).finally(() => { starting = false })
        },
      })
      // The backdrop is muted, so it may start even while the audio waits for a gesture.
      if (backdropMedia && backdrop) {
        synchronizeBackground(backdropMedia, playback, performance.now(), backdrop.loop,
          () => void backdropMedia.play().catch(() => {}))
      }
    }

    synchronize()
    media.addEventListener('loadedmetadata', synchronize)
    media.addEventListener('canplay', synchronize)
    backdropMedia?.addEventListener('loadedmetadata', synchronize)
    const timer = setInterval(synchronize, 1000)
    return () => {
      disposed = true
      clearInterval(timer)
      media.removeEventListener('loadedmetadata', synchronize)
      media.removeEventListener('canplay', synchronize)
      backdropMedia?.removeEventListener('loadedmetadata', synchronize)
    }
  }, [playback, currentId, connected, backdrop])

  function activateSound() {
    const media = video.current
    if (!media || activated.current) return
    activated.current = true
    media.muted = false
    setSoundEnabled(true)
    // Invoke play synchronously inside the user gesture, before any HTTP await.
    if (connected && playback?.isPlaying && current) {
      void media.play().then(() => setBlocked(false)).catch(() => {
        activated.current = false
        setSoundEnabled(false)
        setBlocked(true)
      })
    }
  }

  useEffect(() => {
    const activate = () => activateSound()
    document.addEventListener('pointerdown', activate)
    document.addEventListener('keydown', activate)
    return () => {
      document.removeEventListener('pointerdown', activate)
      document.removeEventListener('keydown', activate)
    }
  })

  async function command(action: string, positionSeconds = 0) {
    if (busy.current || !connected || !playback || !current) return
    busy.current = true
    setPending(true)
    setError('')
    try {
      await mutate('/api/queue/playback', 'POST', { action, revision: playback.revision, positionSeconds })
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Не удалось выполнить команду.')
    } finally {
      busy.current = false
      setPending(false)
    }
  }

  async function fullscreen() {
    try {
      if (document.fullscreenElement) await document.exitFullscreen()
      else if (stage.current?.requestFullscreen) await stage.current.requestFullscreen()
      else setError('Полноэкранный режим недоступен в этом браузере.')
    } catch { setError('Не удалось открыть полноэкранный режим.') }
  }

  function resumeLocally() {
    activateSound()
    const media = video.current
    if (media && playback?.isPlaying && connected) {
      void media.play().then(() => setBlocked(false)).catch(() => setBlocked(true))
    }
  }

  return <section className="page player-page" aria-labelledby="player-title">
    <h1 id="player-title">Плеер</h1>
    <div className={`web-player${karaoke ? ' web-player-karaoke' : ''}`} ref={stage}>
      {current && backdrop && <video ref={background} className="player-backdrop" muted playsInline
        preload="auto" tabIndex={-1} aria-hidden="true" loop={backdrop.loop}
        src={`/api/songs/${encodeURIComponent(current.songId)}/background`} />}
      <video ref={video} src={current ? `/api/songs/${encodeURIComponent(current.songId)}/media` : undefined}
        playsInline preload="metadata" muted={!soundEnabled} aria-label={current?.title ?? 'Караоке'}
        onEmptied={() => { setError(''); setDuration(0); setPosition(0) }}
        onLoadedMetadata={event => setDuration(Number.isFinite(event.currentTarget.duration) ? event.currentTarget.duration : 0)}
        onDurationChange={event => setDuration(Number.isFinite(event.currentTarget.duration) ? event.currentTarget.duration : 0)}
        onTimeUpdate={event => setPosition(event.currentTarget.currentTime)}
        onEnded={event => {
          if (!event.currentTarget.error && playback?.isPlaying && playbackPosition(playback, performance.now()) >= event.currentTarget.duration - 1)
            void command('ended')
        }}
        onError={() => {
          if (current) setError(karaoke
            ? 'Фонограмма недоступна или её формат не поддерживается браузером. Можно перейти к следующей песне.'
            : 'Видео недоступно или его формат не поддерживается браузером. Можно перейти к следующей песне.')
        }} />
      {karaoke && <Lyrics lines={karaoke.lines} media={video} />}
      {!current && <p className="player-empty">Выберите песню в очереди, чтобы начать.</p>}
      <div className="player-controls">
        <button className="primary-button" disabled={disabled} onClick={() => {
          if (!playback?.isPlaying) {
            activateSound()
            void video.current?.play().then(() => setBlocked(false)).catch(() => setBlocked(true))
          }
          void command(playback?.isPlaying ? 'pause' : 'play')
        }}>{playback?.isPlaying ? 'Пауза' : 'Играть'}</button>
        <button className="secondary-button" disabled={disabled} onClick={() => void command('next')}>Следующая</button>
        <button className="secondary-button" onClick={() => void fullscreen()}>Полный экран</button>
        <label className="player-seek">Позиция
          <input type="range" min="0" max={duration} step="0.1" value={seek ?? Math.min(position, duration)}
            disabled={disabled || duration === 0}
            aria-valuetext={formatTime(seek ?? position)}
            onChange={event => setSeek(Number(event.target.value))}
            onPointerUp={event => { void command('seek', Number(event.currentTarget.value)); setSeek(null) }}
            onKeyUp={event => {
              if (['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End', 'PageUp', 'PageDown'].includes(event.key)) {
                void command('seek', Number(event.currentTarget.value)); setSeek(null)
              }
            }} />
        </label>
        <span className="player-time">{formatTime(seek ?? position)} / {formatTime(duration)}</span>
      </div>
      {(!soundEnabled || blocked) && current && <button className="primary-button player-activation"
        disabled={!connected} onClick={resumeLocally}>{blocked ? 'Разрешить воспроизведение' : 'Включить звук'}</button>}
      {error && <p className="auth-error player-error" role="alert">{error}</p>}
    </div>
    <QueueState />
  </section>
}
