namespace Tollkar.Core;

internal static class Guard
{
    public static string NotNullOrWhiteSpace(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty or whitespace.", parameterName);
        }

        return value;
    }

    public static string? NullOrNotWhiteSpace(string? value, string parameterName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be whitespace.", parameterName);
        }

        return value;
    }
}
