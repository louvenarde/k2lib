
namespace LouveSystems.K2.Lib
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;

    public class GameSession
    {
        public event Action<Transform> OnTransformAdded;

        // This tracks _some_ statistics
        private class StatisticsTracker
        {
            public void RefreshRealmMinMax(in GameState state)
            {
                for (byte i = 0; i < state.statistics.realms.Length; i++) {
                    RefreshRealmMinMax(state, i);
                }
            }

            private void RefreshRealmMinMax(in GameState state, byte realmIndex)
            {
                List<int> regions = new List<int>();
                state.world.GetTerritoryOfRealm(realmIndex, regions);

                state.statistics.realms[realmIndex].minTerritorySize = Math.Min((ushort)regions.Count, state.statistics.realms[realmIndex].minTerritorySize);
                state.statistics.realms[realmIndex].maxTerritorySize = Math.Max((ushort)regions.Count, state.statistics.realms[realmIndex].maxTerritorySize);

                state.statistics.realms[realmIndex].minRiches = Math.Min(state.statistics.realms[realmIndex].minRiches, (ushort)state.world.Realms[realmIndex].silverTreasury);
                state.statistics.realms[realmIndex].maxRiches = Math.Max(state.statistics.realms[realmIndex].maxRiches, (ushort)state.world.Realms[realmIndex].silverTreasury);

                state.statistics.realms[realmIndex].maxAdministration = Math.Max(state.statistics.realms[realmIndex].maxAdministration, (ushort)state.world.Realms[realmIndex].availableDecisions);

                int buildingsCount = 0;
                for (int regionIndex = 0; regionIndex < state.world.Regions.Count; regionIndex++) {
                    if (state.world.Regions[regionIndex].Building != EBuilding.None) {
                        buildingsCount++;
                    }
                }

                state.statistics.realms[realmIndex].maxBuildings = Math.Max(state.statistics.realms[realmIndex].maxBuildings, (ushort)buildingsCount);

                if (state.voting.Result.scores != null) {
                    int myVotes = state.voting.Result.GetScoreOfRealm(realmIndex).totalVotes;
                    state.statistics.realms[realmIndex].maxVotesInSingleCouncil = Math.Max(state.statistics.realms[realmIndex].maxVotesInSingleCouncil, (ushort)myVotes);
                }
            }

            public void AddVotes(in GameState state)
            {
                for (byte i = 0; i < state.statistics.realms.Length; i++) {
                    AddVotes(state, i);
                }
            }

            private void AddVotes(in GameState state, byte realmIndex)
            {
                if (state.voting.Result.scores != null) {
                    int myVotes = state.voting.Result.GetScoreOfRealm(realmIndex).totalVotes;
                    state.statistics.realms[realmIndex].tally += (ushort)myVotes;
                }
            }
        }

        public GameRules Rules => parameters;

        public GameState CurrentGameState => gameState;

        public IReadOnlyList<Transform> AwaitingTransforms => awaitingTransforms;

        // Key is player id
        public IReadOnlyDictionary<byte, SessionPlayer> SessionPlayers => sessionPlayers;

        public ManagedRandom ComputersRandom { get; }

        protected ManagedRandom Random { get; }

        private readonly List<Transform> awaitingTransforms = new List<Transform>();

        private readonly GameRules parameters;

        private readonly Dictionary<byte, SessionPlayer> sessionPlayers = new Dictionary<byte, SessionPlayer>();

        private readonly StatisticsTracker statisticsRecorder = new StatisticsTracker();

        protected GameState gameState;

        public GameSession(PartySessionInitializationParameters party, GameRules parameters, int seed)
        {
            this.parameters = parameters;

            this.Random = new ManagedRandom(seed);
            this.ComputersRandom = new ManagedRandom(seed);

            gameState = new GameState(party, parameters);

            List<byte> realmsIndices = new List<byte>();
            for (byte i = 0; i < gameState.world.Realms.Count; i++) {
                realmsIndices.Add(i);
            }

            realmsIndices.RemoveAll(CurrentGameState.world.IsRealmExcludedFromVoting);
            realmsIndices.Shuffle(Random);

            gameState.world.Modify(out Region[] regions, out Realm[] realms);

            for (byte i = 0; i < party.realmsToInitialize.Length; i++) {
                byte playerId = party.realmsToInitialize[i].forPlayerId;
                sessionPlayers[playerId] = CreatePlayer(playerId);
                sessionPlayers[playerId].RealmIndex = realmsIndices[i];
                realms[realmsIndices[i]].factionIndex = party.realmsToInitialize[i].factionIndex;
            }

            for (byte i = 0; i < party.realmsToInitialize.Length; i++) {
                if (party.realmsToInitialize[i].initialSubjugatorPlayerId.HasValue) {
                    byte subjugator = party.realmsToInitialize[i].initialSubjugatorPlayerId.Value;
                    if (sessionPlayers.TryGetValue(subjugator, out SessionPlayer sessionPlayer)) {
                        realms[realmsIndices[i]].isSubjugated = true;
                        realms[realmsIndices[i]].subjugatedBy = sessionPlayer.RealmIndex;

                        // Transfer all gold
                        realms[sessionPlayer.RealmIndex].silverTreasury += realms[realmsIndices[i]].silverTreasury;
                        realms[realmsIndices[i]].silverTreasury = 0;
                    }
                }
            }

            gameState.daysRemainingBeforeNextCouncil = (byte)(parameters.turnsBetweenVotes + parameters.initialVoteTurnsDelay);
        }

        public bool GetPlannedConstructionForRegion(int regionIndex, out SessionPlayer player, out EBuilding building)
        {
            foreach (var sessionPlayer in SessionPlayers) {
                if (sessionPlayer.Value.IsBuildingSomething(regionIndex, out building)) {
                    player = sessionPlayer.Value;
                    return true;
                }
            }

            player = default;
            building = default;
            return false;
        }

        public bool GetPlannedConstructionForRegion(int regionIndex, out EBuilding building)
        {
            return GetPlannedConstructionForRegion(regionIndex, out _, out building);
        }


        public bool EverybodyHasPlayed()
        {
            foreach (var player in SessionPlayers) {
                if (player.Value.HasAnythingToPlay()) {
                    return false;
                }
            }

            return true;
        }

        protected virtual SessionPlayer CreatePlayer(byte playerId)
        {
            return new SessionPlayer(this);
        }

        public virtual bool AddTransform(Transform transform)
        {
            return AddTransformIfPossible(transform);
        }

        public bool IsFavoured(byte realmIndex)
        {
            return gameState.world.Realms[realmIndex].isFavoured;
        }

        public bool HasRegionPlayed(int regionIndex)
        {
            return HasRegionPlayed(regionIndex, out _);
        }

        public bool HasRegionPlayed(int regionIndex, out RegionRelatedTransform transform)
        {
            for (int i = 0; i < awaitingTransforms.Count; i++) {
                if (awaitingTransforms[i] is RegionRelatedTransform attack && attack.actingRegionIndex == regionIndex) {
                    transform = attack;
                    return true;
                }
            }

            transform = default;
            return false;
        }

        // Returns TRUE if the game can continue
        // Returns FALSE if the game has ended
        public bool Advance()
        {
            ResolveTransformsToEffects(out ITransformEffect[] orderedEffects);
            GameState newGameState = gameState.Duplicate();
            gameState.ApplyEffects(orderedEffects, ref newGameState);

            // Resolve revenue
            for (int regionIndex = 0; regionIndex < newGameState.world.Regions.Count; regionIndex++) {
                if (newGameState.world.Regions[regionIndex].GetOwner(out byte owningRealm)) {
                    int revenue = newGameState.world.GetRegionSilverWorth(regionIndex);
                    newGameState.world.AddSilverTreasury(owningRealm, revenue);
                }
            }

            gameState = newGameState;

            AdvanceDay();

            if (gameState.daysRemainingBeforeNextCouncil <= 0) {
                gameState.voting.ComputeVotes(in gameState, Random);
                AdvanceAfterCouncil();


                if (gameState.voting.Result.HasMajorityWinner(out byte winnerIndex)) {
                    return false;
                }
            }

            return true;
        }

        public bool GetOwnerOfRegion(int regionIndex, out byte owningPlayerId, out byte subjugatingPlayerId)
        {
            bool isOwned = GetOwnerOfRegion(regionIndex, out subjugatingPlayerId, subjugator: true);
            isOwned |= GetOwnerOfRegion(regionIndex, out owningPlayerId, subjugator: false);

            return isOwned;
        }

        public bool GetOwnerOfRegion(int regionIndex, out byte owningPlayerId, bool subjugator = true)
        {
            if (CurrentGameState.world.Regions[regionIndex].GetOwner(out byte owningRealm)) {
                return GetOwnerOfRealm(owningRealm, out owningPlayerId, subjugator);
            }

            owningPlayerId = default;
            return false;
        }

        public bool IsRealmSubjugated(int realmIndex, out byte subjugatingRealmIndex)
        {
            return CurrentGameState.world.Realms[realmIndex].IsSubjugated(out subjugatingRealmIndex);
        }

        public List<SessionPlayer> GetAlliance(SessionPlayer containingPlayer)
        {
            List<byte> alliedRealms = new List<byte>();
            CurrentGameState.world.GetAllianceRealms(containingPlayer.RealmIndex, alliedRealms);

            List<SessionPlayer> alliance = new List<SessionPlayer>(alliedRealms.Count);
            for (int i = 0; i < alliedRealms.Count; i++) {
                foreach (var kv in sessionPlayers) {
                    if (kv.Value.RealmIndex == alliedRealms[i]) {
                        alliance.Add(kv.Value);
                        break;
                    }
                }
            }

            return alliance;
        }

        public List<RegionAttackRegionTransform> GetAllianceAttacks(SessionPlayer containingPlayer)
        {
            var alliance = GetAlliance(containingPlayer);
            List<RegionAttackRegionTransform> attacks = new List<RegionAttackRegionTransform>();
            for (int i = 0; i < alliance.Count; i++) {
                alliance[i].GetPlannedAttacks(attacks);
            }

            return attacks;
        }

        public bool GetOwnerOfRealm(int realmIndex, out SessionPlayer player, bool subjugator = true)
        {
            if (GetOwnerOfRealm(realmIndex, out byte pId, subjugator)) {
                player = SessionPlayers[pId];
                return true;
            }

            player = default;

            return false;
        }

        public bool GetOwnerOfRealm(int realmIndex, out byte owningPlayerId, bool subjugator = true)
        {
            if (subjugator &&
                CurrentGameState.world.Realms[realmIndex].IsSubjugated(out byte subjugatingRealmIndex)) {
                realmIndex = subjugatingRealmIndex;
            }

            foreach (var player in SessionPlayers) {
                if (player.Value.RealmIndex == realmIndex) {
                    owningPlayerId = player.Key;
                    return true;
                }
            }

            owningPlayerId = 0;
            return false;
        }

        protected void ResolveTransformsToEffects(out ITransformEffect[] effects)
        {
            effects = new ITransformEffect[0];
            try {
                CurrentGameState.ComputeEffects(Random, AwaitingTransforms, out effects);
            }
            catch (Exception e) {
                Logger.Error(e);
            }

            awaitingTransforms.Clear();

        }

        protected void AdvanceDay()
        {
            gameState.daysRemainingBeforeNextCouncil--;

            gameState.daysPassed++;

            statisticsRecorder.RefreshRealmMinMax(gameState);
        }

        protected void AdvanceAfterCouncil()
        {
            gameState.daysRemainingBeforeNextCouncil = this.Rules.turnsBetweenVotes;

            gameState.councilsPassed++;

            // Reset favours
            for (byte realmIndex = 0; realmIndex < gameState.world.Realms.Count; realmIndex++) {
                gameState.world.SetRealmFavoured(realmIndex, false);
                gameState.world.ClearBeastAttacks(realmIndex);
            }

            statisticsRecorder.AddVotes(gameState);
            statisticsRecorder.RefreshRealmMinMax(gameState);
        }


        private bool AddTransformIfPossible(Transform transform)
        {
            Logger.Trace($"Trying to add transform {transform}...");

            if (transform.CompatibleWith(this)) {
                Logger.Info($"Adding transform {transform}");
                awaitingTransforms.Add(transform);
                OnTransformAdded?.Invoke(transform);
                return true;
            }
            else {
                Logger.Warn($"Discarding transform {transform} because it is not compatible with current awaiting transforms");
                return false;
            }
        }
    }
}