import { useEffect, useRef, useState, type RefObject } from 'react'
import { shouldPaintVisualizer, visualizerFrameIntervalMs, visualizerPixelRatio } from './visualizer'
import { connectVisualizerAudio } from './visualizerAudio'

type KaraokeVisualizerProps = {
  enabled: boolean
  media: RefObject<HTMLVideoElement | null>
  prepare: boolean
}

const BARS = 32

/**
 * A deliberately low-detail spectrum for karaoke tracks without a background clip. It is kept
 * outside React's render loop: a television only paints a small canvas at 20 FPS while playing.
 */
export function KaraokeVisualizer({ enabled, media, prepare }: KaraokeVisualizerProps) {
  const canvas = useRef<HTMLCanvasElement>(null)
  const analyzer = useRef<AnalyserNode | null>(null)
  const source = useRef<MediaElementAudioSourceNode | null>(null)
  const context = useRef<AudioContext | null>(null)
  const [reducedMotion, setReducedMotion] = useState(false)
  const [available, setAvailable] = useState(true)
  const [audioReady, setAudioReady] = useState(false)
  const closeTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)

  useEffect(() => {
    if (!window.matchMedia) return
    const query = window.matchMedia('(prefers-reduced-motion: reduce)')
    const update = () => setReducedMotion(query.matches)
    update()
    if (query.addEventListener) {
      query.addEventListener('change', update)
      return () => query.removeEventListener('change', update)
    }
    query.addListener(update)
    return () => query.removeListener(update)
  }, [])

  useEffect(() => {
    if (closeTimer.current) clearTimeout(closeTimer.current)
    return () => {
      // Strict Mode remounts effects once in development. Defer disposal so that pass retains
      // the one media source node, while a real unmount releases the browser audio resources.
      closeTimer.current = setTimeout(() => {
        source.current?.disconnect()
        analyzer.current?.disconnect()
        void context.current?.close()
        source.current = null
        analyzer.current = null
        context.current = null
      }, 0)
    }
  }, [])

  useEffect(() => {
    let disposed = false
    const element = media.current
    function connect() {
      const audioContext = context.current
      if (disposed || !audioContext || audioContext.state !== 'running' || !element) return
      const existing = source.current && analyzer.current ? { source: source.current, analyzer: analyzer.current } : null
      const connection = connectVisualizerAudio(audioContext, element, existing)
      if (!connection) return
      source.current = connection.source
      analyzer.current = connection.analyzer
      setAvailable(true)
      setAudioReady(true)
    }

    function activate() {
      if (((!prepare || reducedMotion) && !context.current) || !element) return
      try {
        const AudioContextConstructor = window.AudioContext
          ?? (window as Window & { webkitAudioContext?: typeof AudioContext }).webkitAudioContext
        if (!AudioContextConstructor) { setAvailable(false); return }
        const audioContext = context.current ?? new AudioContextConstructor()
        context.current = audioContext
        // Do not reroute audible media into a suspended context while autoplay waits for a gesture.
        void audioContext.resume().then(connect).catch(() => {
          if (!disposed) setAvailable(false)
        })
      } catch {
        // Some older TV browsers cannot expose an HTML media element to Web Audio.
        setAvailable(false)
      }
    }

    document.addEventListener('pointerdown', activate)
    document.addEventListener('keydown', activate)
    element?.addEventListener('playing', activate)
    activate()
    return () => {
      disposed = true
      element?.removeEventListener('playing', activate)
      document.removeEventListener('pointerdown', activate)
      document.removeEventListener('keydown', activate)
    }
  }, [media, prepare, reducedMotion])

  useEffect(() => {
    const element = media.current
    const drawing = canvas.current
    const audioAnalyzer = analyzer.current
    if (!enabled || reducedMotion || !available || !audioReady || !element || !drawing || !audioAnalyzer) return

    const drawingContext = drawing.getContext('2d')
    if (!drawingContext) { queueMicrotask(() => setAvailable(false)); return }

    const mediaElement = element
    const canvasElement = drawing
    const canvasContext = drawingContext
    const frequencyAnalyzer = audioAnalyzer

    const values = new Uint8Array(frequencyAnalyzer.frequencyBinCount)
    let timer: ReturnType<typeof setTimeout> | undefined
    let stopped = false

    function resize() {
      const bounds = canvasElement.getBoundingClientRect()
      const ratio = visualizerPixelRatio(bounds.width, bounds.height, window.devicePixelRatio)
      canvasElement.width = Math.max(1, Math.round(bounds.width * ratio))
      canvasElement.height = Math.max(1, Math.round(bounds.height * ratio))
      canvasContext.setTransform(ratio, 0, 0, ratio, 0, 0)
    }

    function paint() {
      const { width, height } = canvasElement.getBoundingClientRect()
      canvasContext.clearRect(0, 0, width, height)
      const background = canvasContext.createLinearGradient(0, 0, width, height)
      background.addColorStop(0, '#0e0a20')
      background.addColorStop(0.55, '#170d2c')
      background.addColorStop(1, '#240f34')
      canvasContext.fillStyle = background
      canvasContext.fillRect(0, 0, width, height)

      frequencyAnalyzer.getByteFrequencyData(values)
      const gap = Math.max(2, width / 180)
      const barWidth = Math.max(2, (width - gap * (BARS - 1)) / BARS)
      for (let index = 0; index < BARS; index++) {
        const value = values[Math.min(values.length - 1, index + 1)] / 255
        const barHeight = Math.max(3, value * height * 0.7)
        const left = index * (barWidth + gap)
        const top = height - barHeight - height * 0.1
        const color = canvasContext.createLinearGradient(left, top, left, top + barHeight)
        color.addColorStop(0, '#4a2a87')
        color.addColorStop(0.4, '#33205e')
        color.addColorStop(1, '#1c102e')
        canvasContext.shadowBlur = 4
        canvasContext.shadowColor = 'rgba(137, 88, 255, 0.22)'
        canvasContext.fillStyle = color
        canvasContext.fillRect(left, top, barWidth, barHeight)
      }
      canvasContext.shadowBlur = 0
    }

    function schedule() {
      timer = undefined
      if (stopped || document.hidden || mediaElement.paused) return
      paint()
      timer = setTimeout(schedule, visualizerFrameIntervalMs)
    }

    function update() {
      if (!shouldPaintVisualizer(!mediaElement.paused, document.hidden)) {
        if (timer) clearTimeout(timer)
        timer = undefined
        paint()
      } else if (!timer) schedule()
    }

    resize()
    paint()
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(resize)
    observer?.observe(canvasElement)
    window.addEventListener('resize', resize)
    mediaElement.addEventListener('play', update)
    mediaElement.addEventListener('pause', update)
    document.addEventListener('visibilitychange', update)
    update()

    return () => {
      stopped = true
      if (timer) clearTimeout(timer)
      observer?.disconnect()
      window.removeEventListener('resize', resize)
      mediaElement.removeEventListener('play', update)
      mediaElement.removeEventListener('pause', update)
      document.removeEventListener('visibilitychange', update)
    }
  }, [audioReady, available, enabled, media, reducedMotion])

  if (!enabled) return null

  return <div className="karaoke-visualizer" aria-hidden="true">
    {!reducedMotion && available && audioReady && <canvas ref={canvas} />}
    {(reducedMotion || !available || !audioReady) && <div className="karaoke-visualizer-fallback" />}
  </div>
}
