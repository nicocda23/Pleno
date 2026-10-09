using System.Diagnostics.Metrics;

namespace Casino.Modules.Wallet.Application;

/// <summary>Metricas de la Wallet. ServiceDefaults ya exporta todo meter "Casino.*" por OpenTelemetry.</summary>
public static class WalletTelemetry
{
    public const string MeterName = "Casino.Wallet";
    public const string ReasonTag = "reason";
    public const string ConflictReason = "conflict";
    public const string TransientReason = "transient";

    private static readonly Meter Meter = new(MeterName);

    /// <summary>Cada vez que una operacion tiene que recargar y repetirse: por conflicto de version o por fallo transitorio.</summary>
    public static readonly Counter<long> OperationRetries = Meter.CreateCounter<long>(
        "wallet.operation.retries",
        unit: "{retry}",
        description: "Reintentos internos de operaciones de la Wallet, por motivo.");
}
