using CognitiveMemory.Infrastructure.Subconscious;
using Xunit;

namespace CognitiveMemory.Api.Tests;

public sealed class SubconsciousOutcomeValidatorTests
{
    [Fact]
    public void Validate_ReturnsRejected_ForMalformedJson()
    {
        var validator = new SubconsciousOutcomeValidator(new SubconsciousDebateOptions());

        var result = validator.Validate("{ this is invalid json }");

        Assert.False(result.IsValid);
        Assert.Equal("Rejected", result.Status);
    }

    [Fact]
    public void Validate_ReturnsNeedsUserConfirmation_ForProtectedIdentityUpdate()
    {
        var validator = new SubconsciousOutcomeValidator(
            new SubconsciousDebateOptions
            {
                RequireHumanApprovalForProtectedIdentity = true,
                ProtectedIdentityKeys = ["identity.name"]
            });

        const string outcomeJson = """
                                   {
                                     "decisionType": "identity_update",
                                     "finalConfidence": 0.9,
                                     "reasoningSummary": "identity update",
                                     "evidenceRefs": [],
                                     "claimsToCreate": [],
                                     "claimsToSupersede": [],
                                     "contradictions": [],
                                     "proceduralUpdates": [],
                                     "selfUpdates": [
                                       {
                                         "key": "identity.name",
                                         "value": "Feather",
                                         "confidence": 0.9,
                                         "requiresConfirmation": true
                                       }
                                     ],
                                     "requiresUserInput": false,
                                     "userQuestion": null
                                   }
                                   """;

        var result = validator.Validate(outcomeJson);

        Assert.True(result.IsValid);
        Assert.True(result.RequiresUserInput);
        Assert.Equal("NeedsUserConfirmation", result.Status);
    }

    [Fact]
    public void Validate_ReturnsNeedsUserConfirmation_ForProtectedIdentityUpdate_EvenWithoutModelFlag()
    {
        var validator = new SubconsciousOutcomeValidator(
            new SubconsciousDebateOptions
            {
                RequireHumanApprovalForProtectedIdentity = true,
                ProtectedIdentityKeys = ["identity.name"]
            });

        const string outcomeJson = """
                                   {
                                     "decisionType": "identity_update",
                                     "finalConfidence": 0.9,
                                     "reasoningSummary": "identity update",
                                     "evidenceRefs": [],
                                     "claimsToCreate": [],
                                     "claimsToSupersede": [],
                                     "contradictions": [],
                                     "proceduralUpdates": [],
                                     "selfUpdates": [
                                       {
                                         "key": "identity.name",
                                         "value": "Feather",
                                         "confidence": 0.9,
                                         "requiresConfirmation": false
                                       }
                                     ],
                                     "requiresUserInput": false,
                                     "userQuestion": null
                                   }
                                   """;

        var result = validator.Validate(outcomeJson);

        Assert.True(result.IsValid);
        Assert.True(result.RequiresUserInput);
        Assert.Equal("NeedsUserConfirmation", result.Status);
    }

    [Fact]
    public void Validate_ReturnsValid_ForSafeOutcome()
    {
        var validator = new SubconsciousOutcomeValidator(new SubconsciousDebateOptions());

        const string outcomeJson = """
                                   {
                                     "decisionType": "refine",
                                     "finalConfidence": 0.82,
                                     "reasoningSummary": "safe",
                                     "evidenceRefs": [],
                                     "claimsToCreate": [],
                                     "claimsToSupersede": [],
                                     "contradictions": [],
                                     "proceduralUpdates": [],
                                     "selfUpdates": [],
                                     "requiresUserInput": false,
                                     "userQuestion": null
                                   }
                                   """;

        var result = validator.Validate(outcomeJson);

        Assert.True(result.IsValid);
        Assert.False(result.RequiresUserInput);
        Assert.Equal("Valid", result.Status);
    }

    [Fact]
    public void Validate_AllowsProtectedIdentityDowngradeWithoutUserInput()
    {
        var validator = new SubconsciousOutcomeValidator(
            new SubconsciousDebateOptions
            {
                RequireHumanApprovalForProtectedIdentity = true,
                AllowAutomaticProtectedIdentityDowngrade = true,
                ProtectedIdentityKeys = ["identity.birth_datetime"]
            });

        const string outcomeJson = """
                                   {
                                     "decisionType": "refine",
                                     "finalConfidence": 0.82,
                                     "reasoningSummary": "downgrade certainty",
                                     "evidenceRefs": [],
                                     "claimsToCreate": [],
                                     "claimsToSupersede": [],
                                     "contradictions": [],
                                     "proceduralUpdates": [],
                                     "selfUpdates": [
                                       {
                                         "key": "identity.birth_datetime",
                                         "value": "UNVERIFIED_USER_ASSERTION_RECLASSIFY_TO_PERSONA",
                                         "confidence": 0.9,
                                         "requiresConfirmation": false
                                       }
                                     ],
                                     "requiresUserInput": true,
                                     "userQuestion": "confirm?"
                                   }
                                   """;

        var result = validator.Validate(outcomeJson);

        Assert.True(result.IsValid);
        Assert.False(result.RequiresUserInput);
        Assert.Equal("Valid", result.Status);
        Assert.NotNull(result.Outcome);
        Assert.False(result.Outcome!.RequiresUserInput);
        Assert.Null(result.Outcome.UserQuestion);
    }

    [Fact]
    public void Validate_StillRequiresUserInput_ForProtectedIdentityAssertion()
    {
        var validator = new SubconsciousOutcomeValidator(
            new SubconsciousDebateOptions
            {
                RequireHumanApprovalForProtectedIdentity = true,
                AllowAutomaticProtectedIdentityDowngrade = true,
                ProtectedIdentityKeys = ["identity.birth_datetime"]
            });

        const string outcomeJson = """
                                   {
                                     "decisionType": "identity_update",
                                     "finalConfidence": 0.92,
                                     "reasoningSummary": "assert identity fact",
                                     "evidenceRefs": [],
                                     "claimsToCreate": [],
                                     "claimsToSupersede": [],
                                     "contradictions": [],
                                     "proceduralUpdates": [],
                                     "selfUpdates": [
                                       {
                                         "key": "identity.birth_datetime",
                                         "value": "2026-02-21T14:30:00Z",
                                         "confidence": 0.9,
                                         "requiresConfirmation": false
                                       }
                                     ],
                                     "requiresUserInput": false,
                                     "userQuestion": null
                                   }
                                   """;

        var result = validator.Validate(outcomeJson);

        Assert.True(result.IsValid);
        Assert.True(result.RequiresUserInput);
        Assert.Equal("NeedsUserConfirmation", result.Status);
    }
}
