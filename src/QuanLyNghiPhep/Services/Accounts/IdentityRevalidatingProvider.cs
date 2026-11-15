using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace QuanLyNghiPhep.Services.Accounts;

public sealed class IdentityRevalidatingProvider(ILoggerFactory loggerFactory, AccountValidity validity)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(30);
    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken cancellationToken)
        => Task.FromResult(validity.IsValid(state.User));
}
