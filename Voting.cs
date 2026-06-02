
namespace LouveSystems.K2.Lib
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;

    public struct Voting : IBinarySerializableWithVersion
    {
        public struct VotingResult : IBinarySerializableWithVersion
        {
            public struct VoteScore : IBinarySerializableWithVersion
            {
                public bool WasFavoured => wonCriterias.Contains(EVotingCriteria.Favoured);

                public byte realmIndex;
                public int totalVotes;
                public HashSet<EVotingCriteria> wonCriterias;

                public int GetHash()
                {
                    int[] values;

                    if (wonCriterias != null) {

                        values = new int[wonCriterias.Count];

                        int index = 0;
                        for (EVotingCriteria i = 0; i < EVotingCriteria.COUNT; i++) {
                            if (wonCriterias.Contains(i)) {
                                values[index++] = (int)i;
                            }
                        }
                    }
                    else {
                        values = new int[0];
                    }

                    return Extensions.Hash(
                        realmIndex,
                        totalVotes,
                        Extensions.Hash(values)
                    );
                }

                public void Write(BinaryWriter into)
                {
                    into.Write(realmIndex);
                    into.Write(totalVotes);
                    into.WriteEnums(wonCriterias ?? new HashSet<EVotingCriteria>());
                }

                public void Read(byte version, BinaryReader from)
                {
                    realmIndex = from.ReadByte();
                    totalVotes = from.ReadInt32();

                    wonCriterias ??= new HashSet<EVotingCriteria>();
                    wonCriterias.Clear();
                    var criterias = from.ReadEnums<EVotingCriteria>();
                    for (int i = 0; i < criterias.Length; i++) {
                        wonCriterias.Add(criterias[i]);
                    }
                }
            }

            public VoteScore[] scores;

            public int wastedVotes;

            public VotingResult(in GameState gameState) : this()
            {
                scores = new VoteScore[gameState.world.Realms.Count];
            }

            public VotingResult(in VotingResult other)
            {
                if (other.scores != null) {
                    scores = new VoteScore[other.scores.Length];
                    Array.Copy(other.scores, scores, other.scores.Length);
                }
                else {
                    scores = null;
                }

                wastedVotes = other.wastedVotes;
            }

            public VoteScore GetScoreOfRealm(byte realmIndex)
            {
                for (int i = 0; i < scores.Length; i++) {
                    if (scores[i].realmIndex == realmIndex) {
                        return scores[i];
                    }
                }

                throw new Exception($"Realm {realmIndex} has no score");
            }

            public void OrderScores()
            {
                VoteScore[] orderedScores =
                    scores
                        .OrderByDescending((o) => o.totalVotes)
                        .ThenBy((o) => o.realmIndex)
                        .ToArray(); // Deterministic tie breaker

                for (int i = 0; i < orderedScores.Length; i++) {
                    scores[i] = orderedScores[i];
                }
            }

            public bool GetSecondPlace(out byte secondPlaceIndex)
            {
                if (scores.Length > 1) {
                    secondPlaceIndex = scores[1].realmIndex;
                    return true;
                }

                secondPlaceIndex = default;
                return false;
            }

            public bool HasMajorityWinner(out byte winnerIndex)
            {
                int totalScores = wastedVotes;

                if (scores != null) {
                    for (int i = 0; i < scores.Length; i++) {
                        totalScores += scores[i].totalVotes;
                    }

                    for (int i = 0; i < scores.Length; i++) {
                        if (scores[i].totalVotes > totalScores / 2) {
                            winnerIndex = scores[i].realmIndex;
                            return true;
                        }
                    }
                }

                winnerIndex = default;
                return false;
            }

            public string Dump()
            {
                StringBuilder sb = new StringBuilder();

                sb.AppendLine($"{wastedVotes} were wasted");

                for (int i = 0; i < scores.Length; i++) {
                    sb.AppendLine($"Realm {scores[i].realmIndex} won the following categories: {string.Join(", ", scores[i].wonCriterias)} for a total of {scores[i].totalVotes} votes");
                }


                return sb.ToString();
            }

            public int GetHash()
            {
                return Extensions.Hash(
                    scores == null ? 0 : Extensions.Hash(scores),
                    wastedVotes
                );
            }

            public void Write(BinaryWriter into)
            {
                if (scores == null) {
                    into.Write(new VoteScore[0]);
                }
                else {
                    into.Write(scores);
                }

                into.Write(wastedVotes);
            }

            public void Read(byte version, BinaryReader from)
            {
                from.Read(default, ref scores);
                wastedVotes = from.ReadInt32();
            }
        }

        public VotingResult Result => result;

        private VotingResult result;

        private readonly K2.Lib.GameRules rules;

        public Voting(K2.Lib.GameRules rules)
        {
            this.result = default;
            this.rules = rules;
        }

        public Voting(in Voting other)
        {
            this.result = new(other.result);
            this.rules = other.rules;
        }

        public void ComputeVotes(in K2.Lib.GameState gameState, ManagedRandom random)
        {
            result = new VotingResult(gameState);
            Logger.Info($"Computing votes for gamestate {gameState}");


            Dictionary<EVotingCriteria, HashSet<byte>> realmCategoryWinners = new Dictionary<EVotingCriteria, HashSet<byte>>();
            Dictionary<byte, int> totalVotesPerRealmIndex = new Dictionary<byte, int>();
            Dictionary<byte, int> totalWeightOfRealm = new Dictionary<byte, int>();

            realmCategoryWinners.Clear();
            totalVotesPerRealmIndex.Clear();

            for (byte i = 0; i < gameState.world.Realms.Count; i++) {
                totalVotesPerRealmIndex[i] = 0;
                totalWeightOfRealm[i] = 0;
                result.scores[i].wonCriterias = new HashSet<EVotingCriteria>();
                result.scores[i].realmIndex = i;
            }

            var criterias = PickCriterias(gameState, rules);

            Logger.Info($"The criterias picked for now are the following: {string.Join(", ", criterias)}");

            byte voicesPercentageThisTurn = rules.voting.turnoverPercentagePerCouncil[Math.Min(gameState.councilsPassed, rules.voting.turnoverPercentagePerCouncil.Length - 1)];

            Logger.Info($"This turn, {voicesPercentageThisTurn}% of votes will be counted");

            int totalVoicesPoint = voicesPercentageThisTurn * rules.voting.voterCount;

            Logger.Info($"That means {totalVoicesPoint} total voices will speak");

            for (EVotingCriteria criteria = 0; criteria < EVotingCriteria.COUNT; criteria++) {

                if (criteria == EVotingCriteria.Invalid) {
                    continue;
                }

                if (!criterias.Contains(criteria)) {
                    continue;
                }

                realmCategoryWinners[criteria] = new HashSet<byte>();

                // Special calculation for this one
                if (criteria == EVotingCriteria.Favoured) {

                    continue;
                }

                if (GetWinnersOfCategory(criteria, in gameState, random, realmCategoryWinners[criteria])) {
                    Logger.Info($"Realms {string.Join(", ", realmCategoryWinners[criteria])} are the category winners for criteria {criteria}");
                }
            }

            // Remove accidental if the person already got something else
            {
                if (realmCategoryWinners.TryGetValue(EVotingCriteria.Accident, out HashSet<byte> accidentWinners)) {
                    for (byte realmIndex = 0; realmIndex < gameState.world.Realms.Count; realmIndex++) {
                        if (accidentWinners.Contains(realmIndex)) {
                            for (EVotingCriteria criteria = 0; criteria < EVotingCriteria.COUNT; criteria++) {
                                if (criteria == EVotingCriteria.Accident) {
                                    continue;
                                }

                                if (realmCategoryWinners.TryGetValue(criteria, out var winnersOfThatCriteria)) {
                                    if (winnersOfThatCriteria.Contains(realmIndex)) {
                                        realmCategoryWinners[EVotingCriteria.Accident].Remove(realmIndex);
                                        Logger.Info($"Realm {realmIndex} cannot win the Accident criteria because they're winning something else {criteria}");
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            LimitCriteriasToTurnLimitations(in gameState, rules, random, realmCategoryWinners, criterias);

            AddFavouredWeights(in gameState, rules, in totalWeightOfRealm);

            // Apply category winners
            for (EVotingCriteria criteria = 0; criteria < EVotingCriteria.COUNT; criteria++) {
                if (realmCategoryWinners.TryGetValue(criteria, out HashSet<byte> winners) && winners.Count > 0) {
                    var settings = rules.GetVotingSetting(criteria);

                    for (byte realmIndex = 0; realmIndex < gameState.world.Realms.Count; realmIndex++) {
                        if (winners.Contains(realmIndex)) {
                            totalWeightOfRealm[realmIndex] += settings.influenceWeight;

                            for (int scoreIndex = 0; scoreIndex < result.scores.Length; scoreIndex++) {
                                if (result.scores[scoreIndex].realmIndex == realmIndex) {
                                    result.scores[scoreIndex].wonCriterias.Add(criteria);
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            // calculate total weight
            int totalWeight = 0;
            for (byte i = 0; i < gameState.world.Realms.Count; i++) {
                foreach (var criteria in result.scores[i].wonCriterias) {
                    totalWeight += rules.GetVotingSetting(criteria).influenceWeight;
                }
            }

            Logger.Info($"Sum of criterias weight is {totalWeight}");

            Logger.Info($"Total weight is {totalWeight}");

            // Transform voices into votes
            int totalExpressedVotes = 0;
            for (byte i = 0; i < gameState.world.Realms.Count; i++) {
                if (totalWeightOfRealm[i] == 0) {
                    Logger.Info($"Real {i} won nothing, skipping");
                    result.scores[i].totalVotes = 0;
                    continue;
                }

                int percentageOfVoices = (totalWeightOfRealm[i] * 100) / totalWeight;
                Logger.Info($"Accumulated weight of realm {i} is {totalWeightOfRealm[i]}. With a total weight of {totalWeight}, they have {(percentageOfVoices):n1}% of voices");

                int voices = (percentageOfVoices * totalVoicesPoint) / 100;
                Logger.Info($"That means they have {voices} out of the total {totalVoicesPoint} voices");

                int votes = voices / 100; // 100 % turnout is the best you can have. So this gives the number of votercount that voted for them
                Logger.Info($"This represents a total of {votes} votes out of {rules.voting.voterCount} voters");

                totalVotesPerRealmIndex[i] = votes;

                result.scores[i].totalVotes = votes;
                totalExpressedVotes += votes;
            }

            result.wastedVotes = rules.voting.voterCount - totalExpressedVotes;

            Logger.Info($"Only {totalExpressedVotes} were expressed, which means {result.wastedVotes} were wasted.");

            result.OrderScores();

            bool isAtMaxTurnover = gameState.councilsPassed >= rules.voting.turnoverPercentagePerCouncil.Length - 1;
            if (rules.voting.forceMajorityEventually && isAtMaxTurnover) {
                if (result.HasMajorityWinner(out byte _)) { // Nothing to do
                }
                else {
                    Logger.Info($"We are at max turnover and so a majority MUST be reached!");

                    Logger.Info($"Adding all wasted votes ({result.wastedVotes} votes) to current poll leader (Realm {result.scores[0].realmIndex} with {result.scores[0].totalVotes} votes)");
                    result.scores[0].totalVotes += result.wastedVotes;
                    result.wastedVotes = 0;
                    Logger.Info($"Poll leader now has {result.scores[0].totalVotes} votes (Realm {result.scores[0].realmIndex})");

                    while (!result.HasMajorityWinner(out byte _)) {

                        // take votes from others
                        for (int i = result.scores.Length - 1; i >= 1; i--) {

                            if (result.scores[i].totalVotes > 0) {
                                result.scores[i].totalVotes--;
                                result.scores[0].totalVotes++;

                                if (result.scores[i].totalVotes <= 0 &&
                                    result.scores[i].wonCriterias.Count > 0) {
                                    result.scores[i].wonCriterias.Clear();
                                }

                                Logger.Info($"Transferring vote from realm {result.scores[i].realmIndex} to realm {result.scores[0].realmIndex} (Poll leader now has {result.scores[0].totalVotes} votes)");
                            }
                        }

                        result.OrderScores();
                        Logger.Info($"Poll leader now has {result.scores[0].totalVotes} votes (Realm {result.scores[0].realmIndex})");
                    }
                }
            }

            Logger.Info($"Final voting results: {result.Dump()}");
        }

        private void LimitCriteriasToTurnLimitations(in K2.Lib.GameState gameState, K2.Lib.GameRules rules, in ManagedRandom random, in Dictionary<EVotingCriteria, HashSet<byte>> winnersPerCriteria, in HashSet<EVotingCriteria> criterias)
        {
            criterias.Clear();
            for (EVotingCriteria criteria = 0; criteria < EVotingCriteria.COUNT; criteria++) {
                if (winnersPerCriteria.TryGetValue(criteria, out HashSet<byte> winners) && winners.Count > 0) {
                    criterias.Add(criteria);
                }
            }

            Logger.Info($"After limiting to winners only, only {criterias.Count} criteria remain ({string.Join(", ", criterias)})");


            byte criteriasThisTurn = rules.voting.criteriasUsedPerVote[Math.Min(gameState.councilsPassed, rules.voting.criteriasUsedPerVote.Length - 1)];
            criteriasThisTurn = Math.Min(criteriasThisTurn, (byte)EVotingCriteria.COUNT);

            if (criterias.Count <= criteriasThisTurn) {
                Logger.Info($"Criteria count ({criterias.Count}) is under the limit ({criteriasThisTurn}), not limiting any more.");
            }
            else {
                Logger.Info($"Criteria count ({criterias.Count}) is over the limit ({criteriasThisTurn}), limiting some");

                HashSet<EVotingCriteria> oldCriterias = new HashSet<EVotingCriteria>(criterias);
                criterias.Clear();

                while (criterias.Count < criteriasThisTurn) {

                    int totalWeights = 0;

                    for (EVotingCriteria criteria = 0; criteria < EVotingCriteria.COUNT; criteria++) {

                        if (!oldCriterias.Contains(criteria)) {
                            continue;
                        }

                        if (criterias.Contains(criteria)) {
                            continue;
                        }

                        var setting = rules.GetVotingSetting(criteria);
                        totalWeights += setting.chancesToBeSelected;
                    }

                    int picked = random.Next(totalWeights);
                    totalWeights = 0;

                    for (EVotingCriteria criteria = 0; criteria < EVotingCriteria.COUNT; criteria++) {

                        if (!oldCriterias.Contains(criteria)) {
                            continue;
                        }

                        if (criterias.Contains(criteria)) {
                            continue;
                        }

                        int weight = rules.GetVotingSetting(criteria).chancesToBeSelected;
                        if (totalWeights + weight > picked) {
                            Logger.Info($"Randomly selected criteria {criteria} ({criterias.Count}) for this voting session");
                            criterias.Add(criteria);
                            break;
                        }
                        else {
                            totalWeights += weight;
                        }
                    }
                }

                // Removed acquired voices
                for (EVotingCriteria criteria = 0; criteria < EVotingCriteria.COUNT; criteria++) {
                    if (!criterias.Contains(criteria) && winnersPerCriteria.TryGetValue(criteria, out var winners) && winners.Count > 0) {
                        Logger.Info($"Disqualified winners of criteria {criteria}");
                        winners.Clear();
                    }
                }


                Logger.Info($"Done picking an appropriate number of criterias ({criterias.Count}) to retain: {string.Join(", ", criterias)}");
            }
        }

        private void AddFavouredWeights(in K2.Lib.GameState gameState, K2.Lib.GameRules rules, in Dictionary<byte, int> voices)
        {
            var settings = rules.GetVotingSetting(EVotingCriteria.Favoured);

            for (byte realmIndex = 0; realmIndex < gameState.world.Realms.Count; realmIndex++) {
                if (gameState.world.Realms[realmIndex].isFavoured) {
                    voices[realmIndex] += settings.influenceWeight;

                    for (int scoreIndex = 0; scoreIndex < result.scores.Length; scoreIndex++) {
                        if (result.scores[scoreIndex].realmIndex == realmIndex) {
                            result.scores[scoreIndex].wonCriterias.Add(EVotingCriteria.Favoured);
                            break;
                        }
                    }
                }
            }
        }

        private HashSet<EVotingCriteria> PickCriterias(in K2.Lib.GameState gameState, K2.Lib.GameRules rules)
        {
            HashSet<EVotingCriteria> pickedCriterias = new HashSet<EVotingCriteria>((int)EVotingCriteria.COUNT);

            // These two always have priority to be there
            {
                if (rules.GetVotingSetting(EVotingCriteria.Favoured).enabled &&
                    rules.GetVotingSetting(EVotingCriteria.Favoured).activeAfterCouncils <= gameState.councilsPassed) {

                    for (int i = 0; i < gameState.world.Realms.Count; i++) {

                        if (gameState.world.Realms[i].isSubjugated) {
                            continue;
                        }

                        if (gameState.world.Realms[i].isFavoured) {
                            pickedCriterias.Add(EVotingCriteria.Favoured);
                            break;
                        }
                    }
                }

                if (rules.GetVotingSetting(EVotingCriteria.MaxChurches).enabled &&
                    rules.GetVotingSetting(EVotingCriteria.MaxChurches).activeAfterCouncils <= gameState.councilsPassed) {

                    for (int i = 0; i < gameState.world.Regions.Count; i++) {
                        if (gameState.world.Regions[i].RelevantBuilding.HasFlagSafe(EBuilding.Church)) {
                            pickedCriterias.Add(EVotingCriteria.MaxChurches);
                            break;
                        }
                    }
                }
            }


            for (EVotingCriteria criteria = 0; criteria < EVotingCriteria.COUNT; criteria++) {

                if (criteria == EVotingCriteria.Invalid) {
                    continue;
                }

                if (pickedCriterias.Contains(criteria)) {
                    continue;
                }

                var setting = rules.GetVotingSetting(criteria);
                if (!setting.enabled) {
                    continue;
                }

                if (setting.activeAfterCouncils > gameState.councilsPassed) {
                    continue;
                }

                pickedCriterias.Add(criteria);
            }

            return pickedCriterias;
        }

        private bool GetWinnersOfCategory(EVotingCriteria criteria, in K2.Lib.GameState gameState, ManagedRandom random, in ICollection<byte> winnerRealmIndices)
        {
            switch (criteria) {
                default:
                    return false;

                case EVotingCriteria.MaxDevelopment: {
                        GetRealmIndicesWithMostOf(gameState, (in GameState state, byte realmIndex) =>
                        {
                            List<int> regions = new();
                            state.world.GetTerritoryOfRealm(realmIndex, regions, (region) => region.RelevantBuilding != EBuilding.None, includeSubjugated: true);

                            return regions.Count;
                        }, winnerRealmIndices, minScore: 3, stretchPercentageAllowed: 10, maxWinners: 2);

                        return winnerRealmIndices.Count > 0;
                    }

                case EVotingCriteria.MaxChurches: {
                        GetRealmIndicesWithMostOf(gameState, (in GameState state, byte realmIndex) =>
                        {
                            List<int> regions = new();
                            state.world.GetTerritoryOfRealm(realmIndex, regions, (region) => region.RelevantBuilding.HasFlagSafe(EBuilding.Church), includeSubjugated: true);
                            return regions.Count;
                        }, winnerRealmIndices, minScore: 2, maxWinners: 2);

                        return winnerRealmIndices.Count > 0;
                    }

                case EVotingCriteria.MaxLands: {
                        GetRealmIndicesWithMostOf(gameState, (in GameState state, byte realmIndex) =>
                        {
                            List<int> regions = new();
                            state.world.GetTerritoryOfRealm(realmIndex, regions, includeSubjugated: true);
                            return regions.Count;
                        }, winnerRealmIndices, stretchPercentageAllowed: 20, maxWinners: 2);

                        return winnerRealmIndices.Count > 0;
                    }

                case EVotingCriteria.MaxMoney: {
                        GetRealmIndicesWithMostOf(gameState, (in GameState state, byte realmIndex) =>
                        {
                            return state.world.GetSilverTreasury(realmIndex);
                        }, winnerRealmIndices, minScore: 100, stretchPercentageAllowed: 10, maxWinners: 2);

                        return winnerRealmIndices.Count > 0;
                    }

                case EVotingCriteria.Accident: {
                        byte accidentWinner = (byte)random.Next(gameState.world.Realms.Count);

                        if (!gameState.world.IsRealmExcludedFromVoting(accidentWinner)) {
                            winnerRealmIndices.Add(accidentWinner);
                            return true;
                        }

                        return false;
                    }

                case EVotingCriteria.BestAdministration: {

                        if (GetRealmIndexWithMostOf(gameState, (in GameState state, byte realmIndex) =>
                        {
                            return state.world.Realms[realmIndex].availableDecisions;
                        }, out byte winnerRealm, minScore: rules.startingDecisionCount + 1)) {
                            winnerRealmIndices.Add(winnerRealm);
                            return true;
                        }

                        return false;
                    }

                case EVotingCriteria.Martyrdom: {
                        List<int> regionIndices = new List<int>();
                        GetRealmIndicesWithMostOf(gameState, (in GameState state, byte realmIndex) =>
                        {
                            regionIndices.Clear();
                            state.world.GetTerritoryOfRealm(realmIndex, regionIndices, includeSubjugated: true);
                            return Math.Max(0, 6 - regionIndices.Count);
                        }, winnerRealmIndices, minScore: 3, stretchPercentageAllowed: 15);

                        return winnerRealmIndices.Count > 0;
                    }

                case EVotingCriteria.CouncilNeighbor: {

                        List<int> territory = new();
                        List<int> neighbors = new();

                        if (GetRealmIndexWithMostOf(gameState, (in GameState state, byte realmIndex) =>
                        {
                            territory.Clear();
                            state.world.GetTerritoryOfRealm(realmIndex, in territory, includeSubjugated: true);

                            int councilNeighbors = 0;
                            for (int i = 0; i < territory.Count; i++) {
                                int regionIndex = territory[i];

                                neighbors.Clear();
                                state.world.GetNeighboringRegions(regionIndex, neighbors);

                                for (int j = 0; j < neighbors.Count; j++) {
                                    int neighborIndex = neighbors[j];
                                    if (state.world.IsCouncilRegion(neighborIndex)) {
                                        councilNeighbors++;
                                    }
                                }
                            }

                            return councilNeighbors;
                        }, out byte winnerRealm, minScore: 2)) {
                            winnerRealmIndices.Add(winnerRealm);
                            return true;
                        }

                        return false;
                    }

                case EVotingCriteria.BeastAttacked: {
                        GetRealmIndicesWithMostOf(
                            gameState,
                            (in GameState state, byte realmIndex) => state.world.Realms[realmIndex].beastsAttacked,
                            winnerRealmIndices,
                            minScore: 1,
                            stretchPercentageAllowed: 20
                        );

                        return winnerRealmIndices.Count > 0;
                    }
            }
        }

        private delegate int RealmScoreAtCategory(in GameState gameState, byte realmIndex);

        private void GetRealmIndicesWithMostOf(
            in K2.Lib.GameState gameState,
            RealmScoreAtCategory getter,
            in ICollection<byte> winners,
            int minScore = 0,
            int stretchPercentageAllowed = 0,
            int maxWinners = int.MaxValue
            )
        {
            Dictionary<byte, int> scores = new Dictionary<byte, int>(gameState.world.Realms.Count);
            int maxScore = 0;

            for (byte realmIndex = 0; realmIndex < gameState.world.Realms.Count; realmIndex++) {
                if (gameState.world.IsRealmExcludedFromVoting(realmIndex)) {
                    continue;
                }

                int score = getter(gameState, realmIndex);

                scores[realmIndex] = score;
                maxScore = Math.Max(maxScore, score);
            }

            if (maxScore < minScore) {
                return;
            }

            int stretchAllowed = (maxScore * stretchPercentageAllowed) / 100;

            for (byte realmIndex = 0; realmIndex < gameState.world.Realms.Count; realmIndex++) {
                if (gameState.world.IsRealmExcludedFromVoting(realmIndex)) {
                    continue;
                }

                if (scores[realmIndex] >= maxScore - stretchAllowed) {
                    winners.Add(realmIndex);
                }
            }

            if (winners.Count > maxWinners) {
                winners.Clear();
            }
        }

        private bool GetRealmIndexWithMostOf(in K2.Lib.GameState gameState, RealmScoreAtCategory getter, out byte winner, int minScore = 0)
        {
            Dictionary<byte, int> scores = new Dictionary<byte, int>(gameState.world.Realms.Count);
            int maxScore = 0;

            for (byte realmIndex = 0; realmIndex < gameState.world.Realms.Count; realmIndex++) {
                int score = gameState.world.IsRealmExcludedFromVoting(realmIndex)
                    ? int.MinValue :
                    getter(gameState, realmIndex);

                scores[realmIndex] = score;
                maxScore = Math.Max(maxScore, score);
            }

            bool hasWinner = false;
            winner = default;

            if (maxScore < minScore) {
                return false;
            }

            for (byte realmIndex = 0; realmIndex < gameState.world.Realms.Count; realmIndex++) {
                if (scores[realmIndex] == maxScore) {
                    if (hasWinner) {
                        // Two winners
                        return false;
                    }
                    else {
                        hasWinner = true;
                        winner = realmIndex;
                    }
                }
            }

            return hasWinner;
        }

        public int GetHash()
        {
            return Extensions.Hash(
                result
            );
        }

        public void Write(BinaryWriter into)
        {
            result.Write(into);
        }

        public void Read(byte version, BinaryReader from)
        {
            result.Read(version, from);
        }
    }
}