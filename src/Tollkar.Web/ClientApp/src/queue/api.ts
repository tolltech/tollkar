import { mutate } from '../api/request'

export type Song = { id: string; title: string; artist: string | null; folder: string | null }

export async function searchSongs(text: string, signal: AbortSignal): Promise<Song[]> {
  const query = new URLSearchParams({ text: text.trim(), limit: '100' })
  const response = await fetch(`/api/library/search?${query}`, { credentials: 'same-origin', signal })
  if (!response.ok) throw new Error(response.status === 401
    ? 'Сессия истекла. Войдите снова.' : 'Не удалось загрузить песни. Повторите поиск.')
  return response.json()
}

export const addSong = (songId: string) => mutate('/api/queue', 'POST', { songId })
export const clearQueue = () => mutate('/api/queue', 'DELETE')
export const removeItem = (id: string) => mutate(`/api/queue/${encodeURIComponent(id)}`, 'DELETE')
export const moveItem = (id: string, offset: -1 | 1) => mutate(`/api/queue/${encodeURIComponent(id)}/move`, 'POST', { offset })
export const playItem = (id: string) => mutate(`/api/queue/${encodeURIComponent(id)}/play`, 'POST')
