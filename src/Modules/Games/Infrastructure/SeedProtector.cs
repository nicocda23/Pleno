using System.Security.Cryptography;
using System.Text;

namespace Casino.Modules.Games.Infrastructure;

/// <summary>
/// Cifra la server seed en reposo con AES-256-GCM (cifrado autenticado). El "dato asociado" (AAD) ata el texto cifrado
/// a su usuario y par de seeds: copiar un cifrado de una cuenta a otra no permite abrirlo.
/// Formato: base64( version(1) | nonce(12) | tag(16) | cifrado ).
/// </summary>
public sealed class SeedProtector
{
    private const byte FormatVersion = 1;
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public SeedProtector(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeySize)
        {
            throw new ArgumentException($"La clave maestra debe tener {KeySize} bytes (AES-256).", nameof(key));
        }

        _key = [.. key];
    }

    /// <summary>Lee la clave maestra de una configuracion segura (user-secrets o Key Vault), en base64.</summary>
    public static SeedProtector FromBase64Key(string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
        {
            throw new InvalidOperationException(
                "Falta la clave maestra 'Fairness:MasterKey' (32 bytes en base64). En local: dotnet user-secrets set \"Fairness:MasterKey\" <valor>.");
        }

        try
        {
            return new SeedProtector(Convert.FromBase64String(base64Key));
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("'Fairness:MasterKey' no es base64 valido.", ex);
        }
    }

    public static string GenerateBase64Key() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeySize));

    public string Protect(string plaintext, string associatedData)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(associatedData));

        var payload = new byte[1 + NonceSize + TagSize + cipher.Length];
        payload[0] = FormatVersion;
        nonce.CopyTo(payload, 1);
        tag.CopyTo(payload, 1 + NonceSize);
        cipher.CopyTo(payload, 1 + NonceSize + TagSize);
        return Convert.ToBase64String(payload);
    }

    /// <exception cref="CryptographicException">Clave incorrecta, dato asociado distinto o texto manipulado.</exception>
    public string Unprotect(string protectedValue, string associatedData)
    {
        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(protectedValue);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("El valor cifrado no es base64 valido.", ex);
        }

        if (payload.Length < 1 + NonceSize + TagSize || payload[0] != FormatVersion)
        {
            throw new CryptographicException("Formato de valor cifrado desconocido.");
        }

        var nonce = payload.AsSpan(1, NonceSize);
        var tag = payload.AsSpan(1 + NonceSize, TagSize);
        var cipher = payload.AsSpan(1 + NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain, Encoding.UTF8.GetBytes(associatedData));
        return Encoding.UTF8.GetString(plain);
    }
}
