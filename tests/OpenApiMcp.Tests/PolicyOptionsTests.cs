using OpenApiMcp;
using Xunit;

public class PolicyOptionsTests
{
    [Fact]
    public void Mutating_is_blocked_by_default()
    {
        var p = new PolicyOptions();
        Assert.False(p.IsAllowed("createPost", isMutating: true));
        Assert.True(p.IsAllowed("getPost", isMutating: false));
    }

    [Fact]
    public void Mutating_is_allowed_when_opted_in()
    {
        var p = new PolicyOptions { AllowMutating = true };
        Assert.True(p.IsAllowed("createPost", isMutating: true));
    }

    [Fact]
    public void Allowlist_filters_by_name()
    {
        var p = new PolicyOptions { AllowedOperations = new() { "getPost" } };
        Assert.True(p.IsAllowed("getPost", isMutating: false));
        Assert.False(p.IsAllowed("listPosts", isMutating: false));
    }

    [Fact]
    public void Wildcard_allows_any_read_operation()
    {
        var p = new PolicyOptions { AllowedOperations = new() { "*" } };
        Assert.True(p.IsAllowed("anything", isMutating: false));
    }
}
