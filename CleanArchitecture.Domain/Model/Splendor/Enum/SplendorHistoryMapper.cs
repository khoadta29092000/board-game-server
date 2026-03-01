using CleanArchitecture.Domain.Model.History;
using CleanArchitecture.Domain.Model.Splendor.Components;
using CleanArchitecture.Domain.Model.Splendor.Entity;
using CleanArchitecture.Domain.Model.Splendor.System;
using MongoDB.Bson;


namespace CleanArchitecture.Domain.Model.Splendor.Enum
{
    public static class SplendorHistoryMapper
    {
        public static GameHistory Map(GameContext context, string roomCode)
        {
            var session = context.GameSession;
            var boardEntity = context.GetEntity<BoardEntity>(session.BoardEntityId);
            var turnComp = boardEntity?.GetComponent<TurnComponent>();
            var boardComp = boardEntity?.GetComponent<BoardComponent>();

            // Rank players by points desc, then fewer cards = better tiebreak
            var ranked = session.PlayerEntityIds
                .Select(id => context.GetEntity<PlayerEntity>(id)?.GetComponent<PlayerComponent>())
                .Where(p => p != null)
                .OrderByDescending(p => p!.PrestigePoints)
                .ThenBy(p => p!.PurchaseCards.Count)
                .ToList();

            var players = new Dictionary<string, GamePlayerInfo>();
            for (int i = 0; i < ranked.Count; i++)
            {
                var p = ranked[i]!;
                players[p.PlayerId] = new GamePlayerInfo
                {
                    PlayerId = p.PlayerId,
                    Name = p.Name,
                    Rank = i + 1,
                    Score = p.PrestigePoints,
                    IsWinner = p.PlayerId == session.WinnerId,
                    Stats = new BsonDocument
                    {
                        ["gems"] = new BsonDocument(
                            p.Gems.ToDictionary(k => k.Key.ToString(), v => (BsonValue)v.Value)),
                        ["bonuses"] = new BsonDocument(
                            p.Bonuses.ToDictionary(k => k.Key.ToString(), v => (BsonValue)v.Value)),
                        ["purchasedCards"] = p.PurchaseCards.Count,
                        ["reservedCards"] = p.ReservedCards.Count,
                    }
                };
            }

            var winner = ranked.FirstOrDefault(p => p?.PlayerId == session.WinnerId);
            var startedAt = session.StartedAt ?? DateTime.UtcNow;
            var completedAt = session.CompletedAt ?? DateTime.UtcNow;

            return new GameHistory
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                GameId = roomCode,
                GameName = "Splendor",
                State = "Completed",
                WinnerId = session.WinnerId,
                WinnerName = winner?.Name,
                PlayerOrder = session.PlayerEntityIds
                    .Select(id => context.GetEntity<PlayerEntity>(id)
                        ?.GetComponent<PlayerComponent>()?.PlayerId ?? "")
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToList(),
                Players = players,
                TotalTurns = turnComp?.TurnNumber ?? 0,
                DurationSeconds = (int)(completedAt - startedAt).TotalSeconds,
                CreatedAt = session.CreatedAt,
                StartedAt = session.StartedAt,
                CompletedAt = completedAt,
                GameData = new BsonDocument
                {
                    ["remainingGems"] = boardComp != null
                        ? new BsonDocument(boardComp.AvailableGems.ToDictionary(
                            k => k.Key.ToString(), v => (BsonValue)v.Value))
                        : new BsonDocument(),
                    ["remainingDeckSizes"] = boardComp != null
                        ? new BsonDocument(boardComp.CardDecks.ToDictionary(
                            k => k.Key.ToString(), v => (BsonValue)v.Value.Count))
                        : new BsonDocument(),
                }
            };
        }
     }
    }
