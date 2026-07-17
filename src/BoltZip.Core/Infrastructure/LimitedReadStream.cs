namespace BoltZip.Core.Infrastructure;

/// <summary>
/// A read-only view over a fixed number of bytes from an inner stream. Used to bound
/// codec readers to an archive section without exposing trailing bytes.
/// </summary>
internal sealed class LimitedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly bool _leaveOpen;
    private long _remaining;

    public LimitedReadStream(Stream inner, long length, bool leaveOpen = true)
    {
        _inner = inner;
        _remaining = length;
        _leaveOpen = leaveOpen;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
        => Read(new Span<byte>(buffer, offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_remaining <= 0 || buffer.IsEmpty)
        {
            return 0;
        }

        var toRead = (int)Math.Min(buffer.Length, _remaining);
        var read = _inner.Read(buffer[..toRead]);
        _remaining -= read;
        return read;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveOpen)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
