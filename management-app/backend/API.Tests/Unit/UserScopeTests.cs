using System.Security.Claims;
using API.Auth;
using API.Models;

namespace API.Tests.Unit;

public class UserScopeTests
{
    private static User UserCreatedBy(string? createdBy) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Name = "Test",
        Secret = Guid.NewGuid().ToString(),
        CreatedBy = createdBy,
    };

    private static ClaimsPrincipal Principal(string username, string role) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, username), new Claim(ClaimTypes.Role, role)],
            authenticationType: "Test", nameType: ClaimTypes.Name, roleType: ClaimTypes.Role));

    [Theory]
    [InlineData("alice", "alice", false, true)]
    [InlineData("Alice", "alice", false, true)]
    [InlineData("bob", "alice", false, false)]
    [InlineData("root", "alice", false, false)]
    [InlineData(null, "alice", false, false)]
    [InlineData("root", "root", true, true)]
    [InlineData("bob", "root", true, true)]
    [InlineData(null, "root", true, true)]
    public void IsVisibleTo_ReturnsExpected(string? createdBy, string username, bool isSuperAdmin, bool expected)
    {
        Assert.Equal(expected, UserScope.IsVisibleTo(UserCreatedBy(createdBy), username, isSuperAdmin));
    }

    [Fact]
    public void CanSee_NormalAdminPrincipal_SeesOnlyOwnUsers()
    {
        var alice = Principal("alice", AdminRoles.Admin);

        Assert.True(alice.CanSee(UserCreatedBy("alice")));
        Assert.False(alice.CanSee(UserCreatedBy("bob")));
        Assert.False(alice.CanSee(UserCreatedBy(null)));
    }

    [Fact]
    public void CanSee_SuperAdminPrincipal_SeesEveryone()
    {
        var root = Principal("root", AdminRoles.SuperAdmin);

        Assert.True(root.CanSee(UserCreatedBy("bob")));
        Assert.True(root.CanSee(UserCreatedBy(null)));
    }
}
