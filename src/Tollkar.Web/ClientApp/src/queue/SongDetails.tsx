import type { Song } from './api'

// Queue items carry no folder, so the badge is optional and simply absent there.
export function SongDetails({ song }: { song: Pick<Song, 'title' | 'artist'> & { folder?: string | null } }) {
  return <div className="song-details">
    <strong>{song.title}</strong>
    <span>
      {song.artist || 'Неизвестный исполнитель'}
      {song.folder && <span className="song-folder"><span className="visually-hidden">Папка: </span>{song.folder}</span>}
    </span>
  </div>
}
