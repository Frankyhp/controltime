namespace ControlTime.Infrastructure;

public static class ControlTimeStorage
{
    public const string DatabaseFileName = "controltime.db";

    public static string DatabaseDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ControlTime");

    public static string LegacyDatabaseDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ControlTime");

    public static string DatabasePath => Path.Combine(DatabaseDirectory, DatabaseFileName);

    public static string GetConnectionString(string? databasePath = null)
    {
        var path = databasePath ?? DatabasePath;
        return new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = path,
            DefaultTimeout = 5,
        }.ToString();
    }

    public static void EnsureDatabaseDirectory()
    {
        Directory.CreateDirectory(DatabaseDirectory);
    }

    public static bool MigrateLegacyDatabase()
    {
        if (File.Exists(DatabasePath))
        {
            return false;
        }

        string legacyPath = Path.Combine(LegacyDatabaseDirectory, DatabaseFileName);

        if (!File.Exists(legacyPath))
        {
            return false;
        }

        EnsureDatabaseDirectory();

        try
        {
            foreach (string suffix in new[] { "-wal", "-shm" })
            {
                string legacySidecar = legacyPath + suffix;

                if (File.Exists(legacySidecar))
                {
                    File.Move(legacySidecar, DatabasePath + suffix);
                }
            }

            File.Move(legacyPath, DatabasePath);
            return true;
        }
        catch (IOException) when (File.Exists(DatabasePath))
        {
            // Another process (App or Service) completed the move concurrently.
            return false;
        }
    }
}
