using System.Text.Json;
using FlyrTech.Core;
using FlyrTech.Core.Models;

namespace FlyrTech.Infrastructure;

/// <summary>
/// Journey service using optimistic concurrency control with versioning.
/// Instead of locking, we detect conflicts at write time and retry on version mismatch.
/// </summary>
public class JourneyService : IJourneyService
{
    private readonly ICacheService _cacheService;
    private const string JourneyKeyPrefix = "journey:";
    private const string JourneyIdsKey = "journey:ids";
    private const int MaxRetries = 50;

    public JourneyService(ICacheService cacheService)
    {
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
    }

    public async Task<Journey?> GetJourneyAsync(string journeyId)
    {
        if (string.IsNullOrWhiteSpace(journeyId))
            throw new ArgumentException("Journey ID cannot be null or empty", nameof(journeyId));

        var key = GetJourneyKey(journeyId);
        var json = await _cacheService.GetAsync(key);

        if (json == null)
            return null;

        return JsonSerializer.Deserialize<Journey>(json);
    }

    public async Task<bool> UpdateSegmentStatusAsync(string journeyId, string segmentId, string newStatus)
    {
        if (string.IsNullOrWhiteSpace(journeyId))
            throw new ArgumentException("Journey ID cannot be null or empty", nameof(journeyId));

        if (string.IsNullOrWhiteSpace(segmentId))
            throw new ArgumentException("Segment ID cannot be null or empty", nameof(segmentId));

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            // STEP 1: Read journey + its current version
            var key = GetJourneyKey(journeyId);
            var json = await _cacheService.GetAsync(key);
            if (json == null) return false;

            var journey = JsonSerializer.Deserialize<Journey>(json);
            if (journey == null) return false;

            var originalVersion = journey.Version;

            // STEP 2: Find and modify the segment
            var segment = journey.Segments.FirstOrDefault(s => s.SegmentId == segmentId);
            if (segment == null) return false;

            segment.Status = newStatus;
            journey.Version++;

            // STEP 3: Conditional write — only if version still matches
            var updated = await _cacheService.CompareAndSetAsync(
                key,
                originalVersion,
                JsonSerializer.Serialize(journey));

            if (updated)
                return true;

            // Version mismatch — another thread wrote first, retry
        }

        return false;
    }

    public async Task<bool> UpdateJourneyStatusAsync(string journeyId, string newStatus)
    {
        if (string.IsNullOrWhiteSpace(journeyId))
            throw new ArgumentException("Journey ID cannot be null or empty", nameof(journeyId));

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            var key = GetJourneyKey(journeyId);
            var json = await _cacheService.GetAsync(key);
            if (json == null) return false;

            var journey = JsonSerializer.Deserialize<Journey>(json);
            if (journey == null) return false;

            var originalVersion = journey.Version;

            journey.Status = newStatus;
            journey.Version++;

            var updated = await _cacheService.CompareAndSetAsync(
                key,
                originalVersion,
                JsonSerializer.Serialize(journey));

            if (updated)
                return true;
        }

        return false;
    }

    public async Task<List<string>> GetAllJourneyIdsAsync()
    {
        var json = await _cacheService.GetAsync(JourneyIdsKey);

        if (json == null)
            return new List<string>();

        return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
    }

    public async Task InitializeCacheAsync(List<Journey> journeys)
    {
        if (journeys == null || journeys.Count == 0)
            return;

        // Store each journey with version 0
        foreach (var journey in journeys)
        {
            journey.Version = 0;
            var key = GetJourneyKey(journey.Id);
            var json = JsonSerializer.Serialize(journey);
            await _cacheService.SetAsync(key, json);
        }

        // Store the list of journey IDs
        var journeyIds = journeys.Select(j => j.Id).ToList();
        var idsJson = JsonSerializer.Serialize(journeyIds);
        await _cacheService.SetAsync(JourneyIdsKey, idsJson);
    }

    private static string GetJourneyKey(string journeyId) => $"{JourneyKeyPrefix}{journeyId}";
}
