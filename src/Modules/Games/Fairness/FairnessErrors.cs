namespace Casino.Modules.Games.Fairness;

public enum FairnessError
{
    InvalidClientSeed,
    InvalidCommitment,
    PendingBets,
    BetNotFound,
    SeedDoesNotMatchCommitment,
}

public sealed class FairnessDomainException(FairnessError error, string message) : Exception(message)
{
    public FairnessError Error { get; } = error;
}
