namespace Tollkar.Web.Authentication;

// A class deliberately avoids the record-generated ToString exposing the password.
public sealed class Credentials
{
    public string? Login { get; init; }
    public string? Password { get; init; }
}
