import { useOutletContext } from 'react-router-dom'
import type { useQueue } from './useQueue'
import { SongDetails } from './SongDetails'
import './queue.css'

export function QueueState() {
  const { snapshot, connected } = useOutletContext<ReturnType<typeof useQueue>>()
  const current = snapshot?.items.find(item => item.id === snapshot.currentItemId)
  return <div className="queue-state" aria-live="polite">
    <p className={`connection-status${connected ? ' is-connected' : ''}`} role="status">
      {connected ? 'Очередь синхронизирована' : 'Восстанавливаем соединение. Управление временно недоступно…'}
    </p>
    <section className="current-song" aria-label="Текущая песня">
      <p className="eyebrow">Текущая песня</p>
      {current ? <SongDetails song={current} /> : <p>{snapshot ? 'Песня не выбрана. Нажмите «Играть сейчас» в очереди.' : 'Загружаем очередь…'}</p>}
      <p className="queue-hint">Выбор синхронизируется с плеером. Воспроизведение видео появится на следующем этапе.</p>
    </section>
  </div>
}
