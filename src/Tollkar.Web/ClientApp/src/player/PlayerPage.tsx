import { useEffect, useEffectEvent, useRef, useState } from 'react'
import { useOutletContext } from 'react-router-dom'
import { mutate } from '../api/request'
import { QueueState } from '../queue/QueueState'
import { isKaraoke } from '../queue/snapshot'
import type { useQueue } from '../queue/useQueue'
import { Lyrics } from './Lyrics'
import { KaraokeVisualizer } from './KaraokeVisualizer'
import { useKaraokeScript } from './karaoke'
import { formatTime, playbackPosition } from './timeline'
import { synchronizeBackground, synchronizeMedia } from './media'
import './player.css'

type PlayerIconName = 'fullscreen' | 'next' | 'pause' | 'play' | 'volume'

function PlayerIcon({ name }: { name: PlayerIconName }) {
  const paths = {
    fullscreen: <><path d="M4 9V4h5" /><path d="M15 4h5v5" /><path d="M20 15v5h-5" /><path d="M9 20H4v-5" /></>,
    next: <><path d="m4 5 10 7-10 7V5Z" fill="currentColor" stroke="none" /><path d="M20 5v14" /></>,
    pause: <><path d="M7 5v14" /><path d="M17 5v14" /></>,
    play: <path d="m6 4 13 8-13 8V4Z" fill="currentColor" stroke="none" />,
    volume: <><path d="M4 10v4h4l5 4V6l-5 4H4Z" fill="currentColor" stroke="none" /><path d="M17 9a4 4 0 0 1 0 6" /><path d="M19.5 6.5a8 8 0 0 1 0 11" /></>,
  }

  return <svg className="player-icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
    {paths[name]}
  </svg>
}

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
      <KaraokeVisualizer enabled={Boolean(karaoke && !backdrop)} media={video} prepare={isKaraoke(current)} />
      {karaoke && <Lyrics lines={karaoke.lines} media={video} />}
      {!current && <p className="player-empty">Выберите песню в очереди, чтобы начать.</p>}
      <div className="player-controls">
        <button type="button" className="primary-button player-icon-button" aria-label={playback?.isPlaying ? 'Пауза' : 'Играть'}
          title={playback?.isPlaying ? 'Пауза' : 'Играть'} disabled={disabled} onClick={() => {
          if (!playback?.isPlaying) {
            activateSound()
            void video.current?.play().then(() => setBlocked(false)).catch(() => setBlocked(true))
          }
          void command(playback?.isPlaying ? 'pause' : 'play')
        }}>{playback?.isPlaying ? <PlayerIcon name="pause" /> : <PlayerIcon name="play" />}</button>
        <button type="button" className="secondary-button player-icon-button" aria-label="Следующая песня" title="Следующая песня"
          disabled={disabled} onClick={() => void command('next')}><PlayerIcon name="next" /></button>
        <button type="button" className="secondary-button player-icon-button" aria-label="Полный экран" title="Полный экран"
          onClick={() => void fullscreen()}><PlayerIcon name="fullscreen" /></button>
        <label className="player-seek" aria-label="Позиция воспроизведения">
          <input type="range" min="0" max={duration} step="0.1" value={seek ?? Math.min(position, duration)}
            disabled={disabled || duration === 0}
            aria-label="Позиция воспроизведения"
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
        type="button" aria-label={blocked ? 'Разрешить воспроизведение' : 'Включить звук'}
        title={blocked ? 'Разрешить воспроизведение' : 'Включить звук'} disabled={!connected} onClick={resumeLocally}>
        <PlayerIcon name={blocked ? 'play' : 'volume'} />
      </button>}
      {error && <p className="auth-error player-error" role="alert">{error}</p>}
    </div>
    <QueueState />
  </section>
}
