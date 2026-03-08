using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Domain.DTO.Splendor;
using CleanArchitecture.Domain.Model.Splendor.System;
using StackExchange.Redis;
using System.Text.Json;
using System.Text.Json.Serialization;
using RedisDatabase = StackExchange.Redis.IDatabase;

namespace CleanArchitecture.Infrastructure.Repository
{
    public class RedisGameStateStore : IGameStateStore
    {
        private readonly RedisDatabase _db;
        private readonly IConnectionMultiplexer _redis;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            IncludeFields = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        public RedisGameStateStore(IConnectionMultiplexer redis)
        {
            _redis = redis;
            _db = redis.GetDatabase();
        }

        #region Core Methods
        public async Task SaveGameContext(string roomCode, GameContext context)
        {
            var json = JsonSerializer.Serialize(context, _jsonOptions);
            await _db.StringSetAsync($"splendor:game:{roomCode}", json);
        }

        public async Task<GameContext?> LoadGameContext(string roomCode)
        {

            var value = await _db.StringGetAsync($"splendor:game:{roomCode}");
            if (value.IsNullOrEmpty)
                return null;

            return JsonSerializer.Deserialize<GameContext>(value!, _jsonOptions);

        }

        public async Task DeleteGameContext(string roomCode)
        {
            // Batch delete — 1 round trip thay vì 5
            var keys = new RedisKey[]
            {
                $"splendor:game:{roomCode}",
                $"game:{roomCode}:info",
                $"game:{roomCode}:players",
                $"game:{roomCode}:board",
                $"game:{roomCode}:turn"
            };
            await _db.KeyDeleteAsync(keys);
        }
        #endregion

        #region Format & Helper Methods

        /// <summary>
        /// Lấy tất cả keys theo pattern và nhóm lại theo gameId
        /// </summary>
        public async Task<List<GroupedGameKeys>> GetGroupedGameKeys(string pattern = "game:*", int limit = 50)
        {
            var endpoints = _redis.GetEndPoints();
            var server = _redis.GetServer(endpoints.First());

            var keys = server.Keys(pattern: pattern, pageSize: 100)
                            .Take(limit)
                            .Select(k => k.ToString())
                            .ToList();

            var grouped = keys
                .Select(key => {
                    var parts = key.Split(':');
                    return new
                    {
                        GameId = parts.Length > 1 ? parts[1] : "",
                        Type = parts.Length > 2 ? parts[2] : "",
                        FullKey = key
                    };
                })
                .Where(x => !string.IsNullOrEmpty(x.GameId))
                .GroupBy(x => x.GameId)
                .Select(g => new GroupedGameKeys
                {
                    GameId = g.Key,
                    Keys = g.Select(x => x.Type).ToList(),
                    FullKeys = g.Select(x => x.FullKey).ToList()
                })
                .ToList();

            return grouped;
        }

        /// <summary>
        /// Lấy tất cả data của game và format đẹp
        /// </summary>
        public async Task<List<FormattedGameData>> GetFormattedGamesData(string pattern = "game:*", int limit = 50)
        {
            var groupedGames = await GetGroupedGameKeys(pattern, limit);
            var result = new List<FormattedGameData>();

            foreach (var game in groupedGames)
            {
                var gameData = new FormattedGameData
                {
                    GameId = game.GameId,
                    //Keys = game.Keys,
                    Data = new Dictionary<string, object>()
                };

                foreach (var fullKey in game.FullKeys)
                {
                    try
                    {
                        var keyName = fullKey.Split(':').Last();
                        var value = await GetFormattedValue(fullKey);
                        gameData.Data[keyName] = value;
                    }
                    catch (Exception ex)
                    {
                        gameData.Data[fullKey.Split(':').Last()] = $"Error: {ex.Message}";
                    }
                }

                result.Add(gameData);
            }

            return result;
        }

        /// <summary>
        /// Lấy tất cả data của 1 game theo gameId
        /// </summary>
        public async Task<FormattedGameData?> GetGameDataById(string gameId)
        {
            var games = await GetFormattedGamesData($"game:{gameId}:*", 100);
            return games.FirstOrDefault();
        }

        /// <summary>
        /// Kiểm tra game có tồn tại không
        /// </summary>
        public async Task<bool> GameExists(string gameId)
        {
            var endpoints = _redis.GetEndPoints();
            var server = _redis.GetServer(endpoints.First());
            var keys = server.Keys(pattern: $"game:{gameId}:*", pageSize: 1);
            return keys.Any();
        }

        /// <summary>
        /// Lấy value từ Redis và tự động format theo type
        /// </summary>
        public async Task<object> GetFormattedValue(string key)
        {
            var keyType = await _db.KeyTypeAsync(key);

            return keyType switch
            {
                RedisType.String => await GetFormattedStringValue(key),
                RedisType.Hash => await GetFormattedHashValue(key),
                RedisType.List => await GetFormattedListValue(key),
                RedisType.Set => await GetFormattedSetValue(key),
                RedisType.SortedSet => await GetFormattedSortedSetValue(key),
                _ => $"Unknown type: {keyType}"
            };
        }

        private async Task<object> GetFormattedStringValue(string key)
        {
            var value = await _db.StringGetAsync(key);
            var strValue = value.ToString();

            // Thử parse JSON
            if (strValue.TrimStart().StartsWith("{") || strValue.TrimStart().StartsWith("["))
            {
                try
                {
                    using var doc = JsonDocument.Parse(strValue);
                    return ConvertJsonElement(doc.RootElement);
                }
                catch
                {
                    return strValue;
                }
            }

            return strValue;
        }

        private async Task<object> GetFormattedHashValue(string key)
        {
            var entries = await _db.HashGetAllAsync(key);
            var dict = new Dictionary<string, object>();

            foreach (var entry in entries)
            {
                var valueStr = entry.Value.ToString();

                if (valueStr.TrimStart().StartsWith("{") || valueStr.TrimStart().StartsWith("["))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(valueStr);
                        dict[entry.Name.ToString()] = ConvertJsonElement(doc.RootElement);
                    }
                    catch
                    {
                        dict[entry.Name.ToString()] = valueStr;
                    }
                }
                else
                {
                    dict[entry.Name.ToString()] = valueStr;
                }
            }

            return dict;
        }

        private async Task<object> GetFormattedListValue(string key)
        {
            var values = await _db.ListRangeAsync(key);
            return values.Select(v => {
                var str = v.ToString();
                if (str.TrimStart().StartsWith("{") || str.TrimStart().StartsWith("["))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(str);
                        return ConvertJsonElement(doc.RootElement);
                    }
                    catch
                    {
                        return (object)str;
                    }
                }
                return (object)str;
            }).ToList();
        }

        private async Task<object> GetFormattedSetValue(string key)
        {
            var values = await _db.SetMembersAsync(key);
            return values.Select(v => {
                var str = v.ToString();
                if (str.TrimStart().StartsWith("{") || str.TrimStart().StartsWith("["))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(str);
                        return ConvertJsonElement(doc.RootElement);
                    }
                    catch
                    {
                        return (object)str;
                    }
                }
                return (object)str;
            }).ToList();
        }

        private async Task<object> GetFormattedSortedSetValue(string key)
        {
            var values = await _db.SortedSetRangeByRankWithScoresAsync(key);
            return values.Select(v => new
            {
                Value = v.Element.ToString(),
                Score = v.Score
            }).ToList();
        }

        /// <summary>
        /// Convert JsonElement thành object thực (Dictionary hoặc List)
        /// </summary>
        private static object ConvertJsonElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var dict = new Dictionary<string, object>();
                    foreach (var property in element.EnumerateObject())
                    {
                        dict[property.Name] = ConvertJsonElement(property.Value);
                    }
                    return dict;

                case JsonValueKind.Array:
                    return element.EnumerateArray()
                        .Select(ConvertJsonElement)
                        .ToList();

                case JsonValueKind.String:
                    return element.GetString() ?? "";

                case JsonValueKind.Number:
                    if (element.TryGetInt32(out int intValue))
                        return intValue;
                    if (element.TryGetInt64(out long longValue))
                        return longValue;
                    return element.GetDouble();

                case JsonValueKind.True:
                    return true;

                case JsonValueKind.False:
                    return false;

                case JsonValueKind.Null:
                    return null!;

                default:
                    return element.ToString();
            }
        }

        /// <summary>
        /// Parse JSON string an toàn
        /// </summary>
        public static T? SafeDeserialize<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                return JsonSerializer.Deserialize<T>(json, _jsonOptions);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Serialize object thành JSON string
        /// </summary>
        public static string SafeSerialize<T>(T obj)
        {
            try
            {
                return JsonSerializer.Serialize(obj, _jsonOptions);
            }
            catch
            {
                return "{}";
            }
        }

        #endregion
    }
}