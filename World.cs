
namespace LouveSystems.K2.Lib
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    public struct World : IBinarySerializableWithVersion
    {
        private const byte OPTIMAL_REALM_SIZE = 1;

        public delegate bool CanRealmAttackRegionDelegate(byte realmIndex, int regionIndex);

        internal delegate void RegionStarvationDelegate(int regionIndex, byte? newOwner, byte? previousOwner);
        internal delegate void RegionConquestDelegate(int regionIndex, byte newOwner, byte? previousOwner);
        internal delegate void RegionBuiltDelegate(int regionIndex, EBuilding building);
        internal delegate void RegionDestroyedDelegate(int regionIndex, EBuilding building, byte? destructor);
        internal delegate void RegionNeutralizationDelegate(int regionIndex);
        internal event RegionStarvationDelegate OnRegionStarved;
        internal event RegionConquestDelegate OnRegionConquest;
        internal event RegionBuiltDelegate OnRegionBuilt;
        internal event RegionDestroyedDelegate OnRegionDestroyed;
        internal event RegionNeutralizationDelegate OnRegionNeutralizedFromHazard;

        internal delegate void SilverTreasuryChangedDelegate(byte realmIndex, uint amount);
        internal event SilverTreasuryChangedDelegate OnSilverTreasuryGained;
        internal event SilverTreasuryChangedDelegate OnSilverTreasuryLost;

        private struct AxialPosition
        {
            public int q;
            public int r;

            public AxialPosition(int q, int r)
            {
                this.q = q;
                this.r = r;
            }

            public AxialPosition(Position p)
            {
                q = p.x - (p.y - (p.y & 1)) / 2;
                r = p.y;
            }

            public Position ToPosition()
            {
                var x = q + (r - (r & 1)) / 2;
                var y = r;
                return new Position(x, y);
            }

            public static AxialPosition operator +(AxialPosition left, AxialPosition right)
            {
                return new AxialPosition(left.q + right.q, left.r + right.r);
            }

            public static AxialPosition operator *(AxialPosition left, int right)
            {
                return new AxialPosition(left.q * right, left.r * right);
            }
        }

        private static readonly IReadOnlyList<AxialPosition> AxialDirectionVectors = new AxialPosition[] {
            new AxialPosition(+1,  0),
            new AxialPosition( 0, +1),
            new AxialPosition(+1, -1),
            new AxialPosition(-1,  0),
            new AxialPosition( 0, -1),
            new AxialPosition(-1, +1),
        };

        private static readonly IReadOnlyList<AxialPosition> AxialSlitherAttackVectors = new AxialPosition[] {
            new AxialPosition(+2, -1),
            new AxialPosition(+1, -2),
            new AxialPosition(-1, -1),
            new AxialPosition(-2, +1),
            new AxialPosition(-1, +2),
            new AxialPosition(+1, +1),
        };

        public IReadOnlyList<Region> Regions => regions;
        public IReadOnlyList<Realm> Realms => realms;

        public byte SideLength => sideLength;

        public byte SquareSideLength => squareSideLength;


        private Region[] regions;

        private Realm[] realms;

        private byte sideLength;

        private byte squareSideLength;

        private readonly GameRules rules;

        private byte? councilRealmIndex;

        public World(in PartySessionInitializationParameters playerParams, in GameRules parameters) : this()
        {
            this.rules = parameters;

            bool hasCouncilRealm = parameters.board.type == EBoardType.CouncilRegion;

            int realmCountWithoutCouncil = parameters.additionalRealmsCount + playerParams.realmsToInitialize.Length;
            int realmCount = realmCountWithoutCouncil + (hasCouncilRealm ? 1 : 0);

            sideLength = CalculateSideLength(
                parameters,
                realmCountWithoutCouncil,
                out squareSideLength
            );

            regions = new Region[sideLength * sideLength];
            realms = new Realm[realmCount];

            EatColumnsRows();
            EatCorners();

            bool isRealmCountPowerOfTwo = (realmCountWithoutCouncil & (realmCountWithoutCouncil - 1)) == 0;
            Lib.Position[] positions = isRealmCountPowerOfTwo ?
                GetPossibleGridAlignedPositions() :
                GetPossibleRotationaryPositions(realmCountWithoutCouncil);

            if (positions.Length < realmCountWithoutCouncil) {
                throw new System.Exception($"Map is too small (needed to create {realms.Length} realms and have only {positions.Length}, square side is {squareSideLength})");
            }

            if (hasCouncilRealm) {
                Lib.Position middle = new Position(sideLength / 2, sideLength / 2);
                Lib.Position councilPosition = middle;
                int posIndex = -1;

                for (int i = 0; i < positions.Length; i++) {
                    if (positions[i] == middle) {
                        councilPosition = positions[i];
                        posIndex = i;
                    }
                }

                if (posIndex >= 0) {
                    positions[posIndex] = positions[^1];
                    positions[^1] = councilPosition;
                }
                else {
                    // Add a position just for council
                    Lib.Position[] newPositions = new Position[positions.Length + 1];
                    positions.CopyTo(newPositions, 0);
                    newPositions[positions.Length] = middle;
                    positions = newPositions;
                }
            }

            Lib.Position[] startingPositions = positions;

            byte realmsToPosition = (byte)realms.Length;
            if (hasCouncilRealm) {
                byte i = (byte)(realms.Length - 1);
                councilRealmIndex = i;
                InitializeCouncilRealm(i, startingPositions[^1]);
                realmsToPosition--;
            }

            bool teamBased = playerParams.realmsToInitialize.Any(o => o.initialSubjugatorPlayerId.HasValue);
            if (teamBased) {
                // Spawn teammates close to each other
                Dictionary<byte, int> teamSizes = new();
                for (byte i = 0; i < playerParams.realmsToInitialize.Length; i++) {
                    int teamSize = 1;

                    for (byte j = 0; j < playerParams.realmsToInitialize.Length; j++) {
                        if (j == i) {
                            continue;
                        }

                        if (playerParams.realmsToInitialize[j].initialSubjugatorPlayerId == playerParams.realmsToInitialize[i].forPlayerId) {
                            teamSize++;
                        }
                    }

                    teamSizes[i] = teamSize;
                }

                byte[] orderedRealmsIndicesToSpawn = 
                    Enumerable.Range(0, realmsToPosition)
                    .Select(o=>(byte)o)
                    .OrderBy(o=>teamSizes[o])
                    .ToArray();


                for (byte i = 0; i < orderedRealmsIndicesToSpawn.Length; i++) {
                    InitializeRealm(orderedRealmsIndicesToSpawn[i], startingPositions[i], rules.initialRealmsSize);
                }
            }
            else {
                for (byte i = 0; i < realmsToPosition; i++) {
                    InitializeRealm(i, startingPositions[i], rules.initialRealmsSize);
                }
            }
        }

        public World(in World other) : this()
        {
            regions = new Region[other.regions.Length];
            other.regions.CopyTo(regions, 0);

            realms = new Realm[other.realms.Length];
            other.realms.CopyTo(realms, 0);

            rules = other.rules;

            sideLength = other.sideLength;
            squareSideLength = other.squareSideLength;
            councilRealmIndex = other.councilRealmIndex;
        }

        public static World Empty()
        {
            return new World(new GameRules());
        }

        private World(GameRules rules) : this()
        {
            this.rules = rules;
        }

        public void Modify(out Region[] regions, out Realm[] realms)
        {
            regions = this.regions;
            realms = this.realms;
        }

        public void NeutralizeRegionFromHazard(int regionIndex)
        {
            bool hadOwner = regions[regionIndex].GetOwner(out byte previousOwner);

            if (regions[regionIndex].Building != EBuilding.None) {
                OnRegionDestroyed?.Invoke(regionIndex, regions[regionIndex].Building, default);
                regions[regionIndex].RemoveBuilding();
            }

            regions[regionIndex].isOwned = false;

            OnRegionNeutralizedFromHazard?.Invoke(regionIndex);
        }

        public void StarveRegion(int regionIndex, bool hasNewOwner, byte newOwningRealm)
        {
            bool hadOwner = regions[regionIndex].GetOwner(out byte previousOwner);

            if (hasNewOwner) {
                bool keepBuilding = true;
                if (GetRealmFaction(newOwningRealm).HasFlagSafe(EFactionFlag.ConquestBuilding) ||
                    !rules.goTakeDestroysBuildings) {
                    // Keep building
                }
                else {
                    keepBuilding = false;
                }

                TakeOwnershipOfRegion(regionIndex, newOwningRealm, keepBuilding);
            }
            else {
                regions[regionIndex].isOwned = false;
            }

            OnRegionStarved?.Invoke(
                regionIndex,
                hasNewOwner ? newOwningRealm : default,
                hadOwner ? previousOwner : default
            );
        }

        public void ConquestRegion(int regionIndex, byte newOwningRealm)
        {
            bool hadOwner = regions[regionIndex].GetOwner(out byte previousOwner);

            bool keepBuilding =
                GetRealmFaction(newOwningRealm).HasFlagSafe(EFactionFlag.ConquestBuilding)
                && !IsActionableRegion(newOwningRealm, regionIndex);

            TakeOwnershipOfRegion(regionIndex, newOwningRealm, keepBuilding);

            OnRegionConquest?.Invoke(
                regionIndex,
                newOwningRealm,
                hadOwner ? previousOwner : null
            );
        }

        private void TakeOwnershipOfRegion(int regionIndex, byte newOwningRealm, bool keepBuilding)
        {
            if (!keepBuilding && regions[regionIndex].Building != EBuilding.None) {
                OnRegionDestroyed?.Invoke(regionIndex, regions[regionIndex].Building, newOwningRealm);
                regions[regionIndex].RemoveBuilding();
            }

            regions[regionIndex].ownerIndex = newOwningRealm;
            regions[regionIndex].isOwned = true;

        }

        public void ConstructBuilding(int regionIndex, EBuilding building, int silverCostPaid)
        {
            this.regions[regionIndex].AddBuilding(building, silverCostPaid);

            OnRegionBuilt?.Invoke(regionIndex, building);
        }

        public void SetRealmFavoured(byte realmIndex, bool favoured)
        {
            this.realms[realmIndex].isFavoured = favoured;
        }

        public void ClearBeastAttacks(byte realmIndex)
        {
            this.realms[realmIndex].beastsAttacked = 0;
        }

        public void NotifyBeastAttacked(byte realmIndex, byte attackCount = 1)
        {
            this.realms[realmIndex].beastsAttacked += attackCount;
        }

        public void CancelPartialSubjugation(byte attackingRealmIndex, byte targetRealmIndex)
        {
            realms[targetRealmIndex].RemoveSubjugatingAttackFrom(attackingRealmIndex);
        }

        public bool AttemptSubjugation(byte attackingRealmIndex, byte targetRealmIndex)
        {
            if (IsValidRealmIndex(attackingRealmIndex) && IsValidRealmIndex(targetRealmIndex)) {

                if (Realms[attackingRealmIndex].IsSubjugated(out byte newOwner)) {
                    // Subjugator takes the subjugated conquests! It spreads!
                }
                else {
                    newOwner = attackingRealmIndex;
                }


                ref Realm targetRealm = ref realms[targetRealmIndex];

                {
                    if (targetRealm.IsSubjugated(out byte subjugator) && subjugator == newOwner) {
                        return false; // Not supposed to happen
                    }
                }

                targetRealm.AddSubjugatingAttackFrom(newOwner);

                Logger.Trace($"Realm {targetRealmIndex} ({realms[targetRealmIndex]}) has received 1 subjugating attack from {newOwner} ({realms[newOwner]})");

                if (targetRealm.GetSubjugatingAttacksReceived(newOwner) >= rules.factions.subjugationAttacksRequired) {
                    SubjugateRealm(attackingRealmIndex, targetRealmIndex);
                    return true;
                }
            }

            return false;
        }

        public void SubjugateRealm(byte attackingRealmIndex, byte targetRealmIndex)
        {
            if (IsValidRealmIndex(attackingRealmIndex) && IsValidRealmIndex(targetRealmIndex)) {

                if (Realms[attackingRealmIndex].IsSubjugated(out byte newOwner)) {
                    // Subjugator takes the subjugated conquests! It spreads!
                }
                else {
                    newOwner = attackingRealmIndex;
                }

                ref Realm targetRealm = ref realms[targetRealmIndex];
                targetRealm.isSubjugated = true;
                targetRealm.subjugatedBy = newOwner;

                ref Realm newOwningRealm = ref realms[newOwner];
                newOwningRealm.silverTreasury += targetRealm.silverTreasury;
                targetRealm.silverTreasury = 0;

                newOwningRealm.isFavoured |= targetRealm.isFavoured;
                targetRealm.isFavoured = false;

                targetRealm.ClearSubjugatingAttacks();

                Logger.Trace($"Realm {targetRealmIndex} ({realms[targetRealmIndex]}) is now subjugated by realm {newOwner} ({realms[newOwner]})");

                // Also avoid nesting subjugations
                for (int i = 0; i < realms.Length; i++) {
                    if (realms[i].IsSubjugated(out byte subjugator) && subjugator == targetRealmIndex) {
                        // They're mine nows!
                        realms[i].subjugatedBy = newOwner;
                        Logger.Trace($"Realm {i} ({realms[i]} is now ALSO subjugated by realm {newOwner} ({realms[newOwner]}) because their prior subjugator was realm {targetRealmIndex}, who's getting subjugated right now");
                    }
                }

            }
        }

        public bool IsCompletelySubjugated(out byte realmIndex)
        {
            HashSet<byte> subjugators = new HashSet<byte>(realms.Length);
            List<byte> independentRealms = new List<byte>(realms.Length);
            realmIndex = default;
            for (byte i = 0; i < realms.Length; i++) {

                if (IsCouncilRealm(i)) {
                    continue;
                }

                if (realms[i].IsSubjugated(out byte subjugator)) {
                    subjugators.Add(subjugator);
                }
                else {
                    independentRealms.Add(i);

                    if (independentRealms.Count > 1) {
                        return false;
                    }
                }
            }

            if (independentRealms.Count == 1) {
                if (subjugators.Count == 1) {
                    if (subjugators.Contains(independentRealms[0])) {
                        realmIndex = independentRealms[0];
                        return true;
                    }
                }
            }

            return false;
        }

        public void IncreaseMaxDecisions(byte realmIndex, int byAmount = 1)
        {
            realms[realmIndex].availableDecisions = realms[realmIndex].availableDecisions + byAmount;
        }

        public void AddSilverTreasury(byte realmIndex, int amount)
        {
            int treasury = GetSilverTreasury(realmIndex);
            SetSilverTreasury(realmIndex, treasury + amount);
        }

        public void SetSilverTreasury(byte realmIndex, int treasury)
        {
            if (!IsValidRealmIndex(realmIndex)) {
                return;
            }

            byte target = realmIndex;
            if (realms[realmIndex].IsSubjugated(out byte subjugator)) {
                target = subjugator;
            }

            // Statistics
            {
                int delta = realms[target].silverTreasury - treasury;

                if (delta < 0) {
                    OnSilverTreasuryLost?.Invoke(target, (uint)(-delta));
                }
                else if (delta > 0) {
                    OnSilverTreasuryGained?.Invoke(target, (uint)(delta));
                }
            }

            realms[target].silverTreasury = treasury;
        }

        public int GetSilverTreasury(byte realmIndex)
        {
            if (!IsValidRealmIndex(realmIndex)) {
                return default;
            }

            if (realms[realmIndex].IsSubjugated(out byte subjugator)) {
                return realms[subjugator].silverTreasury;
            }

            return realms[realmIndex].silverTreasury;
        }

        private static byte CalculateSideLength(GameRules rules, int realmCountWithoutCouncil, out byte squareSideLength)
        {
            // 1,2,3,4 => 2
            // 5,6,7,8,9 => 3

            // Small hack to bump if we have 9 realms on a 3v3 grid so that the council can have the central space
            if (realmCountWithoutCouncil == 9 && rules.board.type == EBoardType.CouncilRegion) {
                realmCountWithoutCouncil++;
            }

            squareSideLength = (byte)Math.Ceiling(Math.Sqrt(realmCountWithoutCouncil));

            squareSideLength = Math.Max((byte)2, squareSideLength);

            int sideLength = rules.initialSafetyMarginBetweenRealms + (1 + rules.initialSafetyMarginBetweenRealms + /*rules.initialRealmsSize*/ 1 * 2) * squareSideLength;

            return (byte)sideLength;
        }

        public bool IsCouncilRegion(int regionIndex) => councilRealmIndex.HasValue
            && IsValidRegionIndex(regionIndex)
            && this.regions[regionIndex].IsOwnedBy(councilRealmIndex.Value);

        public bool IsCouncilRealm(byte realmIndex) => councilRealmIndex == realmIndex;

        public bool IsRealmExcludedFromVoting(byte realmIndex)
        {
            if (!IsValidRealmIndex(realmIndex)) {
                return default;
            }

            return IsCouncilRealm(realmIndex) || this.realms[realmIndex].isSubjugated;
        }

        public bool CanSeePlannedAttacksOf(byte realmIndex, byte otherRealm)
        {
            if (realmIndex == otherRealm) {
                return true;
            }

            if (IsRealmAlliedWith(realmIndex, otherRealm)) {
                return true;
            }

            return false;
        }


        public bool CanSeePlannedConstructionsOf(byte realmIndex, byte otherRealm)
        {
            if (realmIndex == otherRealm) {
                return true;
            }

            if (GetAllianceFaction(realmIndex).HasFlagSafe(EFactionFlag.SeeEnemyPlannedConstructions)) {
                return true;
            }

            if (IsRealmAlliedWith(realmIndex, otherRealm)) {
                return true;
            }

            return false;
        }

        public bool IsRealmAlliedWith(byte realmIndex, byte otherRealm)
        {
            if (realmIndex == otherRealm) {
                return true;
            }

            realmPoolBuffer.Clear();
            GetAllianceRealms(realmIndex, realmPoolBuffer);
            if (realmPoolBuffer.Contains(otherRealm)) {
                return true;
            }

            return false;
        }

        public bool IsActionableRegion(byte realmIndex, int regionIndex)
        {
            if (!IsValidRegionIndex(regionIndex) || !IsValidRealmIndex(realmIndex)) {
                return false;
            }

            return
                // Personal ownership
                Regions[regionIndex].GetOwner(out byte ownerIndex) &&
                IsRealmAlliedWith(ownerIndex, realmIndex);
        }

        public EFactionFlag GetRealmFaction(byte realmIndex)
        {
            return this.rules.factions.factionFlags[realms[realmIndex].factionIndex].flag;
        }

        public EFactionFlag GetAllianceFaction(byte realmIndex)
        {
            if (!IsValidRealmIndex(realmIndex)) {
                return default;
            }

            EFactionFlag faction = default;

            realmPoolBuffer.Clear();
            GetAllianceRealms(realmIndex, realmPoolBuffer); // This includes me

            for (byte i = 0; i < realmPoolBuffer.Count; i++) {
                byte alliedRealmIndex = realmPoolBuffer[i];
                faction |= GetRealmFaction(alliedRealmIndex);

            }

            return faction;
        }

        public bool GetRegionFactionIndex(int regionIndex, out byte factionIndex)
        {
            if (IsValidRegionIndex(regionIndex) &&
                regions[regionIndex].GetOwner(out byte realmIndex)) {
                factionIndex = realms[realmIndex].factionIndex;
                return true;
            }

            factionIndex = default;
            return false;
        }

        public EFactionFlag GetRegionAllianceFaction(int regionIndex)
        {
            if (!IsValidRegionIndex(regionIndex)) {
                return default;
            }

            if (regions[regionIndex].GetOwner(out byte realmIndex)) {
                return GetAllianceFaction(realmIndex);
            }

            return EFactionFlag.None;
        }

        public EFactionFlag GetRegionFaction(int regionIndex)
        {
            if (IsValidRegionIndex(regionIndex) && regions[regionIndex].GetOwner(out byte realmIndex)) {
                return GetRealmFaction(realmIndex);
            }

            return EFactionFlag.None;
        }

        public int GetRegionSilverWorth(int regionIndex)
        {
            if (!IsValidRegionIndex(regionIndex)) {
                return default;
            }

            EFactionFlag faction = GetRegionFaction(regionIndex);
            return regions[regionIndex].GetSilverWorth(faction, rules);
        }

        public int GetRegionLootableSilverWorth(int regionIndex, byte lootingRealm)
        {
            if (!IsValidRegionIndex(regionIndex)) {
                return default;
            }

            int silver;
            EFactionFlag lootingRealmFaction = GetRealmFaction(lootingRealm);

            if (regions[regionIndex].RelevantBuilding.HasFlagSafe(EBuilding.Capital) &&
                regions[regionIndex].CannotBeTaken(rules, lootingRealmFaction)) {
                silver = rules.silverLootedOnCapital;
            }
            else {
                silver = GetRegionSilverWorth(regionIndex);
            }

            if (lootingRealmFaction.HasFlagSafe(EFactionFlag.LootMoreMoney)) {
                if (silver < rules.factions.looterMinimumSilver) {

                    silver = rules.factions.looterMinimumSilver;
                    if (GetRegionFaction(regionIndex).HasFlagSafe(EFactionFlag.RicherTerritories)) {
                        silver *= rules.factions.looterRichesMultiplier;
                    }
                }
                else {

                }
            }

            return silver;
        }

        public bool GetAttackTargetsForRegionNoAlloc(int regionIndex, ERegionAttackType allowedAttackTypes, CanRealmAttackRegionDelegate canRealmAttackRegion, in List<AttackTarget> attackTargets)
        {
            if (!IsValidRegionIndex(regionIndex)) {
                return default;
            }

            if (regions[regionIndex].inert)
                return false;

            int countBefore = attackTargets.Count;

            int range = 1;

            EFactionFlag attackingFaction = GetRegionFaction(regionIndex);

            if (allowedAttackTypes.HasFlagSafe(ERegionAttackType.Charge) &&
                attackingFaction.HasFlagSafe(EFactionFlag.Charge)) {
                range = 2;
            }

            Position position = Position(regionIndex);
            AxialPosition axialCenter = new AxialPosition(position);

            for (int i = 0; i < AxialDirectionVectors.Count; i++) {
                AxialPosition direction = AxialDirectionVectors[i];
                for (int distance = 1; distance <= range; distance++) {
                    Position neighborPosition = (axialCenter + direction * distance).ToPosition();

                    if (!IsValidPosition(neighborPosition)) {
                        break;
                    }

                    int neighborIndex = Index(neighborPosition);
                    ERegionAttackType type = distance == 1 ? ERegionAttackType.Standard : ERegionAttackType.Charge;

                    if (!canRealmAttackRegion(regions[regionIndex].ownerIndex, neighborIndex)) {
                        break;
                    }

                    attackTargets.Add(new AttackTarget(neighborIndex, type));
                }
            }

            if (allowedAttackTypes.HasFlagSafe(ERegionAttackType.Slithering) &&
                attackingFaction.HasFlagSafe(EFactionFlag.SlitherAttacksBetweenRegions)) {

                AxialPosition pos = new AxialPosition(Position(regionIndex));

                for (int i = 0; i < AxialSlitherAttackVectors.Count; i++) {
                    AxialPosition slitherTarget = pos + AxialSlitherAttackVectors[i];
                    int slitherTargetIndex = Index(slitherTarget.ToPosition());
                    if (IsValidRegionIndex(slitherTargetIndex) && canRealmAttackRegion(regions[regionIndex].ownerIndex, slitherTargetIndex)) {

                        bool isValidSlither = false;

                        // Only if they have a neighbor in common
                        var targetNeighbors = GetNeighboringRegions(slitherTargetIndex);
                        var originNeighbors = GetNeighboringRegions(regionIndex);
                        var commonNeighbors = targetNeighbors.Where(o => originNeighbors.Contains(o)).ToArray();
                        for (int commonNeighborIndex = 0; commonNeighborIndex < commonNeighbors.Length; commonNeighborIndex++) {
                            int neighborRegionIndex = commonNeighbors[commonNeighborIndex];

                            // Only allow slithering if there's an enemy fort between origin and destination
                            if (!regions[neighborRegionIndex].IsOwnedBy(regions[regionIndex].ownerIndex)
                                && regions[neighborRegionIndex].RelevantBuilding != EBuilding.None
                                && !IsCouncilRegion(neighborRegionIndex)
                            ) {
                                isValidSlither = true;
                                break;
                            }
                        }

                        if (isValidSlither) {
                            attackTargets.Add(new AttackTarget(slitherTargetIndex, ERegionAttackType.Slithering));
                        }
                    }
                }
            }

            return attackTargets.Count > countBefore;
        }


        public bool GetNaturalOwnerFromNeighbors(int regionIndex, ManagedRandom randomOptional, bool discardCurrentOwner, out byte newOwner, out bool wasCoinFlip, out bool isTotallySurrounded)
        {
            if (!IsValidRegionIndex(regionIndex)) {
                newOwner = default;
                wasCoinFlip = default;
                isTotallySurrounded = default;
                return default;
            }

            int[] neighbors = GetNeighboringRegions(regionIndex);

            int maxOwnedConnections = 0;
            int ownedNeighbors = 0;

            Dictionary<byte, int> neighboringConnections = new Dictionary<byte, int>(neighbors.Length);

            for (int i = 0; i < neighbors.Length; i++) {
                if (regions[neighbors[i]].isOwned) {
                    byte owner = regions[neighbors[i]].ownerIndex;

                    if (IsCouncilRealm(owner)) {
                        continue;
                    }

                    if (discardCurrentOwner && Regions[regionIndex].IsOwnedBy(owner)) {
                        continue;
                    }

                    if (neighboringConnections.ContainsKey(owner)) {
                        neighboringConnections[owner]++;
                    }
                    else {
                        neighboringConnections.Add(owner, 1);
                    }

                    maxOwnedConnections = Math.Max(maxOwnedConnections, neighboringConnections[owner]);
                    ownedNeighbors++;
                }
            }

            List<byte> potentialOwners = new List<byte>();
            foreach (var kv in neighboringConnections) {
                if (kv.Value >= maxOwnedConnections) {

                    if (IsCouncilRealm(kv.Key)) { // should not be necessary and yet ??
                        continue;
                    }

                    potentialOwners.Add(kv.Key);
                }
            }
            potentialOwners.Sort(); // necessary to avoid relying on Dictionnary<> enumeration order

            newOwner = default;
            wasCoinFlip = false;
            if (potentialOwners.Count > 0) {
                if (potentialOwners.Count > 1) {
                    // Null means random is not allowed for resolution here
                    if (randomOptional == null) {
                        potentialOwners.Clear();
                    }
                    else {
                        newOwner = potentialOwners[randomOptional.Next(potentialOwners.Count)];
                        wasCoinFlip = true;
                    }
                }
                else {
                    newOwner = potentialOwners[0];
                }
            }

            isTotallySurrounded = potentialOwners.Count == 1 && ownedNeighbors == maxOwnedConnections;

            return potentialOwners.Count > 0;
        }

        public void GetTerritoryOfRealm(byte realmIndex, in List<int> regions, Predicate<Region> filter, bool includeSubjugated = false)
        {
            GetTerritoryOfRealm(realmIndex, regions);

            for (int i = 0; i < regions.Count; i++) {
                if (!filter(this.regions[regions[i]])) {
                    regions.RemoveAt(i);
                    i--;
                }
            }
        }

        private static readonly List<byte> realmPoolBuffer = new List<byte>();


        public void GetTerritoryOfRealm(byte realmIndex, in List<int> regions)
        {
            GetTerritoryOfRealm(realmIndex, regions, includeSubjugated: false);
        }

        public void GetTerritoryOfRealm(byte realmIndex, in List<int> regions, bool includeSubjugated)
        {
            if (!IsValidRealmIndex(realmIndex)) {
                return;
            }

            if (includeSubjugated) {
                realmPoolBuffer.Clear();
                GetAllianceRealms(realmIndex, realmPoolBuffer);

                // Add up territories
                for (int i = 0; i < realmPoolBuffer.Count; i++) {
                    GetTerritoryOfRealm(realmPoolBuffer[i], regions, includeSubjugated: false);
                }
            }
            else {
                for (int i = 0; i < Regions.Count; i++) {
                    if (Regions[i].IsOwnedBy(realmIndex)) {
                        regions.Add(i);
                    }
                }
            }
        }

        public void GetAllianceRealms(byte realmIndex, in List<byte> realmPool)
        {
            if (!IsValidRealmIndex(realmIndex)) {
                return;
            }

            realmPool.Add(realmIndex);

            bool isSubjugated = realms[realmIndex].IsSubjugated(out byte myRuler);
            if (isSubjugated) {
                realmPool.Add(myRuler);
            }

            for (byte i = 0; i < realms.Length; i++) {
                if (i == realmIndex) {
                    continue;
                }

                bool isAllied = false;
                if (realms[i].IsSubjugated(out byte subjugator)) {
                    if (subjugator == realmIndex) {
                        isAllied = true;
                    }
                    else if (isSubjugated && myRuler == subjugator) {
                        isAllied = true;
                    }
                }
                else if (isSubjugated && myRuler == realmIndex) {
                    isAllied = true;
                }

                if (isAllied) {
                    if (!realmPool.Contains(i)) {
                        realmPool.Add(i);
                    }
                }
            }
        }

        public bool GetCapitalOfRealm(byte realmIndex, out int regionIndex)
        {
            for (regionIndex = 0; regionIndex < regions.Length; regionIndex++) {
                if (regions[regionIndex].RelevantBuilding.HasFlagSafe(EBuilding.Capital) && regions[regionIndex].IsOwnedBy(realmIndex)) {
                    return true;
                }
            }

            return false;
        }

        public void GetAllConnectedRegions(int startingPoint, in ICollection<int> regionsIndices, Predicate<Region> filter)
        {
            if (!IsValidRegionIndex(startingPoint)) {
                return;
            }

            regionsIndices.Add(startingPoint);
            int[] neighbors = GetNeighboringRegions(startingPoint);
            byte owner = regions[startingPoint].ownerIndex;

            bool hasOwner = regions[startingPoint].isOwned;

            for (int i = 0; i < neighbors.Length; i++) {
                if (regionsIndices.Contains(neighbors[i])) {
                    continue;
                }

                if (!filter(regions[neighbors[i]])) {
                    continue;
                }

                GetAllConnectedRegions(neighbors[i], in regionsIndices, filter);
            }
        }

        public void GetAllConnectedRegionsPreventingStarvation(int startingPoint, in ICollection<int> regionIndices)
        {
            if (rules.alliedRegionsPreventStarvation) {
                GetAllConnectedRegionsOfSameAlliance(startingPoint, regionIndices);
            }
            else {
                GetAllConnectedRegionsOfSameOwner(startingPoint, regionIndices);
            }
        }

        public void GetAllConnectedRegionsOfSameAlliance(int startingPoint, in ICollection<int> regionsIndices)
        {
            if (!IsValidRegionIndex(startingPoint)) {
                return;
            }

            bool hasOwner = regions[startingPoint].isOwned;
            byte owner = regions[startingPoint].ownerIndex;

            World lambdaCopy = this; // :(

            bool isAlliedWith(byte a, byte b)
            {
                return lambdaCopy.IsRealmAlliedWith(a, b);
            }

            GetAllConnectedRegions(startingPoint, regionsIndices, (r) =>
                (!r.isOwned && !hasOwner) ||
                (hasOwner && r.GetOwner(out byte theirOwner) && isAlliedWith(owner, theirOwner))
            );
        }

        public void GetAllConnectedRegionsOfSameOwner(int startingPoint, in ICollection<int> regionsIndices)
        {
            if (!IsValidRegionIndex(startingPoint)) {
                return;
            }

            bool hasOwner = regions[startingPoint].isOwned;
            byte owner = regions[startingPoint].ownerIndex;

            GetAllConnectedRegions(startingPoint, regionsIndices, (r) =>
                (!r.isOwned && !hasOwner) ||
                (hasOwner && r.IsOwnedBy(owner))
            );
        }

        public Position Position(int index)
        {
            return new Position(index % SideLength, index / SideLength);
        }

        public bool GetOppositeNeighbor(int centralRegionIndex, int neighborRegionIndex, out int oppositeRegionIndex)
        {
            Position centralPosition = Position(centralRegionIndex);
            Position neighborPosition = Position(neighborRegionIndex);
            Position opposite = GetOppositePosition(centralPosition, neighborPosition);

            oppositeRegionIndex = Index(opposite);

            return IsValidRegionIndex(oppositeRegionIndex);
        }

        private Position GetOppositePosition(in Position centralPoint, in Position positionToMirror)
        {
            AxialPosition axialCentral = new AxialPosition(centralPoint);
            AxialPosition axialNeighbor = new AxialPosition(positionToMirror);


            AxialPosition axialDir = new AxialPosition() {
                q = axialNeighbor.q - axialCentral.q,
                r = axialNeighbor.r - axialCentral.r
            };

            AxialPosition axialOpposite = new AxialPosition() {
                q = axialCentral.q - axialDir.q,
                r = axialCentral.r - axialDir.r
            };

            Position outputPosition = axialOpposite.ToPosition();

            return outputPosition;
        }

        public void GetNeighboringRegions(int index, byte depth, in List<int> neighbors)
        {
            if (depth == 0) {
                neighbors.Add(index);
                return;
            }

            int startIndex = neighbors.Count;
            GetNeighboringRegions(index, neighbors);
            int count = neighbors.Count - startIndex;

            depth--;
            if (depth > 0) {
                for (int i = startIndex; i < count; i++) {
                    GetNeighboringRegions(neighbors[i], depth, in neighbors);
                }
            }

            count = neighbors.Count - startIndex;

            // Remove duplicates
            for (int i = startIndex; i < count; i++) {
                int value = neighbors[i];
                for (int j = i + 1; j < count; j++) {
                    if (neighbors[j] == value) {
                        neighbors.RemoveAt(j);
                        j--;
                        count--;
                    }
                }
            }
        }

        public void GetNeighboringRegions(int index, in List<int> neighbors)
        {
            Position position = Position(index);

            int offset = 1 - position.y % 2;

            if (position.x > 0) {
                neighbors.Add(index - 1);
            }

            if (position.y > 0) {
                if (position.x >= offset) {
                    neighbors.Add(index - SideLength - offset);
                }

                if (position.x < SideLength - 1 + offset) {
                    neighbors.Add(index - SideLength + 1 - offset);
                }
            }

            if (position.x < SideLength - 1) {
                neighbors.Add(index + 1);
            }

            if (position.y < SideLength - 1) {

                if (position.x < SideLength - 1 + offset) {
                    neighbors.Add(index + SideLength + 1 - offset);
                }

                if (position.x >= offset) {
                    neighbors.Add(index + SideLength - offset);
                }
            }

            int maxIndex = SideLength * SideLength - 1;
            neighbors.RemoveAll(o => o < 0 || o > maxIndex);
            for (int i = 0; i < neighbors.Count; i++) {
                if (regions[neighbors[i]].inert) {
                    neighbors.RemoveAt(i);
                    i--;
                }
            }
        }

        private void EatColumnsRows()
        {
            int eatenColumns = Math.Max(0, rules.eatFirstLastColumns - 3 + squareSideLength);

            for (int x = 0; x < SideLength; x++) {
                if (x < eatenColumns || x >= SideLength - eatenColumns) {
                    for (int y = 0; y < SideLength; y++) {
                        Position position = new Position(x, y);
                        int index = Index(position);
                        regions[index].inert = true;
                    }
                }
            }
        }

        private void EatCorners()
        {
            int eatenCorners = Math.Max(0, rules.eatenCorners - 2 + squareSideLength);

            for (int x = 0; x < SideLength; x++) {
                for (int y = 0; y < SideLength; y++) {

                    int invX = SideLength - x - 1;
                    int invY = SideLength - y - 1;

                    void eat(ref World world, int rX, int rY, int eatenCorners)
                    {
                        if (rX + rY <= eatenCorners) {
                            Position position = new Position(x, y);
                            int index = world.Index(position);

                            world.regions[index].inert = true;
                        }
                    }

                    eat(ref this, x, y, System.Math.Max(0, eatenCorners - 1));
                    eat(ref this, invX, y, eatenCorners);
                    eat(ref this, invX, invY, eatenCorners);
                    eat(ref this, x, invY, System.Math.Max(0, eatenCorners - 1));
                }
            }
        }

        private Position[] GetPossibleRotationaryPositions(int positionCount)
        {
            List<Position> positions = new List<Position>();

            int anglePerStep = 360 / positionCount;

            int distanceFromCenter = (SquareSideLength / 3) * OPTIMAL_REALM_SIZE
                + OPTIMAL_REALM_SIZE + 1 + rules.initialSafetyMarginBetweenRealms;

            Position centerPosition = new Position(sideLength / 2, sideLength / 2);

            for (int i = 0; i < positionCount; i++) {

                int angle = i * anglePerStep;

                int sinePercentage = TrigonometryHelper.GetSineM100100(angle);
                int cosinePercentage = TrigonometryHelper.GetCosineM100100(angle);

                Position offset = new Position(sinePercentage * distanceFromCenter + 50, cosinePercentage * distanceFromCenter);

                Position position = (centerPosition * 100 + offset) / 100;

                positions.Add(position);
            }

            return SortPositionsByFurtherFromEachOther(positions);
        }

        private Position[] GetPossibleGridAlignedPositions()
        {
            List<Position> positions = new List<Position>();

            int margin = OPTIMAL_REALM_SIZE + rules.initialSafetyMarginBetweenRealms;
            int realmSquareSize = OPTIMAL_REALM_SIZE * 2 + 1;

            int realmsPerRow = squareSideLength;

            // Realms can't be glued to each other and have to be not touching the outer or inner ring
            for (int x = 0; x < realmsPerRow; x++) {
                for (int y = 0; y < realmsPerRow; y++) {

                    int posX = margin;
                    int posY = margin;

                    int spacePerRealm = realmSquareSize + rules.initialSafetyMarginBetweenRealms;

                    posX += x * spacePerRealm;

                    posY += y * spacePerRealm;

                    positions.Add(new Lib.Position(posX, posY));
                }
            }

            return SortPositionsByFurtherFromEachOther(positions);
        }

        private Position[] SortPositionsByFurtherFromEachOther(List<Position> positions)
        {
            // Group distances that are the furthest from each other
            if (positions.Count == 1) {
                return new Position[] { positions[0] };
            }

            if (positions.Count == 0) {
                return new Position[0];
            }

            List<Position> positionsLeftToSort = new List<Position>(positions);
            List<Position> newPositions = new List<Position>(positions.Count);

            while (positionsLeftToSort.Count > 0) {
                Position gravityCenter = new Position();

                {
                    List<Position> positionsToAvoid = newPositions.Count == 0 ? positionsLeftToSort : newPositions;

                    for (int existingPositionIndex = 0; existingPositionIndex < positionsToAvoid.Count; existingPositionIndex++) {
                        gravityCenter += positionsToAvoid[existingPositionIndex];
                    }

                    gravityCenter /= positionsToAvoid.Count;
                }

                Position pos = positionsLeftToSort.OrderByDescending((o) => o.DistanceWith(gravityCenter)).First();

                bool removed = positionsLeftToSort.Remove(pos);
                newPositions.Add(pos);

                System.Diagnostics.Debug.Assert(removed);
            }

            return newPositions.ToArray();
        }

        private void InitializeCouncilRealm(byte realmIndex, in Position startingPosition)
        {
            InitializeRealm(
                realmIndex,
                startingPosition,
                (byte)Math.Max(0, SquareSideLength - 3 + rules.board.council.councilRealmRegionSize)
            );

            for (int i = 0; i < regions.Length; i++) {
                if (IsCouncilRegion(i) && regions[i].RelevantBuilding == EBuilding.None) {
                    regions[i].AddBuilding(EBuilding.Church);
                }
            }
        }

        private void InitializeRealm(byte realmIndex, in Position startingPosition, byte size = 1)
        {
            if (!IsValidRealmIndex(realmIndex)) {
                return;
            }

            ref Region region = ref regions[Index(startingPosition)];

            region.AddBuilding(EBuilding.Capital);
            region.ownerIndex = realmIndex;
            region.isOwned = true;

            // Expand until top size reached
            int remainingExpansion = size;
            List<int> cache = new List<int>();
            while (remainingExpansion > 0) {
                remainingExpansion--;
                cache.Clear();
                GetTerritoryOfRealm(realmIndex, cache);
                for (int i = 0; i < cache.Count; i++) {
                    int[] neighbors = GetNeighboringRegions(cache[i]);
                    for (int neighborIndex = 0; neighborIndex < neighbors.Length; neighborIndex++) {
                        ref Region ownedRegion = ref regions[neighbors[neighborIndex]];
                        if (!ownedRegion.IsOwnedBy(realmIndex)) {
                            if (!ownedRegion.isOwned || i % 2 == 0) { // "Flip flop" if the region overlaps with another starting area
                                ownedRegion.ownerIndex = realmIndex;
                                ownedRegion.isOwned = true;
                            }
                        }
                    }
                }
            }

            ref Realm realm = ref realms[realmIndex];
            AddSilverTreasury(realmIndex, this.rules.startingGold * 10);

            realm.availableDecisions = this.rules.startingDecisionCount;
        }

        private int[] GetNeighboringRegions(int index)
        {
            List<int> neighbors = new List<int>(6);

            GetNeighboringRegions(index, neighbors);

            return neighbors.ToArray();
        }

        private int Index(in Position position)
        {
            return position.x + position.y * SideLength;
        }

        private bool IsValidPosition(in Position position)
        {
            return position.x >= 0 && position.y >= 0 && position.x < SideLength && position.y < SideLength;
        }

        public bool IsValidRealmIndex(byte index)
        {
            return index >= 0 && index < realms.Length;
        }

        public bool IsValidRegionIndex(int index)
        {
            return index >= 0 && index < regions.Length;
        }

        public void Write(BinaryWriter into)
        {
            into.Write(sideLength);
            into.Write(squareSideLength);

            into.Write(councilRealmIndex.HasValue);
            into.Write(councilRealmIndex ?? (byte)0);
            into.Write(regions);
            into.Write(realms);
            into.Write(rules);
        }

        public void Read(byte version, BinaryReader from)
        {
            sideLength = from.ReadByte();
            squareSideLength = from.ReadByte();

            councilRealmIndex = null;
            bool hasCouncilRealmIndex = from.ReadBoolean();
            byte realmIndex = from.ReadByte();
            if (hasCouncilRealmIndex) {
                councilRealmIndex = realmIndex;
            }

            from.Read(default, ref regions);
            from.Read(default, ref realms);

            if (version == 6){
                Lib.Position[] startingPositions = default;
                from.Read(default, ref startingPositions);
            }

            rules.Read(from);
        }

        public int GetHash()
        {
            return Extensions.Hash(
                Extensions.Hash(
                    councilRealmIndex ?? 0
                ),
                Extensions.Hash(
                    Extensions.Hash(regions),
                    Extensions.Hash(realms),
                    Extensions.Hash(rules)
                )
            );
        }
    }
}