using System.Globalization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using Tollkar.Web.Realtime;
using Vostok.Logging.Abstractions;

namespace Tollkar.Web.Logging;

internal static class UserActionLogging
{
    internal static readonly object SuccessfulLoginKey = new();
    private static readonly object SuppressLoggingKey = new();

    internal static RouteHandlerBuilder LogUserAction(this RouteHandlerBuilder endpoint,
        UserActionIdentitySource identitySource = UserActionIdentitySource.AuthenticatedUser) =>
        endpoint.WithMetadata(new UserActionMetadata(identitySource));

    internal static RouteHandlerBuilder SuppressAutomaticPlaybackLogging(this RouteHandlerBuilder endpoint) =>
        endpoint.AddEndpointFilter(async (context, next) =>
        {
            if (context.Arguments.OfType<PlaybackCommand>().Any(command => command.Action == "ended"))
                context.HttpContext.Items[SuppressLoggingKey] = true;
            return await next(context);
        });

    internal static IApplicationBuilder UseUserActionLogging(this IApplicationBuilder app) =>
        app.UseMiddleware<UserActionLoggingMiddleware>();

    internal static bool IsSuppressed(HttpContext context) => context.Items.ContainsKey(SuppressLoggingKey);
}

internal enum UserActionIdentitySource
{
    AuthenticatedUser,
    SuccessfulLogin
}

internal sealed record UserActionMetadata(UserActionIdentitySource IdentitySource);

internal sealed class UserActionLoggingMiddleware(RequestDelegate next, ILog log)
{
    private const int MaximumParameterLength = 4096;
    private const string Redacted = "REDACTED";
    private readonly ILog logger = log.ForContext<UserActionLoggingMiddleware>();

    public async Task InvokeAsync(HttpContext context)
    {
        var metadata = context.GetEndpoint()?.Metadata.GetMetadata<UserActionMetadata>();
        if (metadata is null)
        {
            await next(context);
            return;
        }

        var authenticatedLogin = context.User.Identity?.Name;
        var method = context.Request.Method;
        var endpoint = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? context.Request.Path.Value;
        var routeParameters = FormatRouteParameters(context.Request.RouteValues);
        var queryParameters = FormatQueryParameters(context.Request.Query);
        var statusCode = StatusCodes.Status500InternalServerError;

        try
        {
            await next(context);
            statusCode = context.Response.StatusCode;
        }
        finally
        {
            var login = metadata.IdentitySource == UserActionIdentitySource.SuccessfulLogin
                ? context.Items[UserActionLogging.SuccessfulLoginKey] as string
                : authenticatedLogin;
            if (!UserActionLogging.IsSuppressed(context) && !string.IsNullOrWhiteSpace(login))
                logger.Info("User action: {Login} {Method} {Endpoint}; route={RouteParameters}; query={QueryParameters}; status={StatusCode}.",
                    login, method, endpoint, routeParameters, queryParameters, statusCode);
        }

    }

    private static string FormatRouteParameters(RouteValueDictionary values) => Limit(string.Join("&", values
        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
        .Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(
            Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? string.Empty))));

    private static string FormatQueryParameters(IQueryCollection query) => Limit(string.Join("&", query
        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
        .Select(pair => Uri.EscapeDataString(pair.Key) + "=" + FormatQueryValue(pair.Key, pair.Value))));

    private static string FormatQueryValue(string key, StringValues values) => IsSensitive(key)
        ? Redacted
        : string.Join(",", values.Select(value => Uri.EscapeDataString(value ?? string.Empty)));

    private static bool IsSensitive(string key) =>
        key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("apikey", StringComparison.OrdinalIgnoreCase);

    private static string Limit(string value) => value.Length <= MaximumParameterLength
        ? value
        : value[..MaximumParameterLength] + "...";
}
