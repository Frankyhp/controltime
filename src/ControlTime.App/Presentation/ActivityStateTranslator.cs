using ControlTime.Core.Domain;

namespace ControlTime.App.Presentation;

public static class ActivityStateTranslator
{
    public static string Translate(ActivityState state) => state switch
    {
        ActivityState.Active => "Activo",
        ActivityState.Idle => "Ocioso",
        ActivityState.Locked => "Bloqueado",
        _ => state.ToString(),
    };
}
