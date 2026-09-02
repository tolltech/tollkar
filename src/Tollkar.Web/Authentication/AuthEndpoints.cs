using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tollkar.Web.Logging;

namespace Tollkar.Web.Authentication;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth");
        group.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }))
            .AllowAnonymous();
        group.MapGet("/me", async (HttpContext context, UserManager<TollkarUser> users) =>
        {
            if (context.User.HasClaim(GuestAccess.GuestClaim, bool.TrueString))
                return Results.Ok(new
                {
                    id = context.User.FindFirstValue(ClaimTypes.NameIdentifier),
                    login = "Гость",
                    isAdmin = false,
                    isGuest = true
                });
            var user = await users.GetUserAsync(context.User);
            return user is null ? Results.Unauthorized() : Results.Ok(ToResponse(user));
        });
        group.MapPost("/register", RegisterAsync)
            .LogUserAction()
            .AddEndpointFilter<ValidateAuthRequest>()
            .RequireAuthorization(AdministratorAccount.PolicyName);
        group.MapPost("/login", LoginAsync)
            .LogUserAction(UserActionIdentitySource.SuccessfulLogin)
            .AddEndpointFilter<ValidateAuthRequest>()
            .AllowAnonymous();
        group.MapPost("/logout", async (SignInManager<TollkarUser> signIn, HttpContext context) =>
        {
            await signIn.SignOutAsync();
            await context.SignOutAsync(GuestAccess.AuthenticationScheme);
            return Results.NoContent();
        }).LogUserAction().AddEndpointFilter<ValidateAuthRequest>();
    }

    private static async Task<IResult> RegisterAsync(Credentials credentials, UserManager<TollkarUser> users)
    {
        var user = new TollkarUser { UserName = credentials.Login };
        IdentityResult result;
        try
        {
            result = await users.CreateAsync(user, credentials.Password!);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException
               { SqliteExtendedErrorCode: 2067 } sqlite &&
               sqlite.Message.Contains("AspNetUsers.NormalizedUserName", StringComparison.Ordinal))
        {
            // Concurrent registrations can pass Identity's check before the unique index is written.
            result = IdentityResult.Failed(users.ErrorDescriber.DuplicateUserName(credentials.Login!));
        }
        if (!result.Succeeded)
            return Results.ValidationProblem(result.Errors.GroupBy(error => error.Code)
                .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));

        return Results.Ok(ToResponse(user));
    }

    private static async Task<IResult> LoginAsync(Credentials credentials,
        UserManager<TollkarUser> users, SignInManager<TollkarUser> signIn, HttpContext context)
    {
        var user = await users.FindByNameAsync(credentials.Login!);
        if (user is null)
            return InvalidLogin();

        var result = await signIn.PasswordSignInAsync(user, credentials.Password!,
            isPersistent: false, lockoutOnFailure: true);
        if (!result.Succeeded)
            return InvalidLogin();

        await context.SignOutAsync(GuestAccess.AuthenticationScheme);
        context.Items[UserActionLogging.SuccessfulLoginKey] = user.UserName;
        return Results.Ok(ToResponse(user));
    }

    private static IResult InvalidLogin() => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["InvalidCredentials"] = ["Неверный логин или пароль."] },
        statusCode: StatusCodes.Status401Unauthorized);

    private static object ToResponse(TollkarUser user) => new
    {
        user.Id,
        login = user.UserName,
        isAdmin = AdministratorAccount.IsAdministrator(user),
        isGuest = false
    };
}
