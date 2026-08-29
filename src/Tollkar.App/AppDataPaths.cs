namespace Tollkar.App;

internal static class AppDataPaths
{
    public static string LibraryDatabase
    {
        get
        {
            var specialFolder = OperatingSystem.IsMacOS()
                ? Environment.SpecialFolder.ApplicationData
                : Environment.SpecialFolder.LocalApplicationData;
            var dataDirectory = Environment.GetFolderPath(specialFolder);
            return Path.Combine(dataDirectory, "Tollkar", "library.db");
        }
    }
}
