using CognitiveMemory.Infrastructure.Subconscious;
using Xunit;

namespace CognitiveMemory.Api.Tests;

public sealed class SubconsciousGroupChatManagerTests
{
    [Fact]
    public void ShouldRequestUserInput_ReturnsFalse_ForProtectedDowngrade()
    {
        var manager = new SubconsciousGroupChatManager(
            new SubconsciousDebateOptions
            {
                RequireHumanApprovalForProtectedIdentity = true,
                AllowAutomaticProtectedIdentityDowngrade = true,
                ProtectedIdentityKeys = ["identity.birth_datetime"]
            });

        var outcome = new SubconsciousDebateOutcome(
            DecisionType: "refine",
            FinalConfidence: 0.82,
            ReasoningSummary: "downgrade certainty",
            EvidenceRefs: [],
            ClaimsToCreate: [],
            ClaimsToSupersede: [],
            Contradictions: [],
            ProceduralUpdates: [],
            SelfUpdates:
            [
                new SubconsciousDebateSelfUpdate(
                    Key: "identity.birth_datetime",
                    Value: "UNVERIFIED_USER_ASSERTION_RECLASSIFY_TO_PERSONA",
                    Confidence: 0.9,
                    RequiresConfirmation: false)
            ],
            RequiresUserInput: true,
            UserQuestion: "confirm?");

        var result = manager.ShouldRequestUserInput(outcome);

        Assert.False(result);
    }
}
