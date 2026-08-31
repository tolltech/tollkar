using System.Security.Claims;

namespace Tollkar.Web.Authentication;

public static class AdministratorAccount
{
    public const string Login = "admin";
    public const string PolicyName = "AdminOnly";

    public static bool IsAdministrator(ClaimsPrincipal principal) =>
        IsAdministrator(principal.Identity?.Name);

    public static bool IsAdministrator(TollkarUser user) =>
        IsAdministrator(user.UserName);

    private static bool IsAdministrator(string? login) =>
        string.Equals(login, Login, StringComparison.OrdinalIgnoreCase);
}
