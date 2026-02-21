namespace CognitiveMemory.Application.Identity;

public sealed class IdentityEvolutionOptions
{
    public int LookbackDays { get; set; } = 45;
    public int MaxEpisodesScanned { get; set; } = 700;
    public int MinSignalOccurrences { get; set; } = 3;
    public string FocusPreferenceKey { get; set; } = "identity.project_focus";
    public string StylePreferenceKey { get; set; } = "identity.collaboration_style";
    public string GoalPreferenceKey { get; set; } = "identity.long_term_goal";
}
