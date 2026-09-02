using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tollkar.Web.Persistence;

namespace Tollkar.Web.Authentication;

public static class AuthenticationServices
{
    public static void AddWebAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddDbContext<WebDbContext>((services, options) =>
        {
            var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("WebDatabase")
                ?? throw new InvalidOperationException("ConnectionStrings:WebDatabase is required.");
            options.UseSqlite(connectionString);
        });
        builder.Services.AddIdentityCore<TollkarUser>()
            .AddEntityFrameworkStores<WebDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();
        var authentication = builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme);
        authentication.AddIdentityCookies();
        authentication.AddCookie(GuestAccess.AuthenticationScheme, options => ConfigureCookie(options, builder));
        builder.Services.AddSingleton<GuestAccess>();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            ConfigureCookie(options, builder);
            options.Cookie.Name = "Tollkar.Auth";
        });
        builder.Services.AddAuthorization(options =>
        {
            var authenticated = new AuthorizationPolicyBuilder(
                    IdentityConstants.ApplicationScheme, GuestAccess.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build();
            options.DefaultPolicy = authenticated;
            options.FallbackPolicy = authenticated;
            options.AddPolicy(AdministratorAccount.PolicyName, policy =>
            {
                policy.AuthenticationSchemes.Add(IdentityConstants.ApplicationScheme);
                policy.RequireAssertion(context => AdministratorAccount.IsAdministrator(context.User));
            });
        });
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
        });
    }

    private static void ConfigureCookie(CookieAuthenticationOptions options, WebApplicationBuilder builder)
    {
        options.Cookie.Name = "Tollkar.Guest";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.Events.OnRedirectToLogin = context => SetStatus(context, StatusCodes.Status401Unauthorized);
        options.Events.OnRedirectToAccessDenied = context => SetStatus(context, StatusCodes.Status403Forbidden);
    }

    private static Task SetStatus(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        context.Response.StatusCode = status;
        return Task.CompletedTask;
    }
}
