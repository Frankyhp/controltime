namespace ControlTime.App.Presentation;

public static class DomainErrorTranslator
{
    private static readonly Dictionary<string, string> Messages = new(StringComparer.Ordinal)
    {
        ["Rule name must not be empty."] =
            "El nombre de la regla no puede estar vacío.",
        ["Rule must allow at least one day of the week."] =
            "La regla debe permitir al menos un día de la semana.",
        ["Daily limit must be positive when specified."] =
            "El límite diario debe ser positivo.",
        ["Schedule start must be earlier than schedule end. Schedules crossing midnight are not supported."] =
            "La hora de inicio debe ser anterior a la de fin. No se admiten horarios que crucen la medianoche.",
    };

    public static string Translate(string? error)
        => error is not null && Messages.TryGetValue(error, out var localized)
            ? localized
            : error ?? "Ha ocurrido un error desconocido.";
}
