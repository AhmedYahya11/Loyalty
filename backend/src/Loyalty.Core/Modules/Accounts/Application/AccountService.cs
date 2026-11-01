using Loyalty.Core.BuildingBlocks;
using Loyalty.Core.Modules.Accounts.Domain;
using Loyalty.Core.Modules.Accounts.Infrastructure;
using Loyalty.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Loyalty.Core.Modules.Accounts.Application;

public sealed record AccountView(
    Guid AccountId, Guid ApplicationId, string AccountName, string KeyPrefix,
    bool IsActive, DateTimeOffset? ExpiresAt);

/// <summary>The one-time key reveal returned by create / rotate.</summary>
public sealed record AccountWithKey(
    Guid AccountId, Guid ApplicationId, string AccountName, string ApiKey, string KeyPrefix, bool IsActive);

public sealed class AccountService(IRepository<Loyalty.Core.Modules.Applications.Domain.Application> applicationsRepo, IRepository<Loyalty.Core.Modules.Accounts.Domain.Account> accountsRepo, IUnitOfWork uow, ApplicationContext scope, IClock clock)
{
    public async Task<AccountWithKey> CreateAsync(
        Guid applicationId, string accountName, DateTimeOffset? expiresAt, CancellationToken ct)
    {
        scope.EnterSystemScope();
        try
        {
            if (string.IsNullOrWhiteSpace(accountName))
                throw new LoyaltyException(LoyaltyError.ValidationFailed("accountName is required."));

            var appExists = await applicationsRepo.Query().AnyAsync(a => a.Id == applicationId, ct);
            if (!appExists) throw new LoyaltyException(LoyaltyError.NotFound("Application"));

            var dupe = await accountsRepo.Query().AnyAsync(a => a.ApplicationId == applicationId && a.AccountName == accountName.Trim(), ct);
            if (dupe) throw new LoyaltyException(LoyaltyError.Conflict("duplicate_account_name", "An account with this name already exists in the application."));

            var key = ApiKeyGenerator.Generate();
            var account = Account.Create(Guid.NewGuid(), applicationId, accountName, key.KeyPrefix, key.KeyHash, expiresAt, clock.UtcNow);
            accountsRepo.Add(account);
            await uow.SaveChangesAsync(ct);

            return new AccountWithKey(account.Id, applicationId, account.AccountName, key.RawKey, key.KeyPrefix, account.IsActive);
        }
        finally { scope.LeaveSystemScope(); }
    }

    public async Task<AccountWithKey> RotateKeyAsync(Guid accountId, CancellationToken ct)
    {
        scope.EnterSystemScope();
        try
        {
            var account = await accountsRepo.Query().FirstOrDefaultAsync(a => a.Id == accountId, ct)
                ?? throw new LoyaltyException(LoyaltyError.NotFound("Account"));
            var key = ApiKeyGenerator.Generate();
            account.RotateKey(key.KeyPrefix, key.KeyHash);
            await uow.SaveChangesAsync(ct);
            return new AccountWithKey(account.Id, account.ApplicationId, account.AccountName, key.RawKey, key.KeyPrefix, account.IsActive);
        }
        finally { scope.LeaveSystemScope(); }
    }

    public async Task RevokeAsync(Guid accountId, CancellationToken ct)
    {
        scope.EnterSystemScope();
        try
        {
            var account = await accountsRepo.Query().FirstOrDefaultAsync(a => a.Id == accountId, ct)
                ?? throw new LoyaltyException(LoyaltyError.NotFound("Account"));
            account.Revoke(clock.UtcNow);
            await uow.SaveChangesAsync(ct);
        }
        finally { scope.LeaveSystemScope(); }
    }

    public async Task SetStatusAsync(Guid accountId, bool active, CancellationToken ct)
    {
        scope.EnterSystemScope();
        try
        {
            var account = await accountsRepo.Query().FirstOrDefaultAsync(a => a.Id == accountId, ct)
                ?? throw new LoyaltyException(LoyaltyError.NotFound("Account"));
            account.SetActive(active);
            await uow.SaveChangesAsync(ct);
        }
        finally { scope.LeaveSystemScope(); }
    }

    public async Task<IReadOnlyList<AccountView>> ListAsync(Guid applicationId, CancellationToken ct) =>
        await accountsRepo.Query()
            .Where(a => a.ApplicationId == applicationId)
            .OrderBy(a => a.AccountName)
            .Select(a => new AccountView(a.Id, a.ApplicationId, a.AccountName, a.KeyPrefix, a.IsActive, a.ExpiresAt))
            .ToListAsync(ct);

    /// <summary>
    /// Authenticate a raw API key: prefix lookup + constant-time hash compare + usability.
    /// Runs in system scope. Returns the account or null; the caller maps null to 401.
    /// </summary>
    public async Task<Account?> AuthenticateAsync(string rawKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawKey)) return null;
        scope.EnterSystemScope();
        try
        {
            var prefix = ApiKeyGenerator.PrefixOf(rawKey);
            var account = await accountsRepo.Query().FirstOrDefaultAsync(a => a.KeyPrefix == prefix, ct);
            if (account is null) return null;
            if (!ApiKeyGenerator.Verify(rawKey, account.KeyHash)) return null;
            if (!account.IsUsable(clock.UtcNow)) return null;
            return account;
        }
        finally { scope.LeaveSystemScope(); }
    }

    public Task MarkAuthenticatedAsync(Account account, CancellationToken ct)
    {
        account.MarkAuthenticated(clock.UtcNow);
        return uow.SaveChangesAsync(ct);
    }
}
