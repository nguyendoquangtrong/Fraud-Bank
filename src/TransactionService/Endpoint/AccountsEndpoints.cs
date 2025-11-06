using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TransactionService.Infrastructure;

namespace TransactionService.Endpoints;

public static class AccountsEndpoints
{
    public static IEndpointRouteBuilder MapAccountsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accounts")
                       .WithTags("Accounts");

        group.MapGet("/{accountNo}", GetByAccountNoAsync)
             .Produces<AccountResponse>(StatusCodes.Status200OK)
             .Produces(StatusCodes.Status404NotFound)
             .WithSummary("Get account holder information by account number.");

        return app;
    }

    public record AccountResponse(string AccountNo, string? HolderName, decimal Balance);

    private static async Task<Results<Ok<AccountResponse>, NotFound>> GetByAccountNoAsync(
        string accountNo,
        AppDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accountNo))
        {
            return TypedResults.NotFound();
        }

        var normalized = accountNo.Trim();
        var account = await db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AccountNo == normalized, ct);

        if (account is null)
        {
            return TypedResults.NotFound();
        }

        var response = new AccountResponse(account.AccountNo, account.HolderName, account.Balance);
        return TypedResults.Ok(response);
    }
}
