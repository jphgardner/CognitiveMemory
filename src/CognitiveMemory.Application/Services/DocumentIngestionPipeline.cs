using System.Text.Json;
using CognitiveMemory.Application.AI;
using CognitiveMemory.Application.AI.Tooling;
using CognitiveMemory.Application.Contracts;
using CognitiveMemory.Application.Interfaces;
using CognitiveMemory.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CognitiveMemory.Application.Services;

public sealed class DocumentIngestionPipeline(
    IClaimExtractionEngine claimExtractionEngine,
    IClaimRepository claimRepository,
    IEntityRepository entityRepository,
    IOutboxRepository? outboxRepository,
    ILogger<DocumentIngestionPipeline> logger) : IDocumentIngestionPipeline
{
    public async Task<int> ProcessDocumentAsync(SourceDocument document, CancellationToken cancellationToken)
    {
        var metadata = ParseMetadata(document.Metadata);
        var normalized = await claimExtractionEngine.NormalizeAsync(document.Content, cancellationToken);
        var extractedClaims = await claimExtractionEngine.ExtractAsync(
            normalized,
            new ClaimExtractionContext
            {
                SourceType = document.SourceType,
                SourceRef = document.SourceRef,
                Metadata = metadata
            },
            cancellationToken);

        logger.LogInformation(
            "Claim extraction completed for document {DocumentId}. Extracted {ClaimCount} candidate claims.",
            document.DocumentId,
            extractedClaims.Count);

        var createdClaims = 0;
        foreach (var extracted in extractedClaims)
        {
            if (string.IsNullOrWhiteSpace(extracted.Predicate))
            {
                continue;
            }

            var subject = ResolveSubject(document.SourceRef, metadata, extracted);
            if (string.IsNullOrWhiteSpace(subject.Key))
            {
                continue;
            }

            var subjectId = MemoryIdentity.ComputeStableGuid(subject.Key);
            await entityRepository.UpsertAsync(
                entityId: subjectId,
                type: subject.Type,
                name: subject.Name,
                aliases: subject.Aliases,
                metadata: JsonSerializer.Serialize(new
                {
                    sourceType = document.SourceType,
                    sourceRef = document.SourceRef,
                    subjectKey = subject.Key,
                    actorRole = metadata.GetValueOrDefault("actorRole"),
                    actorKey = metadata.GetValueOrDefault("actorKey"),
                    model = metadata.GetValueOrDefault("model")
                }),
                cancellationToken: cancellationToken);

            var claimHash = MemoryIdentity.ComputeClaimHash(subject.Key, extracted.Predicate, extracted.LiteralValue);
            var duplicate = await claimRepository.GetByHashAsync(claimHash, cancellationToken);
            if (duplicate is not null)
            {
                continue;
            }

            var createClaimRequest = new CreateClaimRequest
            {
                SubjectEntityId = subjectId,
                Predicate = extracted.Predicate,
                LiteralValue = extracted.LiteralValue,
                ValueType = "String",
                Confidence = extracted.Confidence,
                Scope = document.Metadata,
                Hash = claimHash,
                Evidence =
                [
                    new CreateEvidenceRequest
                    {
                        SourceType = document.SourceType,
                        SourceRef = document.SourceRef,
                        ExcerptOrSummary = string.IsNullOrWhiteSpace(extracted.EvidenceSummary)
                            ? document.Content
                            : extracted.EvidenceSummary,
                        Strength = Math.Clamp(extracted.Confidence, 0.1, 1.0),
                        CapturedAt = document.CapturedAt,
                        Metadata = document.Metadata
                    }
                ]
            };

            var created = await claimRepository.CreateAsync(createClaimRequest, cancellationToken);
            createdClaims++;

            if (outboxRepository is not null)
            {
                await outboxRepository.EnqueueAsync(new OutboxEventWriteRequest
                {
                    EventType = OutboxEventTypes.MemoryClaimCreated,
                    AggregateType = OutboxAggregateTypes.Claim,
                    AggregateId = created.ClaimId,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        claimId = created.ClaimId,
                        predicate = created.Predicate,
                        confidence = created.Confidence,
                        sourceRef = document.SourceRef
                    }),
                    IdempotencyKey = $"{OutboxEventTypes.MemoryClaimCreated}:{claimHash}"
                }, cancellationToken);
            }
        }

        return createdClaims;
    }

    private static Dictionary<string, string> ParseMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson);
            return parsed ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static SubjectResolution ResolveSubject(
        string sourceRef,
        IReadOnlyDictionary<string, string> metadata,
        ExtractedClaim extracted)
    {
        var actorRole = metadata.GetValueOrDefault("actorRole");
        var actorKey = metadata.GetValueOrDefault("actorKey");
        var actorName = metadata.GetValueOrDefault("actorName");

        var subjectKey = FirstNonEmpty(extracted.SubjectKey, actorKey, sourceRef);
        var subjectType = NormalizeEntityType(FirstNonEmpty(extracted.SubjectType, actorRole));
        var subjectName = FirstNonEmpty(extracted.SubjectName, actorName, subjectKey);
        var aliases = BuildAliases(extracted.SubjectName, actorName, subjectKey, subjectName);

        return new SubjectResolution(subjectKey, subjectType, subjectName, aliases);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string NormalizeEntityType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Concept";
        }

        var normalized = value.Trim().ToLowerInvariant();
        var canonical = normalized switch
        {
            "user" => "Person",
            "person" => "Person",
            "assistant" => "Agent",
            "agent" => "Agent",
            "model" => "Agent",
            "organization" => "Organization",
            "org" => "Organization",
            "place" => "Place",
            "location" => "Place",
            _ => value.Trim()
        };

        return canonical.Length <= 32 ? canonical : canonical[..32];
    }

    private static IReadOnlyList<string> BuildAliases(
        string? extractedSubjectName,
        string? actorName,
        string subjectKey,
        string canonicalName)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in new[] { extractedSubjectName, actorName, subjectKey })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var trimmed = value.Trim();
            if (!string.Equals(trimmed, canonicalName, StringComparison.OrdinalIgnoreCase))
            {
                aliases.Add(trimmed);
            }
        }

        return aliases.ToList();
    }

    private sealed record SubjectResolution(
        string Key,
        string Type,
        string Name,
        IReadOnlyList<string> Aliases);
}
