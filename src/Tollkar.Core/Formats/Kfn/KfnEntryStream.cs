namespace Tollkar.Core.Formats.Kfn;

/// <summary>
/// A seekable read-only window over a region of the container. Keeping the stream seekable is
/// what lets callers serve an embedded track with HTTP range requests without extracting it.
/// </summary>
internal sealed class KfnEntryStream(Stream inner, long origin, long length) : Stream
{
    private long _position;

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        var window = Window(buffer.Length);
        if (window == 0) return 0;

        inner.Position = origin + _position;
        var read = inner.Read(buffer[..window]);
        _position += read;
        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var window = Window(buffer.Length);
        if (window == 0) return 0;

        inner.Position = origin + _position;
        var read = await inner.ReadAsync(buffer[..window], cancellationToken);
        _position += read;
        return read;
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "Unknown seek origin.")
        };

        if (target < 0)
        {
            throw new IOException("Cannot seek before the beginning of the entry.");
        }

        _position = target;
        return _position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) =>
        throw new NotSupportedException("A container entry is read-only.");

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("A container entry is read-only.");

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync();
        await base.DisposeAsync();
    }

    private int Window(int requested) => (int)Math.Clamp(length - _position, 0, requested);
}
