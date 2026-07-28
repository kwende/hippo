using System.Text.Json;
using Hippo.Server.Authentication;
using Hippo.Server.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hippo.Tests;

public sealed class ApiKeyAuthenticationMiddlewareTests
{
    [Fact]
    public async Task Missing_authorization_header_returns_401()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext();
        var caller = new CallerIdentityAccessor();

        await middleware.InvokeAsync(context, caller);

        Assert.False(nextCalled);
        Assert.Null(caller.Current);
        await AssertUnauthorizedAsync(
            context,
            "missing_bearer_token");
    }

    [Fact]
    public async Task Invalid_bearer_token_returns_401()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext();
        context.Request.Headers.Authorization = "Bearer invalid-token";
        var caller = new CallerIdentityAccessor();

        await middleware.InvokeAsync(context, caller);

        Assert.False(nextCalled);
        Assert.Null(caller.Current);
        await AssertUnauthorizedAsync(
            context,
            "invalid_bearer_token");
    }

    [Fact]
    public async Task Valid_bearer_token_populates_identity_and_calls_next()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext();
        context.Request.Headers.Authorization = "Bearer valid-token";
        var caller = new CallerIdentityAccessor();

        await middleware.InvokeAsync(context, caller);

        Assert.True(nextCalled);
        Assert.Equal(
            new CallerIdentity(
                "test-principal",
                "Test Principal",
                "test-client"),
            caller.Current);
    }

    [Fact]
    public async Task Health_path_bypasses_authentication()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext();
        context.Request.Path = "/health/ready";
        var caller = new CallerIdentityAccessor();

        await middleware.InvokeAsync(context, caller);

        Assert.True(nextCalled);
        Assert.Null(caller.Current);
    }

    private static ApiKeyAuthenticationMiddleware CreateMiddleware(
        RequestDelegate next) =>
        new(
            next,
            Options.Create(
                new HippoOptions
                {
                    ApiKeys =
                    [
                        new ApiKeyDefinition
                        {
                            Token = "valid-token",
                            PrincipalId = "test-principal",
                            DisplayName = "Test Principal",
                            ClientName = "test-client"
                        }
                    ]
                }),
            NullLogger<ApiKeyAuthenticationMiddleware>.Instance);

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task AssertUnauthorizedAsync(
        HttpContext context,
        string expectedCode)
    {
        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            context.Response.StatusCode);
        Assert.Equal(
            "Bearer",
            context.Response.Headers.WWWAuthenticate.ToString());

        context.Response.Body.Position = 0;
        using var document =
            await JsonDocument.ParseAsync(context.Response.Body);

        Assert.Equal(
            expectedCode,
            document.RootElement.GetProperty("code").GetString());
    }
}
