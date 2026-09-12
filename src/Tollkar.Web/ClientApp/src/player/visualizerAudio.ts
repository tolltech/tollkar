export type VisualizerAudio = { source: MediaElementAudioSourceNode; analyzer: AnalyserNode }

export function connectVisualizerAudio(context: AudioContext, media: HTMLMediaElement, existing: VisualizerAudio | null) {
  if (existing) return existing
  // Routing media through a suspended context would silence otherwise playable audio.
  if (context.state !== 'running') return null
  const analyzer = context.createAnalyser()
  analyzer.fftSize = 128
  analyzer.smoothingTimeConstant = 0.8
  analyzer.connect(context.destination)
  const source = context.createMediaElementSource(media)
  source.connect(analyzer)
  return { source, analyzer }
}
