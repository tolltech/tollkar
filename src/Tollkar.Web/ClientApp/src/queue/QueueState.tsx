import { useOutletContext } from 'react-router-dom'
import type { useQueue } from './useQueue'

export function QueueState() {
  const { snapshot, connected } = useOutletContext<ReturnType<typeof useQueue>>()
  return <div aria-live="polite">
    {!connected && <p role="status">Восстанавливаем соединение и состояние очереди…</p>}
    {snapshot && (snapshot.items.length === 0
      ? <p>Очередь пока пуста.</p>
      : <ol>{snapshot.items.map(item => <li key={item.id}>
        {item.artist ? `${item.artist} — ` : ''}{item.title}
      </li>)}</ol>)}
  </div>
}
