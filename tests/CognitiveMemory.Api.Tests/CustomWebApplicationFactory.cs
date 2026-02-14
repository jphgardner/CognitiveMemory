using CognitiveMemory.Application.Contracts;
using CognitiveMemory.Application.Services;
using CognitiveMemory.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CognitiveMemory.Api.Tests;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMemoryService>();
            services.AddSingleton<IMemoryService, FakeMemoryService>();
        });
    }

    private sealed class FakeMemoryService : IMemoryService
    {
        public Task<MemoryHealthResponse> GetHealthAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new MemoryHealthResponse
            {
                Database = "ok",
                Cache = "ok",
                CacheLatencyMs = 1,
                Model = "ok",
                ModelProvider = "InMemory"
            });
        }

        public Task<IReadOnlyList<ClaimListItem>> GetClaimsAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<ClaimListItem> claims =
            [
                new()
                {
                    ClaimId = Guid.NewGuid(),
                    Predicate = "selected_transport",
                    Confidence = 0.82,
                    Status = ClaimStatus.Active,
                    EvidenceCount = 1
                }
            ];

            return Task.FromResult(claims);
        }

        public Task<ClaimCreatedResponse> CreateClaimAsync(CreateClaimRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ClaimCreatedResponse
            {
                ClaimId = Guid.NewGuid(),
                SubjectEntityId = request.SubjectEntityId,
                Predicate = request.Predicate,
                LiteralValue = request.LiteralValue,
                ObjectEntityId = request.ObjectEntityId,
                ValueType = request.ValueType,
                Confidence = request.Confidence,
                Status = ClaimStatus.Active,
                Scope = request.Scope,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        public Task<ClaimLifecycleResponse> SupersedeClaimAsync(Guid claimId, Guid replacementClaimId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ClaimLifecycleResponse
            {
                ClaimId = claimId,
                Status = ClaimStatus.Superseded,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        public Task<ClaimLifecycleResponse> RetractClaimAsync(Guid claimId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ClaimLifecycleResponse
            {
                ClaimId = claimId,
                Status = ClaimStatus.Retracted,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        public Task<IngestDocumentResponse> IngestDocumentAsync(IngestDocumentRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new IngestDocumentResponse
            {
                DocumentId = Guid.NewGuid(),
                Status = "Queued",
                ClaimsCreated = 1
            });
        }

        public Task<QueryClaimsResponse> QueryClaimsAsync(QueryClaimsRequest request, string requestId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new QueryClaimsResponse
            {
                Claims =
                [
                    new QueryClaimItem
                    {
                        ClaimId = Guid.NewGuid(),
                        Predicate = "selected_transport",
                        LiteralValue = "SignalR",
                        Score = 0.89,
                        Confidence = 0.82,
                        Evidence =
                        [
                            new QueryEvidenceItem
                            {
                                EvidenceId = Guid.NewGuid(),
                                SourceType = "ChatTurn",
                                SourceRef = "conv:1/turn:2",
                                Strength = 0.78
                            }
                        ],
                        Contradictions = []
                    }
                ],
                Meta = new QueryMeta
                {
                    Strategy = "hybrid",
                    LatencyMs = 12,
                    RequestId = requestId,
                    UncertaintyFlags = []
                }
            });
        }

        public Task<AnswerQuestionResponse> AnswerAsync(AnswerQuestionRequest request, string requestId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AnswerQuestionResponse
            {
                Answer = "Based on available evidence, it appears that the selected transport is SignalR.",
                Confidence = 0.82,
                Citations =
                [
                    new AnswerCitation
                    {
                        ClaimId = Guid.NewGuid(),
                        EvidenceId = Guid.NewGuid()
                    }
                ],
                UncertaintyFlags = [],
                Contradictions = [],
                Conscience = new AnswerConscience
                {
                    Decision = "Approve",
                    RiskScore = 0.12,
                    PolicyVersion = "policy-2026-02-13"
                },
                RequestId = requestId
            });
        }
    }
}
