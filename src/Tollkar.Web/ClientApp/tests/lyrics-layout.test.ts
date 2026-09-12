import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import ts from 'typescript'

// Node's TypeScript loader cannot load TSX; compile this component without a browser test runtime.
async function loadLyrics() {
  const url = new URL('../src/player/Lyrics.tsx', import.meta.url)
  const source = (await readFile(url, 'utf8'))
    .replace("from 'react'", `from '${import.meta.resolve('react')}'`)
    .replace("from './karaoke'", `from '${new URL('../src/player/karaoke.ts', import.meta.url).href}'`)
  const { outputText } = ts.transpileModule(source, {
    compilerOptions: { jsx: ts.JsxEmit.ReactJSX, module: ts.ModuleKind.ESNext },
  })
  const compiled = outputText.replace('"react/jsx-runtime"', JSON.stringify(import.meta.resolve('react/jsx-runtime')))
  return import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`)
}

test('karaoke with text embedded in video keeps its stage without lyric lines', async () => {
  const { Lyrics } = await loadLyrics()
  const markup = renderToStaticMarkup(createElement(Lyrics, { lines: [], media: { current: null } }))
  assert.match(markup, /class="player-lyrics"/)
  assert.doesNotMatch(markup, /<p/)
})
