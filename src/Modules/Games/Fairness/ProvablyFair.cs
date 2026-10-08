using System.Security.Cryptography;
using System.Text;

namespace Casino.Modules.Games.Fairness;

/// <summary>
/// Utilidades del esquema provably fair. El algoritmo exacto esta especificado en docs/provably-fair.md
/// para que cualquiera pueda reimplementarlo y verificar las jugadas.
/// </summary>
public static class ProvablyFair
{
    /// <summary>Server seed nueva: 32 bytes aleatorios criptograficos, como 64 caracteres hex en minuscula.</summary>
    public static string GenerateServerSeed() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    /// <summary>Compromiso publicable antes de jugar: SHA-256 (hex) de la server seed en UTF-8.</summary>
    public static string Commitment(string serverSeed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverSeed);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(serverSeed)));
    }

    /// <summary>Comprueba (en tiempo constante) que una server seed revelada corresponde a un compromiso previo.</summary>
    public static bool MatchesCommitment(string revealedServerSeed, string commitment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitment);

        var expected = Encoding.ASCII.GetBytes(Commitment(revealedServerSeed));
        var provided = Encoding.ASCII.GetBytes(commitment.ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }
}
