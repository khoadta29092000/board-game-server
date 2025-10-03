using CleanArchitecture.Domain.Model.Splendor.Components;
using CleanArchitecture.Domain.Model.Splendor.Entity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CleanArchitecture.Domain.Model.Splendor.System
{
    public class GameInitializationSystem : ISystem
    {
        private readonly Random _rng = new Random();

        public void Execute(GameContext context)
        {
            var session = context.GameSession;
            if (session == null) return;

            var board = context.GetEntity<BoardEntity>(session.BoardEntityId);
            var boardComp = board?.GetComponent<BoardComponent>();
            if (boardComp == null) return;

            // 1. Gather all card entities from context and group by level
            var allCards = context.Entities
                .Values
                .OfType<CardEntity>()
                .Select(e => new { Id = e.Id, Comp = e.GetComponent<CardComponent>() })
                .Where(x => x.Comp != null)
                .ToList();

            // Prepare level -> List<Guid> mapping (ensure keys 1..3 exist)
            var levelToCards = new Dictionary<int, List<Guid>>
            {
                { 1, new List<Guid>() },
                { 2, new List<Guid>() },
                { 3, new List<Guid>() }
            };

            foreach (var c in allCards)
            {
                var lvl = c.Comp.Level;
                if (!levelToCards.ContainsKey(lvl)) levelToCards[lvl] = new List<Guid>();
                levelToCards[lvl].Add(c.Id);
            }

            // 2. Shuffle each level list
            foreach (var lvl in levelToCards.Keys.ToList())
            {
                var list = levelToCards[lvl];
                var shuffled = list.OrderBy(_ => _rng.Next()).ToList();
                levelToCards[lvl] = shuffled;
            }

            // 3. Seed deck queues in BoardComponent (this converts List -> Queue)
            boardComp.SeedDecks(levelToCards, _rng);

            // 4. Refill visible cards per level (will dequeue from each Queue)
            boardComp.RefillVisible(1);
            boardComp.RefillVisible(2);
            boardComp.RefillVisible(3);

            // 5. Shuffle nobles and pick (playerCount + 1) nobles to show
            var allNobleIds = context.Entities
                .Values
                .OfType<NobleEntity>()
                .Select(e => e.Id)
                .ToList();

            var shuffledNobles = allNobleIds.OrderBy(_ => _rng.Next()).ToList();
            var pick = Math.Min(Math.Max(0, session.PlayerEntityIds.Count + 1), shuffledNobles.Count);
            boardComp.VisibleNobles = shuffledNobles.Take(pick).ToList();

            // OPTIONAL: update session.CardDeckIds and session.NobleIds if you rely on them elsewhere
            session.CardDeckIds.Clear();
            session.CardDeckIds.AddRange(allCards.Select(x => x.Id));

            session.NobleIds.Clear();
            session.NobleIds.AddRange(allNobleIds);
        }
    }
}
