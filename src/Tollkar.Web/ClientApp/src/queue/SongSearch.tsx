import { useEffect, useState } from 'react'
import { searchSongs, type Song } from './api'
import { SongDetails } from './SongDetails'

type Props = { disabled: boolean; onAdd: (song: Song) => void }

export function SongSearch({ disabled, onAdd }: Props) {
  const [text, setText] = useState('')
  const [songs, setSongs] = useState<Song[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    const timer = setTimeout(async () => {
      try {
        const results = await searchSongs(text, controller.signal)
        if (!controller.signal.aborted) setSongs(results)
      } catch (reason) {
        if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : 'Не удалось загрузить песни.')
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }, 300)
    return () => { clearTimeout(timer); controller.abort() }
  }, [text, attempt])

  function resetSearch() {
    setLoading(true)
    setError('')
    setSongs([])
  }

  return <section className="queue-panel" aria-labelledby="search-title">
    <h2 id="search-title">Найти песню</h2>
    <label className="search-label" htmlFor="song-search">Название или исполнитель</label>
    <input id="song-search" className="song-search" type="search" placeholder="Начните вводить название…"
      value={text} onChange={event => { resetSearch(); setText(event.target.value) }} />
    <p className="queue-hint">Поиск по началу названия или имени исполнителя.</p>
    <div className="search-results" role="region" tabIndex={0} aria-label="Результаты поиска" aria-busy={loading}>
      <div role="status">
        {loading && <p>Ищем песни…</p>}
        {!loading && !error && <p className="queue-hint">{songs.length === 100 ? 'Первые 100 песен — уточните поиск.' : `Найдено: ${songs.length}`}</p>}
      </div>
      {error && <div role="alert"><p>{error}</p><button className="secondary-button" onClick={() => { resetSearch(); setAttempt(value => value + 1) }}>Повторить поиск</button></div>}
      {!loading && !error && songs.length === 0 && <p className="queue-hint">{text.trim() ? 'Ничего не найдено. Попробуйте другое название.' : 'В библиотеке пока нет песен.'}</p>}
      <ul className="song-list">
        {songs.map(song => <li className="song-row" key={song.id}>
          <SongDetails song={song} />
          <button className="secondary-button" disabled={disabled} aria-label={`Добавить в очередь: ${song.title}`} onClick={() => onAdd(song)}>+ Добавить</button>
        </li>)}
      </ul>
    </div>
  </section>
}
