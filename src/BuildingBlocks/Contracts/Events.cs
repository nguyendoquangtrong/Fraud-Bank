namespace Contracts;

public record TransactionRequested(
    string TransactionId,
    string FromAccount,
    string ToAccount,
    decimal Amount,
    string Type,
    decimal OldBalanceOrg,
    decimal NewBalanceOrig,
    decimal OldBalanceDest,
    decimal NewBalanceDest,
    DateTime OccurredAtUtc);

public record TransactionScored(
    string TransactionId,
    double Risk,
    string DecisionHint,
    IDictionary<string,double> Features,
    DateTime OccurredAtUtc);


public record TransactionDecided(
    string TransactionId,
    string Decision,
    double Risk,
    double AllowThreshold,
    double BlockThreshold,
    string? Reason,
    DateTime OccurredAtUtc);

public record LedgerApplied(
    string TransactionId,
    string FromAccount,
    string ToAccount,
    decimal Amount,
    decimal FinalBalanceOrig,
    decimal FinalBalanceDest,
    DateTime OccurredAtUtc);

public record TransactionReviewed(
    string TransactionId,
    string Action,          
    string? Note,
    string ReviewedBy,
    DateTime OccurredAtUtc);