using FlyrTech.Core;
using StackExchange.Redis;

namespace FlyrTech.Infrastructure;

/// <summary>
/// Redis implementation of the cache service using StackExchange.Redis
/// </summary>
public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly IDatabase _database;

    /// <summary>
    /// Constructor with Redis connection multiplexer injection
    /// </summary>
    /// <param name="connectionMultiplexer">The Redis connection multiplexer</param>
    public RedisCacheService(IConnectionMultiplexer connectionMultiplexer)
    {
        _connectionMultiplexer = connectionMultiplexer ?? throw new ArgumentNullException(nameof(connectionMultiplexer));
        _database = _connectionMultiplexer.GetDatabase();
    }

    /// <inheritdoc/>
    public async Task<string?> GetAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        var value = await _database.StringGetAsync(key);
        return value.HasValue ? value.ToString() : null;
    }

    /// <inheritdoc/>
    public async Task SetAsync(string key, string value, TimeSpan? expiration = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        if (value == null)
            throw new ArgumentNullException(nameof(value));

        if (expiration.HasValue)
        {
            await _database.StringSetAsync(key, value, expiration.Value);
        }
        else
        {
            await _database.StringSetAsync(key, value);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        return await _database.KeyDeleteAsync(key);
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        return await _database.KeyExistsAsync(key);
    }

    /// <inheritdoc/>
    public async Task<bool> CompareAndSetAsync(string key, int expectedVersion, string newValue)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));
        if (newValue == null)
            throw new ArgumentNullException(nameof(newValue));

        // Lua script: atomically check version and set new value
        // This runs as a single atomic operation in Redis
        const string luaScript = @"
            local current = redis.call('GET', KEYS[1])
            if current == false then
                return 0
            end
            local data = cjson.decode(current)
            if data['Version'] ~= tonumber(ARGV[1]) then
                return 0
            end
            redis.call('SET', KEYS[1], ARGV[2])
            return 1";

        var result = await _database.ScriptEvaluateAsync(
            luaScript,
            new RedisKey[] { key },
            new RedisValue[] { expectedVersion, newValue });

        return (int)result == 1;
    }
}
