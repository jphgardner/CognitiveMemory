namespace CognitiveMemory.Application.Identity;

public interface IIdentityEvolutionService
{
    Task<IdentityEvolutionRunResult> RunOnceAsync(CancellationToken cancellationToken = default);
}
