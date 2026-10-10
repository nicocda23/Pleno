using System.Security.Cryptography;
using System.Text;
using Casino.BuildingBlocks;
using Casino.Modules.Wallet.Domain;

namespace Casino.Modules.Wallet.Application;

public sealed partial class WalletService
{
    /// <summary>
    /// Transfiere fichas de un jugador a otro (la carga de un cajero, un cobro). Las dos cuentas se escriben en UNA sola transaccion: o se mueven las dos o ninguna,
    /// y la suma de fichas del sistema no cambia. Es idempotente por (cuenta que envia, <paramref name="key"/>): repetir la misma operacion no mueve nada de nuevo.
    /// Quien llama es responsable de haber comprobado que la transferencia esta permitida (jurisdiccion); aca solo se garantiza la contabilidad.
    /// </summary>
    public async Task<OperationOutcome> TransferAsync(Guid fromUserId, Guid toUserId, string key, long amount, CancellationToken ct = default)
    {
        if (fromUserId == toUserId)
        {
            throw new WalletDomainException(WalletError.InvalidTransfer, "No se pueden transferir fichas a la misma cuenta.");
        }

        var fromId = PlayerIds.WalletAccountFor(fromUserId);
        var toId = PlayerIds.WalletAccountFor(toUserId);
        var lowGate = Math.Min(GateIndex(fromId), GateIndex(toId));
        var highGate = Math.Max(GateIndex(fromId), GateIndex(toId));
        // La mitad de entrada usa su propia clave (derivada) para no chocar con operaciones de la cuenta que recibe que tengan la misma clave.
        var inKey = $"in:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{fromUserId:N}|{key}")))}";

        for (var attempt = 1; ; attempt++)
        {
            TimeSpan retryDelay;
            // Las compuertas se toman siempre en el mismo orden para que dos transferencias cruzadas no se bloqueen entre si.
            await _gates[lowGate].WaitAsync(ct);
            var highHeld = false;
            try
            {
                if (highGate != lowGate)
                {
                    await _gates[highGate].WaitAsync(ct);
                    highHeld = true;
                }

                await using var session = store.LightweightSession();
                var from = await LoadAsync(session, fromId, ct);
                var to = await LoadAsync(session, toId, ct);
                var fromVersion = from.Version;
                var toVersion = to.Version;

                var transferId = Guid.CreateVersion7();
                var outcome = from.TransferOut(key, transferId, toUserId, amount, clock.GetUtcNow());
                if (outcome.IsDuplicate)
                {
                    return outcome; // ya se hizo (las dos mitades salen juntas)
                }

                to.TransferIn(inKey, transferId, fromUserId, amount, clock.GetUtcNow());

                await using var outboxSession = outbox?.Enroll(session);
                await StageAsync(session, outboxSession, fromId, from, fromVersion);
                await StageAsync(session, outboxSession, toId, to, toVersion);

                await session.SaveChangesAsync(ct);
                return outcome;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsRetryable(ex) && !ct.IsCancellationRequested)
            {
                WalletTelemetry.OperationRetries.Add(
                    1,
                    new KeyValuePair<string, object?>(
                        WalletTelemetry.ReasonTag,
                        IsConcurrencyConflict(ex) ? WalletTelemetry.ConflictReason : WalletTelemetry.TransientReason));
                retryDelay = BackoffFor(attempt);
            }
            finally
            {
                if (highHeld)
                {
                    _gates[highGate].Release();
                }

                _gates[lowGate].Release();
            }

            await Task.Delay(retryDelay, ct);
        }
    }

    private static int GateIndex(Guid accountId) => (accountId.GetHashCode() & int.MaxValue) % GateCount;
}
