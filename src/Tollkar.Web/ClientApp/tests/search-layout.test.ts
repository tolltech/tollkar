import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

test('search results keep a stable height while search requests refresh', async () => {
  const styles = await readFile(new URL('../src/queue/queue.css', import.meta.url), 'utf8')
  const searchResultsRule = styles.match(/\.search-results\s*\{([^}]*)\}/)?.[1]

  assert.ok(searchResultsRule, 'Expected a .search-results CSS rule.')
  assert.match(searchResultsRule, /(?:^|;)\s*height:\s*60vh\s*;/)
  assert.doesNotMatch(searchResultsRule, /(?:^|;)\s*max-height:/)
})
