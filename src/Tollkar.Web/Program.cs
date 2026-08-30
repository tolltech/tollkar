using Tollkar.Web.Authentication;
using Tollkar.Web.Catalog;
using Tollkar.Application.Library;

var builder = WebApplication.CreateBuilder(args);
builder.AddWebAuthentication();
builder.AddCatalog();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
var app = builder.Build();
await app.Services.GetRequiredService<ILibraryService>().InitializeAsync();

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
        context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
app.UseStatusCodePages(async context =>
{
    if (context.HttpContext.Request.Path.StartsWithSegments("/api") &&
        context.HttpContext.Response.StatusCode is 400 or 415)
        await Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["Request"] = ["Ожидается корректный JSON-запрос."]
        }, statusCode: context.HttpContext.Response.StatusCode).ExecuteAsync(context.HttpContext);
});
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapAuthEndpoints();
app.MapCatalogEndpoints();
app.Map("/api/{**path}", () => Results.NotFound()).AllowAnonymous();
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

public partial class Program;
