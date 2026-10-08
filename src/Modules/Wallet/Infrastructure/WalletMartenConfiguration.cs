using Casino.Modules.Wallet.Domain;
using Marten;
using JasperFx;

namespace Casino.Modules.Wallet.Infrastructure;

public static class WalletMartenConfiguration
{
    public const string SchemaName = "wallet";

    public static void Configure(StoreOptions options, string connectionString)
    {
        options.Connection(connectionString);
        options.DatabaseSchemaName = SchemaName;
        options.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;

        options.Events.AddEventTypes(
        [
            typeof(AccountOpened),
            typeof(ChipsCredited),
            typeof(BetReserved),
            typeof(BetSettled),
            typeof(ReservationReleased),
            typeof(OperationReversed),
        ]);

        options.Schema.For<IdempotencyRecord>().Identity(r => r.Id);
    }
}

/// <summary>
/// Segunda barrera de idempotencia: PK unica por (cuenta, clave) en la misma transaccion que los eventos.
/// La primera es el chequeo dentro del stream.
/// </summary>
public sealed class IdempotencyRecord
{
    public required string Id { get; init; }

    public required Guid AccountId { get; init; }

    public required string Key { get; init; }

    public required Guid TransactionId { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }

    public static string BuildId(Guid accountId, string key) => $"{accountId:N}:{key}";
}
