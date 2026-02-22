namespace CognitiveMemory.Infrastructure.Subconscious;

public interface ISubconsciousOutcomeValidator
{
    SubconsciousValidationResult Validate(string outcomeJson);
}
