using System.Collections.Concurrent;
using System.Text.Json;
using FlyrTech.Core;
using FlyrTech.Core.Models;

namespace FlyrTech.Infrastructure;

/// <summary>
/// Journey service implementation with INTENTIONAL race condition issues
/// This implementation reads the entire journey, modifies it, and writes it back
/// causing race conditions when multiple concurrent updates occur
/// </summary>
public class JourneyService : IJourneyService
{
    private readonly ICacheService _cacheService;
    private const string JourneyKeyPrefix = "journey:";
    private const string JourneyIdsKey = "journey:ids";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();

    private static SemaphoreSlim GetSemaphore(string journeyId) =>
        _semaphores.GetOrAdd(journeyId, _ => new SemaphoreSlim(1, 1));

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

    /// <summary>
    /// INTENTIONAL RACE CONDITION:
    /// This method reads the entire journey, modifies a segment, and writes back the entire journey.
    /// When called concurrently, updates can overwrite each other, causing data loss.
    /// </summary>
    public async Task<bool> UpdateSegmentStatusAsync(string journeyId, string segmentId, string newStatus)
    {
        if (string.IsNullOrWhiteSpace(journeyId))
            throw new ArgumentException("Journey ID cannot be null or empty", nameof(journeyId));

        if (string.IsNullOrWhiteSpace(segmentId))
            throw new ArgumentException("Segment ID cannot be null or empty", nameof(segmentId));

        var semaphore = GetSemaphore(journeyId);
        await semaphore.WaitAsync();
        try
        {
            // STEP 1: Read the entire journey from cache
            var journey = await GetJourneyAsync(journeyId);

            if (journey == null)
                return false;

            // STEP 2: Find and modify the segment
            var segment = journey.Segments.FirstOrDefault(s => s.SegmentId == segmentId);

            if (segment == null)
                return false;

            // Simulate some processing time to increase the chance of race conditions
            await Task.Delay(10);

            segment.Status = newStatus;

            // STEP 3: Write the entire journey back to cache
            var key = GetJourneyKey(journeyId);
            var json = JsonSerializer.Serialize(journey);
            await _cacheService.SetAsync(key, json);
        }
        finally
        {
            semaphore.Release();
        }

        return true;
    }

    public async Task<bool> UpdateJourneyStatusAsync(string journeyId, string newStatus)
    {
        if (string.IsNullOrWhiteSpace(journeyId))
            throw new ArgumentException("Journey ID cannot be null or empty", nameof(journeyId));

        var semaphore = GetSemaphore(journeyId);
        await semaphore.WaitAsync();
        try
        {
            var journey = await GetJourneyAsync(journeyId);

            if (journey == null)
                return false;

            journey.Status = newStatus;

            var key = GetJourneyKey(journeyId);
            var json = JsonSerializer.Serialize(journey);
            await _cacheService.SetAsync(key, json);
        }
        finally
        {
            semaphore.Release();
        }
        
        return true;
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

        // Store each journey
        foreach (var journey in journeys)
        {
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
