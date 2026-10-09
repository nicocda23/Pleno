using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Casino.Modules.Games.Fairness;

/// <summary>
/// Flujo determinista de enteros uniformes derivado de (serverSeed, clientSeed, nonce).
/// Cada bloque es HMAC-SHA256(clave = serverSeed, mensaje = "clientSeed:nonce:cursor"); los bytes se consumen
/// de a 4 (uint32 big-endian) y se descartan los valores de la "cola" para no sesgar el resultado.
/// </summary>
public sealed class FairRng
{
    private const int MaxClientSeedLength = 64;

    private readonly byte[] _key;
    private readonly string _clientSeed;
    private readonly long _nonce;
    private byte[] _block = [];
    private int _offset;
    private long _cursor;

    public FairRng(string serverSeed, string clientSeed, long nonce)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverSeed);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientSeed);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(clientSeed.Length, MaxClientSeedLength);
        ArgumentOutOfRangeException.ThrowIfNegative(nonce);

        _key = Encoding.UTF8.GetBytes(serverSeed);
        _clientSeed = clientSeed;
        _nonce = nonce;
    }

    /// <summary>Entero uniforme en [0, <paramref name="maxExclusive"/>).</summary>
    public int NextInt(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxExclusive, 1);

        // Rejection sampling: el mayor multiplo de max que cabe en 2^32. Lo que lo supera se descarta.
        var limit = (1UL << 32) / (ulong)maxExclusive * (ulong)maxExclusive;
        while (true)
        {
            var value = NextUInt32();
            if (value < limit)
            {
                return (int)(value % (ulong)maxExclusive);
            }
        }
    }

    private uint NextUInt32()
    {
        if (_offset + sizeof(uint) > _block.Length)
        {
            var message = FormattableString.Invariant($"{_clientSeed}:{_nonce}:{_cursor}");
            _block = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(message));
            _cursor++;
            _offset = 0;
        }

        var value = BinaryPrimitives.ReadUInt32BigEndian(_block.AsSpan(_offset, sizeof(uint)));
        _offset += sizeof(uint);
        return value;
    }
}
