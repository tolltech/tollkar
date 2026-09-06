using Microsoft.AspNetCore.Http.Extensions;
using QRCoder;

namespace Tollkar.Web.Authentication;

/// <summary>Builds the shareable links that guest access and device pairing render as QR codes.</summary>
internal static class AccessLinks
{
    public static string Absolute(HttpRequest request, string path) =>
        UriHelper.BuildAbsolute(request.Scheme, request.Host, request.PathBase, path);

    public static IResult QrCode(string url)
    {
        using var data = QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.M);
        using var code = new SvgQRCode(data);
        return Results.Text(code.GetGraphic(5), "image/svg+xml");
    }
}
