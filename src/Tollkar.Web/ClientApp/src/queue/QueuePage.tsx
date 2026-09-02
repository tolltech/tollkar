import { useRef, useState } from 'react'
import { NavLink, useOutletContext } from 'react-router-dom'
import { addSong, clearQueue, moveItem, playItem, removeItem } from './api'
import { QueueState } from './QueueState'
import { SongDetails } from './SongDetails'
import { SongSearch } from './SongSearch'
import type { useQueue } from './useQueue'
import './queue.css'

export function QueuePage() {
  const { snapshot, connected } = useOutletContext<ReturnType<typeof useQueue>>()
  const busy = useRef(false)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const disabled = pending || !connected

  async function execute(action: () => Promise<void>, success: string) {
    if (busy.current || !connected) return
    busy.current = true
    setPending(true)
    setError('')
    setNotice('Выполняем команду…')
    try {
      await action()
      setNotice(success)
    } catch (reason) {
      setNotice('')
      setError(reason instanceof Error ? reason.message : 'Не удалось выполнить команду.')
    } finally {
      busy.current = false
      setPending(false)
    }
  }

  return <section className="page queue-page" aria-labelledby="queue-title">
    <div className="page-heading">
      <div>
        <p className="eyebrow">Пульт управления</p>
        <h1 id="queue-title">Очередь караоке</h1>
        <p className="page-description">Найдите любимую песню и добавьте её в очередь. Изменения видны на всех ваших устройствах.</p>
      </div>
      <NavLink className="secondary-button" to="/player">Открыть плеер</NavLink>
    </div>
    <QueueState />
    <div className="queue-feedback" role="status">{notice}</div>
    {error && <p className="auth-error" role="alert">{error}</p>}
    <div className="queue-columns">
      <SongSearch disabled={disabled} onAdd={song => void execute(() => addSong(song.id), `Добавлено: ${song.title}`)} />
      <section className="queue-panel" aria-labelledby="up-next-title" aria-busy={pending}>
        <div className="queue-panel-heading">
          <h2 id="up-next-title">В очереди <span className="queue-count">{snapshot?.items.length ?? '…'}</span></h2>
          <button className="secondary-button clear-queue-button" disabled={disabled || !snapshot?.items.length}
            onClick={() => void execute(clearQueue, snapshot?.currentItemId
              ? 'Очередь очищена. Текущая песня доиграет.'
              : 'Очередь очищена.')}>Очистить очередь</button>
        </div>
        {snapshot?.items.length === 0 && <div className="queue-empty"><span aria-hidden="true">♫</span><h3>Споём что-нибудь?</h3><p>Добавьте первую песню из поиска.</p></div>}
        <ol className="song-list queue-list">
          {snapshot?.items.map((item, index) => <li key={item.id} className={`song-row queue-row${snapshot.currentItemId === item.id ? ' is-current' : ''}`}>
            <span className="queue-position" aria-hidden="true">{index + 1}</span>
            <SongDetails song={item} />
            {snapshot.currentItemId === item.id && <span className="current-badge">Текущая</span>}
            <div className="queue-actions" role="group" aria-label={`Управление: ${item.title}, позиция ${index + 1}`}>
              <button className="primary-button" disabled={disabled || snapshot.currentItemId === item.id} onClick={() => void execute(() => playItem(item.id), `Выбрана текущая песня: ${item.title}`)}>Играть сейчас</button>
              <button className="secondary-button icon-button" aria-label={`Выше: ${item.title}`} disabled={disabled || index === 0} onClick={() => void execute(() => moveItem(item.id, -1), 'Песня перемещена выше.')}>↑</button>
              <button className="secondary-button icon-button" aria-label={`Ниже: ${item.title}`} disabled={disabled || index === snapshot.items.length - 1} onClick={() => void execute(() => moveItem(item.id, 1), 'Песня перемещена ниже.')}>↓</button>
              <button className="secondary-button remove-button" aria-label={`Удалить: ${item.title}`} disabled={disabled} onClick={() => void execute(() => removeItem(item.id), `Удалено: ${item.title}`)}>Удалить</button>
            </div>
          </li>)}
        </ol>
      </section>
    </div>
  </section>
}
