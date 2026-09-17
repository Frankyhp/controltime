namespace ControlTime.Infrastructure;

public static class ControlTimeStorage
{
    public const string DatabaseFileName = "controltime.db";

    public static string DatabaseDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ControlTime");

    public static string DatabasePath => Path.Combine(DatabaseDirectory, DatabaseFileName);

    public static string GetConnectionString(string? databasePath = null)
    {
        var path = databasePath ?? DatabasePath;
        return new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = path,
        }.ToString();
    }

    public static void EnsureDatabaseDirectory()
    {
        Directory.CreateDirectory(DatabaseDirectory);
    }
}
