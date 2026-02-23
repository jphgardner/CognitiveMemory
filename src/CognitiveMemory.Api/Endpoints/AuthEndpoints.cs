using System.Security.Claims;
using CognitiveMemory.Api.Auth;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost(
            "/register",
            async (RegisterRequest request, MemoryDbContext dbContext, JwtTokenFactory tokenFactory, CancellationToken cancellationToken) =>
            {
                var email = NormalizeEmail(request.Email);
                var password = request.Password?.Trim() ?? string.Empty;

                if (email.Length == 0 || password.Length < 8)
                {
                    return Results.BadRequest(new { error = "Valid email and password (8+ chars) are required." });
                }

                var exists = await dbContext.PortalUsers.AnyAsync(x => x.Email == email, cancellationToken);
                if (exists)
                {
                    return Results.Conflict(new { error = "Email already registered." });
                }

                var now = DateTimeOffset.UtcNow;
                var (hash, salt) = PasswordHashing.HashPassword(password);
                var user = new PortalUserEntity
                {
                    UserId = Guid.NewGuid(),
                    Email = email,
                    PasswordHash = hash,
                    PasswordSalt = salt,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    IsActive = true
                };

                dbContext.PortalUsers.Add(user);
                await dbContext.SaveChangesAsync(cancellationToken);

                var token = tokenFactory.CreateToken(user.UserId, user.Email);
                return Results.Ok(ToAuthResponse(user, token));
            });

        group.MapPost(
            "/login",
            async (LoginRequest request, MemoryDbContext dbContext, JwtTokenFactory tokenFactory, CancellationToken cancellationToken) =>
            {
                var email = NormalizeEmail(request.Email);
                var password = request.Password?.Trim() ?? string.Empty;
                if (email.Length == 0 || password.Length == 0)
                {
                    return Results.BadRequest(new { error = "Email and password are required." });
                }

                var user = await dbContext.PortalUsers.FirstOrDefaultAsync(x => x.Email == email, cancellationToken);
                if (user is null || !user.IsActive || !PasswordHashing.Verify(password, user.PasswordHash, user.PasswordSalt))
                {
                    return Results.Unauthorized();
                }

                user.UpdatedAtUtc = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);

                var token = tokenFactory.CreateToken(user.UserId, user.Email);
                return Results.Ok(ToAuthResponse(user, token));
            });

        group.MapGet(
                "/me",
                async (HttpContext httpContext, MemoryDbContext dbContext, CancellationToken cancellationToken) =>
                {
                    var userId = TryGetUserId(httpContext.User);
                    if (userId is null)
                    {
                        return Results.Unauthorized();
                    }

                    var user = await dbContext.PortalUsers.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
                    if (user is null || !user.IsActive)
                    {
                        return Results.Unauthorized();
                    }

                    return Results.Ok(new AuthUserDto(user.UserId, user.Email, user.CreatedAtUtc, user.UpdatedAtUtc));
                })
            .RequireAuthorization();

        return endpoints;
    }

    private static object ToAuthResponse(PortalUserEntity user, AuthTokenResult token)
        => new AuthResponse(
            token.AccessToken,
            token.ExpiresAtUtc,
            new AuthUserDto(user.UserId, user.Email, user.CreatedAtUtc, user.UpdatedAtUtc));

    private static Guid? TryGetUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue(ClaimTypes.Name);
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string NormalizeEmail(string? input) => input?.Trim().ToLowerInvariant() ?? string.Empty;
}

public sealed record RegisterRequest(string Email, string Password);
public sealed record LoginRequest(string Email, string Password);
public sealed record AuthUserDto(Guid UserId, string Email, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAtUtc, AuthUserDto User);
