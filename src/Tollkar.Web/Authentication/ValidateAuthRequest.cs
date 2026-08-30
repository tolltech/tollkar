using Microsoft.AspNetCore.Antiforgery;

namespace Tollkar.Web.Authentication;

public sealed class ValidateAuthRequest(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.Arguments.Any(argument => argument is null))
            return Results.ValidationProblem(new Dictionary<string, string[]>
                { ["Request"] = ["Ожидается корректный JSON-запрос."] });

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
                { ["Csrf"] = ["Обновите страницу и повторите запрос."] });
        }

        var credentials = context.Arguments.OfType<Credentials>().FirstOrDefault();
        if (credentials is not null)
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(credentials.Login) || credentials.Login.Length > 256)
                errors["Login"] = ["Укажите логин длиной от 1 до 256 символов."];
            if (string.IsNullOrEmpty(credentials.Password) || credentials.Password.Length > 1024)
                errors["Password"] = ["Укажите пароль длиной от 1 до 1024 символов."];
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);
        }

        return await next(context);
    }
}
