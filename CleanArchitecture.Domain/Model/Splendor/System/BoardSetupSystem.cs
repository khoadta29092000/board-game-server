using CleanArchitecture.Domain.Model.Splendor.Components;
using CleanArchitecture.Domain.Model.Splendor.Entity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Domain.Model.Splendor.System
{
    public class BoardSetupSystem : ISystem
    {
        private readonly Random _rng = new Random();

        // inputs: mapping level -> list of card entity ids, and list of noble ids
        public void Execute(GameContext context)
        {
            var boardEntity = context.GetEntity<BoardEntity>(context.GameSession.BoardEntityId);
            var boardComp = boardEntity?.GetComponent<BoardComponent>();
            if (boardComp == null) return;

            // Expect GameSession.CardDeckIds contains all card entity ids; but best is to separate by level
            // We'll build level -> card guid lists by inspecting CardComponent on entities
            var levelMap = new Dictionary<int, List<Guid>> { { 1, new List<Guid>() }, { 2, new List<Guid>() }, { 3, new List<Guid>() } };

            // gather cards from context
            foreach (var kv in context.Entities)
            {
                if (kv.Value is CardEntity)
                {
                    var cardComp = kv.Value.GetComponent<CardComponent>();
                    if (cardComp != null)
                    {
                        levelMap[cardComp.Level].Add(kv.Key);
                    }
                }
            }

            // seed decks (shuffled)
            boardComp.SeedDecks(levelMap, _rng);

            // deal 4 visible each level
            foreach (var level in new[] { 1, 2, 3 })
            {
                boardComp.RefillVisible(level, 4);
            }

            // shuffle nobles and pick playerCount+1 nobles
            var nobles = context.GameSession.NobleIds.Select(id => id).OrderBy(_ => _rng.Next()).ToList();
            var playerCount = context.GameSession.PlayerEntityIds.Count;
            var pick = Math.Min(Math.Max(0, playerCount + 1), nobles.Count);
            boardComp.VisibleNobles = nobles.Take(pick).ToList();
        }
    }
}
