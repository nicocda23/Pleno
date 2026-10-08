using System.Security.Claims;
using Casino.BuildingBlocks;

namespace Casino.Wallet.Tests;

public class PlayerIdsTests
{
    [Fact]
    public void The_account_id_is_deterministic_for_a_user()
    {
        var userId = Guid.NewGuid();

        Assert.Equal(PlayerIds.WalletAccountFor(userId), PlayerIds.WalletAccountFor(userId));
    }

    [Fact]
    public void Different_users_get_different_accounts_and_never_the_user_id_itself()
    {
        var users = Enumerable.Range(0, 1_000).Select(_ => Guid.NewGuid()).ToList();

        var accounts = users.Select(PlayerIds.WalletAccountFor).ToList();

        Assert.Equal(1_000, accounts.Distinct().Count());
        Assert.Empty(accounts.Intersect(users)); // chocaria con el stream de seeds del mismo usuario
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

    [Fact]
    public void The_user_id_comes_from_the_sub_claim()
    {
        var userId = Guid.NewGuid();

        Assert.True(Principal(new Claim("sub", userId.ToString())).TryGetUserId(out var parsed));
        Assert.Equal(userId, parsed);
    }

    [Fact]
    public void The_name_identifier_claim_is_accepted_as_a_fallback()
    {
        var userId = Guid.NewGuid();

        Assert.True(Principal(new Claim(ClaimTypes.NameIdentifier, userId.ToString())).TryGetUserId(out var parsed));
        Assert.Equal(userId, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-es-un-uuid")]
    [InlineData("12345")]
    public void An_invalid_or_missing_subject_yields_no_user(string sub)
    {
        Assert.False(Principal(new Claim("sub", sub)).TryGetUserId(out _));
        Assert.False(Principal().TryGetUserId(out _));
        Assert.Throws<InvalidOperationException>(() => Principal(new Claim("sub", sub)).GetUserId());
    }
}
