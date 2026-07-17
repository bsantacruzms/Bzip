using System.Buffers.Binary;
using Sodium;

namespace BoltZip.Core.Bz;

/// <summary>
/// Write side of a chunked authenticated-encryption stream. Buffers plaintext into fixed
/// chunks, encrypts each with XChaCha20-Poly1305 under a per-chunk nonce, and frames the
/// output as <c>[int32 length][ciphertext+tag]</c>. The chunk counter and a "final" flag are
/// bound as associated data to prevent reordering and truncation.
/// </summary>
internal sealed class ChunkedAeadWriteStream : Stream
{
    private readonly Stream _inner;
    private readonly byte[] _key;
    private readonly byte[] _noncePrefix;
    private readonly int _chunkSize;
    private readonly bool _leaveOpen;
    private readonly byte[] _buffer;
    private int _bufferPos;
    private ulong _counter;
    private bool _completed;

    public ChunkedAeadWriteStream(Stream inner, byte[] key, byte[] noncePrefix, int chunkSize, bool leaveOpen = true)
    {
        _inner = inner;
        _key = key;
        _noncePrefix = noncePrefix;
        _chunkSize = chunkSize;
        _leaveOpen = leaveOpen;
        _buffer = new byte[chunkSize];
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
        => Write(new ReadOnlySpan<byte>(buffer, offset, count));

    public override void Write(ReadOnlySpan<byte> data)
    {
        while (!data.IsEmpty)
        {
            var space = _chunkSize - _bufferPos;
            var take = Math.Min(space, data.Length);
            data[..take].CopyTo(_buffer.AsSpan(_bufferPos));
            _bufferPos += take;
            data = data[take..];

            if (_bufferPos == _chunkSize)
            {
                WriteChunk(final: false);
            }
        }
    }

    /// <summary>Emits the trailing (possibly empty) chunk marked as final. Idempotent.</summary>
    public void CompleteFinal()
    {
        if (_completed)
        {
            return;
        }

        WriteChunk(final: true);
        _completed = true;
    }

    private void WriteChunk(bool final)
    {
        var nonce = new byte[24];
        _noncePrefix.CopyTo(nonce, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(nonce.AsSpan(16), _counter);

        var aad = new byte[9];
        BinaryPrimitives.WriteUInt64LittleEndian(aad, _counter);
        aad[8] = (byte)(final ? 1 : 0);

        var plain = new byte[_bufferPos];
        Array.Copy(_buffer, plain, _bufferPos);

        var cipher = SecretAeadXChaCha20Poly1305.Encrypt(plain, nonce, _key, aad);

        Span<byte> lengthPrefix = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, cipher.Length);
        _inner.Write(lengthPrefix);
        _inner.Write(cipher, 0, cipher.Length);

        _counter++;
        _bufferPos = 0;
    }

    public override void Flush() => _inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CompleteFinal();
            if (!_leaveOpen)
            {
                _inner.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Read side matching <see cref="ChunkedAeadWriteStream"/>. Reads framed chunks, verifies
/// and decrypts each, and enforces the exact stored length so the final chunk is detected
/// and truncation is rejected. Throws on authentication failure (wrong password/tampering).
/// </summary>
internal sealed class ChunkedAeadReadStream : Stream
{
    private readonly Stream _inner;
    private readonly byte[] _key;
    private readonly byte[] _noncePrefix;
    private readonly long _storedLength;
    private long _storedConsumed;
    private ulong _counter;
    private byte[] _current = Array.Empty<byte>();
    private int _currentPos;
    private bool _finished;

    public ChunkedAeadReadStream(Stream inner, byte[] key, byte[] noncePrefix, long storedLength)
    {
        _inner = inner;
        _key = key;
        _noncePrefix = noncePrefix;
        _storedLength = storedLength;
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
        if (buffer.IsEmpty)
        {
            return 0;
        }

        if (_currentPos >= _current.Length)
        {
            if (_finished || _storedConsumed >= _storedLength)
            {
                return 0;
            }

            ReadNextChunk();
        }

        var available = _current.Length - _currentPos;
        if (available <= 0)
        {
            return 0;
        }

        var take = Math.Min(available, buffer.Length);
        _current.AsSpan(_currentPos, take).CopyTo(buffer);
        _currentPos += take;
        return take;
    }

    private void ReadNextChunk()
    {
        Span<byte> lengthPrefix = stackalloc byte[4];
        ReadInnerExactly(lengthPrefix);
        _storedConsumed += 4;

        var cipherLength = BinaryPrimitives.ReadInt32LittleEndian(lengthPrefix);
        if (cipherLength < 0 || _storedConsumed + cipherLength > _storedLength)
        {
            throw new InvalidDataException("Corrupt BoltZip archive (bad chunk length).");
        }

        var cipher = new byte[cipherLength];
        ReadInnerExactly(cipher);
        _storedConsumed += cipherLength;

        var final = _storedConsumed >= _storedLength;

        var nonce = new byte[24];
        _noncePrefix.CopyTo(nonce, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(nonce.AsSpan(16), _counter);

        var aad = new byte[9];
        BinaryPrimitives.WriteUInt64LittleEndian(aad, _counter);
        aad[8] = (byte)(final ? 1 : 0);

        _current = SecretAeadXChaCha20Poly1305.Decrypt(cipher, nonce, _key, aad);
        _currentPos = 0;
        _counter++;

        if (final)
        {
            _finished = true;
        }
    }

    private void ReadInnerExactly(Span<byte> destination)
    {
        var read = 0;
        while (read < destination.Length)
        {
            var n = _inner.Read(destination[read..]);
            if (n == 0)
            {
                throw new EndOfStreamException("Truncated BoltZip archive.");
            }

            read += n;
        }
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
