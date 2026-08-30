import type { Song } from './api'

export function SongDetails({ song }: { song: Pick<Song, 'title' | 'artist'> }) {
  return <div className="song-details">
    <strong>{song.title}</strong>
    <span>{song.artist || 'Неизвестный исполнитель'}</span>
  </div>
}
