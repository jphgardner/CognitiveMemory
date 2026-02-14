using CognitiveMemory.Application.AI;
using CognitiveMemory.Application.Contracts;
using CognitiveMemory.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;

namespace CognitiveMemory.Application.Tests;

public sealed class DebateOrchestratorTests
{
    [Fact]
    public async Task DebateReturnsInsufficientEvidenceWhenNoClaims()
    {
        var orchestrator = new SemanticKernelDebateOrchestrator(new FakeKernelFactory(), NullLogger<SemanticKernelDebateOrchestrator>.Instance);

        var result = await orchestrator.OrchestrateAsync("What did we decide?", new QueryClaimsResponse(), CancellationToken.None);

        Assert.Equal(0, result.Confidence);
        Assert.Contains("InsufficientEvidence", result.UncertaintyFlags);
    }

    [Fact]
    public async Task DebateIncludesCitationsForFactualOutput()
    {
        var orchestrator = new SemanticKernelDebateOrchestrator(new FakeKernelFactory(), NullLogger<SemanticKernelDebateOrchestrator>.Instance);

        var packet = new QueryClaimsResponse
        {
            Claims =
            [
                new QueryClaimItem
                {
                    ClaimId = Guid.NewGuid(),
                    Predicate = "selected_transport",
                    LiteralValue = "SignalR",
                    Confidence = 0.82,
                    Score = 0.9,
                    Evidence =
                    [
                        new QueryEvidenceItem
                        {
                            EvidenceId = Guid.NewGuid(),
                            SourceType = "ChatTurn",
                            SourceRef = "conv:1/turn:2",
                            Strength = 0.8
                        }
                    ],
                    Contradictions = []
                }
            ],
            Meta = new QueryMeta { UncertaintyFlags = [] }
        };

        var result = await orchestrator.OrchestrateAsync("What did we decide?", packet, CancellationToken.None);

        Assert.NotEmpty(result.Citations);
    }

    private sealed class FakeKernelFactory : IMemoryKernelFactory
    {
        public Kernel CreateKernel()
        {
            return Kernel.CreateBuilder().Build();
        }
    }
}
