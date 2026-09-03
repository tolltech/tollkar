import { mutate } from '../api/request'

export type AdminSong = {
  id: string
  title: string
  artist: string | null
  duration: string | null
  capabilities: number
  folder: string | null
}

export type AdminSongCatalog = {
  items: AdminSong[]
  totalCount: number
  matchedCount: number
}

export async function loadAdminSongs(text: string, signal: AbortSignal): Promise<AdminSongCatalog> {
  const query = new URLSearchParams({ text: text.trim(), limit: '500' })
  const response = await fetch(`/api/admin/songs?${query}`, { credentials: 'same-origin', signal })
  if (!response.ok) throw new Error(response.status === 401
    ? 'Сессия истекла. Войдите снова.' : response.status === 403
      ? 'Недостаточно прав для просмотра каталога.' : 'Не удалось загрузить каталог. Повторите попытку.')
  return response.json() as Promise<AdminSongCatalog>
}

export const deleteAdminSong = (songId: string) =>
  mutate(`/api/admin/songs/${encodeURIComponent(songId)}`, 'DELETE', undefined,
    'Не удалось удалить песню. Проверьте доступ к файлу и повторите попытку.')
