namespace CognitiveMemory.Application.Cognitive;

public interface ICompanionCognitiveProfileService
{
    Task<CompanionCognitiveProfileState> GetStateAsync(Guid companionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompanionCognitiveProfileVersion>> GetVersionsAsync(Guid companionId, int take = 50, CancellationToken cancellationToken = default);
    Task<CompanionCognitiveProfileValidationResult> ValidateAsync(CompanionCognitiveProfileDocument profile, CancellationToken cancellationToken = default);
    Task<CompanionCognitiveProfileVersion> CreateVersionAsync(CreateCompanionCognitiveProfileVersionRequest request, CancellationToken cancellationToken = default);
    Task<CompanionCognitiveProfileState> StageAsync(StageCompanionCognitiveProfileRequest request, CancellationToken cancellationToken = default);
    Task<CompanionCognitiveProfileState> ActivateAsync(ActivateCompanionCognitiveProfileRequest request, CancellationToken cancellationToken = default);
    Task<CompanionCognitiveProfileState> RollbackAsync(RollbackCompanionCognitiveProfileRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompanionCognitiveProfileAudit>> GetAuditsAsync(Guid companionId, int take = 100, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompanionCognitiveRuntimeTrace>> GetRuntimeTracesAsync(Guid companionId, int take = 200, CancellationToken cancellationToken = default);
    Task<SimulateCompanionCognitiveProfileResult> SimulateAsync(SimulateCompanionCognitiveProfileRequest request, CancellationToken cancellationToken = default);
}
