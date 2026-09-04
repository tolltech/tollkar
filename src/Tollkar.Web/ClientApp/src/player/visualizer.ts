export const visualizerFrameIntervalMs = 50
const maxCanvasWidth = 1280
const maxCanvasHeight = 720

export function shouldPaintVisualizer(isPlaying: boolean, documentHidden: boolean) {
  return isPlaying && !documentHidden
}

/** Keeps a fullscreen 4K TV from turning a decorative spectrum into a 4K render loop. */
export function visualizerPixelRatio(width: number, height: number, devicePixelRatio: number) {
  return Math.min(devicePixelRatio || 1, maxCanvasWidth / width, maxCanvasHeight / height)
}
