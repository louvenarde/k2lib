
namespace LouveSystems.K2.Lib
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;

    public struct GameState : IBinarySerializable
    {
        const byte VERSION = 7;

        public int daysPassed;
        public int councilsPassed;
        public int daysRemainingBeforeNextCouncil;
        public World world;
        public Voting voting;
        public GameRules rules;
        public Statistics statistics; // Incremental
        public IBoard board;

        public GameState(PartySessionInitializationParameters party, GameRules parameters)
        {
            rules = parameters;
            councilsPassed = 0;
            daysPassed = 0;
            daysRemainingBeforeNextCouncil = parameters.turnsBetweenVotes;
            world = new World(party, parameters);
            voting = new Voting(parameters);
            statistics = new Statistics();
            board = IBoard.CreateBoard(parameters.board, in world);

            InitializeStatistics();
            SuscribeToStatisticsEvents();
        }

        public GameState(in GameState other)
        {
            rules = other.rules;
            councilsPassed = other.councilsPassed;
            daysPassed = other.daysPassed;
            daysRemainingBeforeNextCouncil = other.daysRemainingBeforeNextCouncil;
            world = new World(other.world);
            voting = new Voting(other.voting);
            statistics = new Statistics(other.statistics);
            board = other.board.Duplicate();
            
            SuscribeToStatisticsEvents();
        }

        public bool IsRegionReserved(int regionIndex)
        {
            return board.IsRegionReserved(in this, regionIndex);
        }



        public bool CanRealmAttackRegion(byte realmIndex, int regionIndex)
        {
            if (!world.IsValidRegionIndex(regionIndex)) {
                return default;
            }

            if (!world.IsValidRealmIndex(realmIndex)) {
                return default;
            }

            if (world.Regions[regionIndex].inert) {
                return false;
            }

            if (IsRegionReserved(regionIndex)) {
                return false;
            }

            EFactionFlag attackerFaction = world.GetRealmFaction(realmIndex);
            bool canSelfAttack = attackerFaction.HasFlagSafe(EFactionFlag.SelfAttack);

            if (world.Regions[regionIndex].IsOwnedBy(realmIndex) && !canSelfAttack) {
                return false;
            }

            if (world.Regions[regionIndex].GetOwner(out byte regionOwner)) {
                if (world.Realms[regionOwner].IsSubjugated(out byte theirOwner)) {
                    regionOwner = theirOwner;
                }

                if (world.Realms[realmIndex].IsSubjugated(out byte myOwner)) {
                    realmIndex = myOwner; // We're friends now
                }

                if (regionOwner == realmIndex && !canSelfAttack) {
                    return false;
                }
            }

            return true;
        }

        public bool GetAttackTargetsForRegionNoAlloc(int regionIndex, ERegionAttackType allowedAttackTypes, in List<AttackTarget> attackTargets)
        {
            return world.GetAttackTargetsForRegionNoAlloc(regionIndex, allowedAttackTypes, CanRealmAttackRegion, in attackTargets);
        }

        public bool GetAttackTargetsForRegion(int regionIndex, ERegionAttackType allowedTypes, out List<AttackTarget> attackTargets)
        {
            attackTargets = new();

            return GetAttackTargetsForRegionNoAlloc(regionIndex, allowedTypes, in attackTargets);
        }


        private void SuscribeToStatisticsEvents()
        {
            world.OnSilverTreasuryGained += World_OnSilverTreasuryGained;
            world.OnSilverTreasuryLost += World_OnSilverTreasuryLost;
            world.OnRegionBuilt += World_OnRegionBuilt;
            world.OnRegionDestroyed += World_OnRegionDestroyed;
            world.OnRegionConquest += World_OnRegionConquest;
            world.OnRegionStarved += World_OnRegionStarved;
        }

        private void World_OnRegionDestroyed(int regionIndex, EBuilding building, byte? destructor)
        {
            if (destructor.HasValue) {
                statistics.realms[destructor.Value].buildingsDestroyed++;
            }
        }

        private void InitializeStatistics()
        {
            statistics.realms = new Statistics.RealmStatistics[world.Realms.Count];
            statistics.regions = new Statistics.RegionStatistics[world.Regions.Count];

            List<int> territoryCache = new List<int>();
            for (byte realmIndex = 0; realmIndex < statistics.realms.Length; realmIndex++) {
                territoryCache.Clear();
                world.GetTerritoryOfRealm(realmIndex, territoryCache);

                for (int territoryRegion = 0; territoryRegion < territoryCache.Count; territoryRegion++) {
                    int regionIndex = territoryCache[territoryRegion];
                    World_OnRegionConquest(regionIndex, realmIndex, default);

                    if (world.Regions[regionIndex].Building != EBuilding.None) {
                        World_OnRegionBuilt(regionIndex, world.Regions[regionIndex].RelevantBuilding);
                    }
                }

                statistics.realms[realmIndex].minRiches = (ushort)world.GetSilverTreasury(realmIndex);
                statistics.realms[realmIndex].minTerritorySize = (ushort)territoryCache.Count;
            }
        }

        private void World_OnRegionStarved(int regionIndex, byte? newOwner, byte? previousOwner)
        {
            if (newOwner.HasValue) {
                statistics.realms[newOwner.Value].regionsGainedFromAttrition++;
            }

            if (previousOwner.HasValue) {
                statistics.realms[previousOwner.Value].regionsLostToAttrition++;
            }

            statistics.regions[regionIndex].starvations++;
        }

        private void World_OnRegionConquest(int regionIndex, byte newOwner, byte? previousOwner)
        {
            statistics.realms[newOwner].regionsGainedFromConquest++;

            if (previousOwner.HasValue) {
                statistics.realms[previousOwner.Value].regionsLostToConquest++;
            }

            statistics.regions[regionIndex].conquests++;
            statistics.regions[regionIndex].conquestsPerRealm ??= new Dictionary<byte, ushort>();
            statistics.regions[regionIndex].conquestsPerRealm.TryAdd(newOwner, 0);
            statistics.regions[regionIndex].conquestsPerRealm[newOwner]++;
        }

        private void World_OnRegionBuilt(int regionIndex, EBuilding building)
        {
            if (world.Regions[regionIndex].GetOwner(out byte owner)) {
                statistics.realms[owner].NotifyBuildingConstructed(building.GetBuildingOrRawDecoy());
            }

            statistics.regions[regionIndex].buildingsConstructed++;
        }

        private void World_OnSilverTreasuryLost(byte realmIndex, uint amount)
        {
            statistics.realms[realmIndex].silverSpent += amount;
        }

        private void World_OnSilverTreasuryGained(byte realmIndex, uint amount)
        {
            statistics.realms[realmIndex].silverGained += amount;
        }

        public void ComputeEffects(ManagedRandom random, IReadOnlyList<Transform> transformsUnordered, out ITransformEffect[] effects)
        {
            Logger.Trace($"Computing {transformsUnordered.Count} UNORDERED transforms on {this}");
            List<string> debugStrings = transformsUnordered.Select(o => o.ToString()).OrderBy((o)=>o).ToList();

            for (int i = 0; i < debugStrings.Count; i++) {
                Logger.Trace($"{i}: {debugStrings[i]}");
            }

            List<ITransformEffect> effectsList = new List<ITransformEffect>();

            List<Transform> remainingTransforms = transformsUnordered.ToList();

            // Priority buildings
            {
                var constructions = TakeConstructions(world, remainingTransforms, priorityOnly: true);
                PlayConstructions(in world, constructions, effectsList);
            }

            // Attacks
            {
                GameState attacksComputationDuplicate = Duplicate();
                ApplyEffects(effectsList, ref attacksComputationDuplicate);

                int attacksStart = effectsList.Count;

                var attacks = TakeAttacks(attacksComputationDuplicate.world, remainingTransforms);
                PlayAttacks(in attacksComputationDuplicate, random, attacks, effectsList);

                // Order subjugations AFTER attacks
                List<ITransformEffect.ConquestEffect> conquestsThatLeadToSubjugations = new List<ITransformEffect.ConquestEffect>();
                HashSet<int> queuedEffectGroup = new HashSet<int>();
                HashSet<byte> subjugationTargets = new HashSet<byte>();

                for (int i = attacksStart; i < effectsList.Count; i++) {

                    if (i < effectsList.Count-1) {
                        if (effectsList[i] is ITransformEffect.ConquestEffect conq && 
                            effectsList[i+1] is ITransformEffect.SubjugationEffect subjugation &&
                            subjugation.attackingRealmIndex ==conq.attackingRealm) {

                            queuedEffectGroup.Add(i);

                            subjugationTargets.Add(subjugation.targetRealmIndex);
                        }
                    }
                }

                for (int i = effectsList.Count-1; i >= 0; i--) {
                    if (queuedEffectGroup.Contains(i)) {
                        effectsList.Add(effectsList[i]);
                        effectsList.Add(effectsList[i + 1]);
                        effectsList.RemoveAt(i+1);
                        effectsList.RemoveAt(i);
                    }
                }

                // Cannot subjugate if you're the target of a subjugation
                effectsList.RemoveAll(o => 
                    o is ITransformEffect.SubjugationEffect sub &&
                    subjugationTargets.Contains(sub.attackingRealmIndex)
                );
            }

            // Border gore
            {
                ResolveBorderGore(random, in effectsList);
            }

            // Ongoing subjugation persistence
            {
                GameState ongoingSubjugationDuplicate = Duplicate();
                ApplyEffects(effectsList, ref ongoingSubjugationDuplicate);

                for (byte realmIndex = 0; realmIndex < world.Realms.Count; realmIndex++) {
                    if (world.Realms[realmIndex].IsUnderThreatOfSubjugation(out byte[] oldSubjugators) &&
                        ongoingSubjugationDuplicate.world.Realms[realmIndex].IsUnderThreatOfSubjugation(out byte[] newSubjugators)
                    ) {
                        for (int i = 0; i < oldSubjugators.Length; i++) {

                            byte subjugatorIndex = oldSubjugators[i];
                            if (newSubjugators.Contains(subjugatorIndex)) {
                                // situation - a subjugation was ongoing and did not complete just yet
                                // If at least one non-starved region of my subjugator-in-becoming is touching me, subjugation stalls
                                // Otherwise, it depops
                                if (ongoingSubjugationDuplicate.world.GetCapitalOfRealm(realmIndex, out int capitalRegionIndex)) {
                                    List<int> neighboringRegions = new List<int>();
                                    ongoingSubjugationDuplicate.world.GetNeighboringRegions(capitalRegionIndex, neighboringRegions);

                                    int[] links = neighboringRegions.Where(regionIndex => 
                                        ongoingSubjugationDuplicate.world.Regions[regionIndex].GetOwner(out byte regionOwner) &&
                                        ongoingSubjugationDuplicate.world.IsRealmAlliedWith(regionOwner, subjugatorIndex)
                                    ).ToArray();

                                    bool persist = links.Length > 0;
                                    ITransformEffect.PartialSubjugationEvolutionEffect evolutionEffect = new() {
                                        attackingRealmIndex = subjugatorIndex,
                                        targetRealmIndex = realmIndex,
                                        linkedAttackerRegions = links
                                    };

                                    Logger.Trace($"{(persist ? "Stalling" : "Decreasing")} ongoing subjugation status for {realmIndex} from {subjugatorIndex} because {links.Length} links exist {this}");
                                    effectsList.Add(evolutionEffect);
                                }
                            }
                        }
                    }
                }
            }

            // Construction
            {
                var constructions = TakeConstructions(world, remainingTransforms);

                GameState constructionsDuplicate = Duplicate();
                ApplyEffects(effectsList, ref constructionsDuplicate);
                PlayConstructions(in constructionsDuplicate.world, constructions, effectsList);
            }

            // Environmental turn
            {
                GameState boardDuplicate = Duplicate();
                ApplyEffects(effectsList, ref boardDuplicate);
                board.ComputeEffects(random, in boardDuplicate, in effectsList);
            }

            // Resolve border gore again in case the board has modified ownership of some parts
            ResolveBorderGore(random, in effectsList);

            // Others - We play them first to avoid visual oddities
            {
                foreach (var t in remainingTransforms) {
                    if (t is AdminUpgradeTransform adminUpgrade) {
                        effectsList.Insert(0, new ITransformEffect.AdministrationUpgradeEffect() {
                            realmIndex = adminUpgrade.realmToUpgrade,
                            silverPricePaid = adminUpgrade.silverPricePaid
                        });
                    }
                    else if (t is PayFavoursTransform payFavoursTransform) {
                        effectsList.Insert(0, new ITransformEffect.FavourPaymentEffect() {
                            realmIndex = payFavoursTransform.realmToFavour,
                            silverPricePaid = payFavoursTransform.silverPricePaid
                        });
                    }
                }
            }

            effects = effectsList.ToArray();
        }

        public void ApplyEffects(IReadOnlyList<ITransformEffect> effects, ref GameState newState)
        {
            Logger.Trace($"Applying {effects.Count} effects {this}");

            for (int i = 0; i < effects.Count; i++) {
                Logger.Trace($"{i}: {effects[i]}");

                effects[i].Apply(this, ref newState);
            }
        }

        public int GetHash()
        {
            return Extensions.Hash(
                Extensions.Hash(
                    daysPassed,
                    councilsPassed
                ),
                Extensions.Hash(world),
                Extensions.Hash(voting),
                Extensions.Hash(board)
            );
        }

        public GameState Duplicate()
        {
            GameState gameState = new (this);
            return gameState;
        }

        private void ResolveBorderGore(ManagedRandom random, in List<ITransformEffect> effectsList)
        {
            GameState borderGoreComputationDuplicate = Duplicate();
            ApplyEffects(effectsList, ref borderGoreComputationDuplicate);

            void resolveRealmsBorderGore(in GameState state, in List<ITransformEffect> effectsList, bool allowNeutralization)
            {
                byte depth = 0;
                while (true) {
                    borderGoreComputationDuplicate.ResolveRealmsBorderGore(in borderGoreComputationDuplicate.world, random, out ITransformEffect[] effects, depth, allowNeutralization: allowNeutralization);

                    if (effects.Length > 0) {
                        state.ApplyEffects(effects, ref borderGoreComputationDuplicate);
                        effectsList.AddRange(effects);
                        depth++;
                        continue;
                    }

                    break;
                }
            }

            // From conquest ( neutralization not allowed )
            resolveRealmsBorderGore(in this, in effectsList, allowNeutralization: false);

            // From hazard ( neutralization allowed )
            resolveRealmsBorderGore(in this, in effectsList, allowNeutralization: true);

            // Neutral regions can starve if they're no longer connected to the edge of the board
            // This is a "go-take" and they usually go to somebody near
            if (rules.neutralRegionStarvation) {
                byte depth = 0;
                while (true) {
                    borderGoreComputationDuplicate.ResolveNeutralBorderGore(in borderGoreComputationDuplicate.world, out ITransformEffect[] effects, depth);

                    if (effects.Length > 0) {
                        ApplyEffects(effects, ref borderGoreComputationDuplicate);
                        effectsList.AddRange(effects);
                        depth++;
                        continue;
                    }

                    break;
                }
            }
        }

        private void ResolveNeutralBorderGore(in World world, out ITransformEffect[] effects, byte depth)
        {
            List<ITransformEffect> effectsToAdd = new List<ITransformEffect>();

            // Next we solve border gore for neutral regions
            {
                List<int> isolatedNeutralRegions = new List<int>();
                List<int> unOwnedRegions = new List<int>(Enumerable.Range(0, world.Regions.Count));
                for (int i = 0; i < unOwnedRegions.Count; i++) {
                    int regionIndex = unOwnedRegions[i];
                    if (world.Regions[regionIndex].GetOwner(out _) || world.Regions[regionIndex].inert) {
                        unOwnedRegions.RemoveAt(i);
                        i--;
                        continue;
                    }
                }

                List<int> connected = new List<int>();
                List<int> neighborsBuffer = new List<int>();
                while (unOwnedRegions.Count > 0) {
                    connected.Clear();
                    int first = unOwnedRegions[0];
                    world.GetAllConnectedRegionsPreventingStarvation(first, connected);
                    unOwnedRegions.RemoveAll(connected.Contains);

                    bool isolated = true;

                    byte? ownerNeighborFound = null;

                    for (int i = 0; i < connected.Count; i++) {
                        int regionIndex = connected[i];
                        neighborsBuffer.Clear();
                        world.GetNeighboringRegions(regionIndex, neighborsBuffer);

                        if (neighborsBuffer.Count < 6) {
                            // This means it's touching a terrain border
                            isolated = false;
                        }
                        else {
                            for (int neighborIndexIndex = 0; neighborIndexIndex < neighborsBuffer.Count; neighborIndexIndex++) {
                                int neighborRegionIndex = neighborsBuffer[neighborIndexIndex];

                                if (world.IsCouncilRegion(neighborRegionIndex)) {
                                    isolated = false;
                                    break;
                                }
                                else if (world.Regions[neighborRegionIndex].inert) {
                                    isolated = false;
                                    break;
                                }
                                else if (rules.goTakeNeutralOnlyWhenNoContest && world.Regions[neighborRegionIndex].isOwned) {
                                        // Go-take of unowned regions can only be accomplished when the takeover is TOTAL
                                        // that means no contested borders of the target region group
                                    if (ownerNeighborFound.HasValue) {
                                        if (world.Regions[neighborRegionIndex].ownerIndex != ownerNeighborFound.Value) {
                                            isolated = false;
                                            break;
                                        }
                                    }
                                    else {
                                        ownerNeighborFound = world.Regions[neighborRegionIndex].ownerIndex;
                                    }
                                }
                            }
                        }

                        // If this one is not isolated, then none of the others in the chain are
                        if (isolated == false) {
                            break;
                        }
                    }

                    if (isolated) {
                        // If this is true, then ALL of those regions are to be solved!!
                        // Somebody's playing Go
                        isolatedNeutralRegions.AddRange(connected);
                    }
                }

                for (int i = 0; i < isolatedNeutralRegions.Count; i++) {
                    int regionIndex = isolatedNeutralRegions[i];
                    // Random is NOT ALLOWED!
                    if (SolveBorderGoreForRegion(world, randomOptional: null, regionIndex, out var effect, depth, allowNeutralization: true)) {
                        effectsToAdd.Add(effect);
                    }
                }
            }

            effects = effectsToAdd.ToArray();
        }

        private void ResolveRealmsBorderGore(in World world, ManagedRandom random, out ITransformEffect[] effects, byte depth, bool allowNeutralization)
        {
            List<ITransformEffect> effectsToAdd = new List<ITransformEffect>();

            for (byte i = 0; i < world.Realms.Count; i++) {
                if (world.IsCouncilRealm(i)) {
                    continue;
                }

                SolveBorderGoreForRealm(world, random, i, effectsToAdd, depth, allowNeutralization);
            }

            effects = effectsToAdd.OrderBy(o => o is ITransformEffect.ConquestEffect conquest && conquest.isACoinFlip).ToArray();
        }

        private void SolveBorderGoreForRealm(in World world,  ManagedRandom random, byte realm, in List<ITransformEffect> effects, byte depth, bool allowNeutralization)
        {
            List<int> remainingRegionsToConnect = new List<int>();
            world.GetTerritoryOfRealm(realm, remainingRegionsToConnect);

            HashSet<int> connectedRegions = new HashSet<int>(remainingRegionsToConnect.Count);

            // 1.  Identify connection central points (capital, and forts for some)
            List<int> startingPoints = new List<int>(4);
            {

                if (world.GetCapitalOfRealm(realm, out int capitalRegionIndex)) {
                    startingPoints.Add(capitalRegionIndex);
                }
                else {
                    // This means the user has no capital - normally this should never happen but whomstdve the fuck knows
                    // Early out
                    return;
                }

                if (world.GetRealmFaction(realm).HasFlagSafe(EFactionFlag.FortsCountAsCapital)) {
                    for (int i = 0; i < remainingRegionsToConnect.Count; i++) {
                        int regionIndex = remainingRegionsToConnect[i];
                        if (world.Regions[regionIndex].RelevantBuilding.HasFlagSafe(EBuilding.Fort)) {
                            startingPoints.Add(regionIndex);
                        }
                    }
                }
            }

            // 2.  Any region connected to either of these starting points is deemed Connected
            {
                for (int i = 0; i < startingPoints.Count; i++) {
                    int centralRegionIndex = startingPoints[i];
                    world.GetAllConnectedRegionsPreventingStarvation(centralRegionIndex, connectedRegions);
                }
            }

            // 3. Any region not present in the Connected list is marked for attrition
            {
                remainingRegionsToConnect.RemoveAll(connectedRegions.Contains);
            }

            // 4. We work on them in a certain order - starts with the regions that are the _most lonely_
            //  because this prevents a group of isolated regions from self-sustaining
            {
                Dictionary<int, int> isolationScoreForRegion = new Dictionary<int, int>(remainingRegionsToConnect.Count);
                ThreadLocal<List<int>> neighborsCache = new ThreadLocal<List<int>>(() => new(6));
                for (int i = 0; i < remainingRegionsToConnect.Count; i++) {
                    neighborsCache.Value.Clear();

                    int regionIndex = remainingRegionsToConnect[i];
                    world.GetNeighboringRegions(regionIndex, neighborsCache.Value);

                    int score = 0;
                    for (int neighborIndex = 0; neighborIndex < neighborsCache.Value.Count; neighborIndex++) {
                        int neighborRegionIndex = neighborsCache.Value[neighborIndex];
                        if (world.Regions[neighborRegionIndex].IsOwnedBy(realm)) {
                            // 🙅‍
                        }
                        else if(world.Regions[neighborRegionIndex].isOwned) {
                            score += 6;
                        }
                        else {
                            score += 1;
                        }
                    }

                    isolationScoreForRegion[regionIndex] = score;
                }

                // Higher score => higher priority
                remainingRegionsToConnect = remainingRegionsToConnect
                    .OrderByDescending((regionId) => { return isolationScoreForRegion[regionId]; })
                    .ThenBy((regionId) => { return regionId; })
                    .ToList();
            }

            // 5. Solve each region. If a region does not have a clear owner, coin flip it or neutralize it
            {
                for (int i = 0; i < remainingRegionsToConnect.Count; i++) {
                    int regionIndex = remainingRegionsToConnect[i];
                    
                    if (SolveBorderGoreForRegion(world, random, regionIndex, out ITransformEffect effect, depth, allowNeutralization)) {
                        effects.Add(effect);
                    }
                }
            }
        }

        private bool SolveBorderGoreForRegion(in World world, ManagedRandom randomOptional, int regionIndex, out ITransformEffect effect, byte depth, bool allowNeutralization)
        {
            effect = default;

            if (world.GetNaturalOwnerFromNeighbors(
                regionIndex, 
                randomOptional,
                discardCurrentOwner: true, 
                out byte newOwner, 
                out bool wasCoinFlip,
                out bool isTotallySurrounded)
            ) {
                if (world.Regions[regionIndex].IsOwnedBy(newOwner)) {
                    // This happens - it's okay, the next pass of solving will fix it
                }
                else {
                    effect = new ITransformEffect.StarvationEffect() {
                        hasNewOwner = true,
                        newOwningRealm = newOwner,
                        regionIndex = regionIndex,
                        waveIndex = depth,
                        wasCoinFlip = wasCoinFlip
                    };
                }
            }
            else {
                // Lose ownership
                if (world.Regions[regionIndex].isOwned && allowNeutralization) {
                    effect = new ITransformEffect.StarvationEffect() {
                        hasNewOwner = false,
                        newOwningRealm = default,
                        regionIndex = regionIndex,
                        waveIndex = depth,
                        wasCoinFlip = false
                    };
                }
            }

            return effect != default;
        }

        private void PlayAttackedRegion(in GameState state, ManagedRandom random, List<RegionAttackRegionTransform> attackOrders, in List<ITransformEffect> effects)
        {
            Logger.Trace($"Playing {attackOrders.Count} attacks on region {attackOrders[0].targetRegionIndex} : \n{string.Join('\n', attackOrders)}");

            World world = state.world;

            bool majorityAttackingRealmIsACoinFlip = false;
            List<byte> otherCoinFlippers = new List<byte>();
            byte majorityAttackingRealm = 0;
            if (attackOrders.Count == 1) {
                majorityAttackingRealm = world.Regions[attackOrders[0].AttackingRegionIndex].ownerIndex;
            }
            else {
                Dictionary<byte, byte> attacksPerRealm = new(attackOrders.Count);
                Dictionary<int, byte> regionOwner = new(attackOrders.Count);
                List<byte> potentialAttackers = new List<byte>(attacksPerRealm.Count);

                byte biggestAmountOfAttacks = 0;

                for (int i = 0; i < attackOrders.Count; i++) {
                    byte attackingRealm = world.Regions[attackOrders[i].AttackingRegionIndex].ownerIndex;
                    if (!attacksPerRealm.ContainsKey(attackingRealm)) {
                        attacksPerRealm[attackingRealm] = 0;
                        potentialAttackers.Add(attackingRealm);
                    }

                    regionOwner[attackOrders[i].AttackingRegionIndex] = attackingRealm;

                    attacksPerRealm[attackingRealm]++;

                    biggestAmountOfAttacks = System.Math.Max(biggestAmountOfAttacks, attacksPerRealm[attackingRealm]);
                }

                // These will never win
                potentialAttackers.RemoveAll((o) => attacksPerRealm[o] < biggestAmountOfAttacks);

                // random choice
                if (potentialAttackers.Count > 1) {
                    majorityAttackingRealm = potentialAttackers[random.Next(potentialAttackers.Count)];
                    majorityAttackingRealmIsACoinFlip = true;
                    otherCoinFlippers.AddRange(potentialAttackers);
                    otherCoinFlippers.Remove(majorityAttackingRealm);
                }
                else {
                    majorityAttackingRealm = potentialAttackers[0];
                }

                attacksPerRealm[majorityAttackingRealm] = byte.MaxValue;

                // Put majority attacker at the end

                var sortedOrders = attackOrders
                    .OrderBy(o => attacksPerRealm[regionOwner[o.AttackingRegionIndex]])
                    .ThenBy(o => regionOwner[o.AttackingRegionIndex])
                    .ThenBy(o => o.attackType)
                    .ToArray(); // Extended attacks at the end

                attackOrders.Clear();
                attackOrders.AddRange(sortedOrders);
            }

            // Play attack
            while (attackOrders.Count > 0) {
                List<int> attackingRegions = new List<int>();
                RegionAttackRegionTransform transform = attackOrders[0];
                byte attackOwner = world.Regions[transform.AttackingRegionIndex].ownerIndex;

                for (int i = 0; i < attackOrders.Count; i++) {
                    byte otherAttackOwner = world.Regions[attackOrders[i].AttackingRegionIndex].ownerIndex;
                    if (
                        // This is one of my attacks
                        otherAttackOwner == attackOwner ||

                        // These are part of a coin flip and must be played together
                        majorityAttackingRealmIsACoinFlip && 
                            (otherCoinFlippers.Contains(otherAttackOwner) || otherAttackOwner == majorityAttackingRealm)
                    ) {
                        attackingRegions.Add(attackOrders[i].AttackingRegionIndex);

                        attackOrders.RemoveAt(i);
                        i--;
                    }
                    else {
                        break;
                    }
                }

                if (majorityAttackingRealmIsACoinFlip) {
                    attackOwner = majorityAttackingRealm; // For faction feats & others, they're the attack owner now
                }

                Region target = world.Regions[transform.targetRegionIndex];
                EFactionFlag attackingFaction = world.GetAllianceFaction(attackOwner);
                
                ITransformEffect.ConquestEffect effect = new ITransformEffect.ConquestEffect();
                effect.regionIndex = transform.targetRegionIndex;
                effect.attackingRealm = attackOwner;
                effect.attackingRegionsIndices = attackingRegions.ToArray();
                effect.previousOwningRealm = target.isOwned ? target.ownerIndex : byte.MaxValue;
                effect.newOwningRealm = effect.previousOwningRealm;

                effect.hadBuilding = target.Building != EBuilding.None;
                effect.isACoinFlip = majorityAttackingRealmIsACoinFlip;
                effect.otherMajorityAttackersWhoLostCoinFlip = otherCoinFlippers.ToArray();

                // Extended attack is a prowess
                if (transform.attackType.HasFlagSafe(ERegionAttackType.Charge)) {
                    effect.factionHighlights |= EFactionFlag.Charge;
                }
                if (transform.attackType.HasFlagSafe(ERegionAttackType.Slithering)) {
                    effect.factionHighlights |= EFactionFlag.SlitherAttacksBetweenRegions;
                }

                if (attackOwner == target.ownerIndex && attackingFaction.HasFlagSafe(EFactionFlag.SelfAttack)) {
                    effect.factionHighlights |= EFactionFlag.SelfAttack;

                    if (rules.factions.selfAttackReimbursesBuilding && 
                        target.Building != EBuilding.None &&
                        !target.WasBuiltForFree) {
                        effect.silverLooted = target.SilverPricePaid;
                    }
                }

                if (target.CannotBeTaken(rules, attackingFaction)) {
                    // It's a fail
                }
                else if (transform.attackType == ERegionAttackType.Charge &&
                    effects.Find((o) =>
                        o is ITransformEffect.ConquestEffect conquest &&
                        conquest.Success &&
                        conquest.attackingRegionsIndices.Contains(transform.AttackingRegionIndex)
                    ) == null) {
                    // Failure - extended attack that came after a tradtional attack that failed
                }
                else {
                    if (target.IsReinforcedAgainstAttack(rules, attackingFaction)) {

                        int attacks = attackingRegions.Count();

                        if (attacks > 1) {
                            effect.newOwningRealm = attackOwner;
                        }
                    }
                    else {
                        effect.newOwningRealm = attackOwner;
                    }
                }

                bool canLoot = !effect.Success && target.RelevantBuilding != EBuilding.None;
                if (target.Building.HasFlagSafe(EBuilding.Fort)
                    && effect.Success
                    && attackingFaction.HasFlagSafe(EFactionFlag.ConqueredFortsGivePayout)) {

                    effect.silverLooted = this.rules.factions.conqueredFortPayout;
                    canLoot = true;
                    effect.factionHighlights |= EFactionFlag.ConqueredFortsGivePayout;
                }
                else {
                    effect.silverLooted = canLoot ?
                        world.GetRegionLootableSilverWorth(transform.targetRegionIndex, attackOwner) * attackingRegions.Count :
                        0;
                }

                // Building capture is a prowess
                if (effect.Success &&
                    target.RelevantBuilding != EBuilding.None &&
                    attackingFaction.HasFlagSafe(EFactionFlag.ConquestBuilding) &&
                    target.RelevantBuilding != EBuilding.Capital
                    ) {
                    effect.factionHighlights |= EFactionFlag.ConquestBuilding;
                }

                // Epic loot from a mighty quest
                if (effect.silverLooted > 0 && attackingFaction.HasFlagSafe(EFactionFlag.LootMoreMoney)) {
                    effect.factionHighlights |= EFactionFlag.LootMoreMoney;
                }

                effects.Add(effect);

                // Subjugation
                bool canSubjugate = rules.subjugationForAll
                    || (rules.factions.vassalsCanSubjugate ? 
                        attackingFaction.HasFlagSafe(EFactionFlag.Subjugate) :
                        world.GetRealmFaction(attackOwner).HasFlagSafe(EFactionFlag.Subjugate)
                    );

                if (canSubjugate && 
                    target.isOwned &&
                    effect.Success && 
                    target.RelevantBuilding.HasFlagSafe(EBuilding.Capital)) {

                    var subjugation = new ITransformEffect.SubjugationEffect() {
                        attackingRealmIndex = attackOwner,
                        targetRealmIndex = effect.previousOwningRealm,
                        isFactionHighlight = !rules.subjugationForAll
                    };

                    effects.Add(subjugation);
                }

                if (state.board is BeastWorldBoard beastWorld) {
                    beastWorld.GetConquestEffects(in state, in effect, in effects);
                }
            }
        }

        private void PlayAttacks(in GameState state, ManagedRandom random, List<RegionAttackRegionTransform> remainingAttacks, in List<ITransformEffect> effects)
        {
            Logger.Trace($"Playing {remainingAttacks.Count} attacks: \n{string.Join('\n', remainingAttacks)}");
            
            World world = state.world;

            while (remainingAttacks.Count > 0) {
                // Play attack

                RegionAttackRegionTransform attack = remainingAttacks[0];

                Region attacking = world.Regions[attack.targetRegionIndex];

                List<RegionAttackRegionTransform> attacksOnSameRegion;

                if (attack.attackType == ERegionAttackType.Charge) {
                    attacksOnSameRegion = new List<RegionAttackRegionTransform>() { attack };
                    remainingAttacks.RemoveAt(0);
                }
                else {
                    attacksOnSameRegion =
                        remainingAttacks.FindAll(o => o.targetRegionIndex == attack.targetRegionIndex);
                    remainingAttacks.RemoveAll(attacksOnSameRegion.Contains);
                }

                PlayAttackedRegion(in state, random, attacksOnSameRegion, effects);
            }
        }

        private void PlayConstructions(in World world, List<RegionBuildTransform> remainingBuilds, in List<ITransformEffect> effects)
        {
            Logger.Trace($"Playing {remainingBuilds.Count} builds: \n{string.Join('\n', remainingBuilds)}");
            while (remainingBuilds.Count > 0) {
                // Play attack

                RegionBuildTransform build = remainingBuilds[0];
                remainingBuilds.RemoveAt(0);

                byte supposedOwner = build.constructingRealmIndex;
                effects.Add(new ITransformEffect.ConstructionEffect() {
                    building = build.building,
                    regionIndex = build.actingRegionIndex,
                    forOwner = supposedOwner,
                    silverPricePaid = build.SilverCost,
                    isSuccessful = world.IsActionableRegion(supposedOwner, build.actingRegionIndex),
                    isFactionHighlight =
                        build.IsPrioritized(in world) ||
                        (build.silverCost == 0 && world.GetRealmFaction(supposedOwner).HasFlagSafe(EFactionFlag.FirstBuildingIsFree)) ||
                        (build.building.IsDecoy() && world.GetRealmFaction(supposedOwner).HasFlagSafe(EFactionFlag.CanBuildDecoys))
                });
            }
        }

        private List<RegionBuildTransform> TakeConstructions(World world, List<Transform> transforms, bool priorityOnly = false)
        {
            List<RegionBuildTransform> builds =
                   transforms
                       .Where(o => o is RegionBuildTransform build && (!priorityOnly || build.IsPrioritized(world)))
                       .Select(o => o as RegionBuildTransform)
                       .ToList();

            transforms.RemoveAll((o) => builds.Contains(o));

            builds = builds
                    .OrderBy((o) =>
                    {
                        Position position = world.Position(o.actingRegionIndex);
                        return position.SquaredDistanceWith(default);
                    })
                    .ThenBy((o) =>
                    {
                        return o.UniqueID;
                    })
                    .ToList();

            return builds.ToList();
        }

        private List<RegionAttackRegionTransform> TakeAttacks(World world, List<Transform> transforms)
        {

            List<RegionAttackRegionTransform> attacks =
                transforms
                    .Where(o => o is RegionAttackRegionTransform)
                    .Select(o => o as RegionAttackRegionTransform)
                    .ToList();

            transforms.RemoveAll((o) => attacks.Contains(o));

            attacks = attacks
                    .OrderBy((o) =>
                    {
                        return o.attackType; // Extended attacks at the very last
                    })
                    .ThenBy((o) =>
                    {
                        // Other identical attacks
                        return attacks.Count((r) => r.targetRegionIndex == o.targetRegionIndex);
                    })
                    .ThenBy((o) =>
                    {
                        // wave effect, attack starts at the top-left
                        Position position = world.Position(o.targetRegionIndex);
                        return position.SquaredDistanceWith(default);
                    })
                    .ThenBy((o) =>
                    {
                        return o.UniqueID;
                    })
                    .ToList();

            return attacks;
        }

        public override string ToString()
        {
            return $"GameState ({daysPassed} days, {councilsPassed} councils)";
        }

        public void Write(BinaryWriter into)
        {
            into.Write(VERSION);
            into.Write(daysPassed);
            into.Write(councilsPassed);
            into.Write(daysRemainingBeforeNextCouncil);
            into.Write(world);
            into.Write(voting);
            into.Write(statistics);
        }

        public void Read(BinaryReader from)
        {
            byte version = from.ReadByte();

            daysPassed = from.ReadInt32();
            councilsPassed = from.ReadInt32();
            daysRemainingBeforeNextCouncil = from.ReadInt32();

            world = World.Empty();
            world.Read(version, from);

            voting = new Voting(rules);
            voting.Read(version, from);

            statistics = new Statistics();
            statistics.Read(version, from);
        }
    }
}