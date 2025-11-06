public record TransactionRequest(
    string? TransactionId,
    string FromAccount,
    string ToAccount,
    decimal Amount,
    string Type,
    decimal OldBalanceOrg,
    decimal NewBalanceOrig,
    decimal OldBalanceDest,
    decimal NewBalanceDest);