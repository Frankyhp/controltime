namespace ControlTime.App.Presentation;

public static class AccessReasonTranslator
{
    private static readonly Dictionary<string, string> translations = new(StringComparer.Ordinal)
    {
        ["No usage rule is enabled."] = "No hay ninguna regla de uso activada.",
        ["The daily usage limit has been reached."] = "Se ha alcanzado el límite diario de uso.",
        ["The current time is outside the allowed schedule."] = "La hora actual está fuera del horario permitido.",
        ["Today is not an allowed day."] = "Hoy no es un día permitido.",
    };

    public static string Translate(string? reason) => reason is null
        ? string.Empty
        : translations.GetValueOrDefault(reason, reason);
}
