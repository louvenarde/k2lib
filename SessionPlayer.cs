
namespace LouveSystems.K2.Lib
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    public class SessionPlayer
    {
        public byte RealmIndex;

        public byte ControllingRealmIndex => gameSession.CurrentGameState.world.Realms[RealmIndex].IsSubjugated(out byte subjugator) ? subjugator : RealmIndex;

        public GameRules Rules => gameSession.Rules;

        public byte FactionIndex => gameSession.CurrentGameState.world.Realms[RealmIndex].factionIndex;

        public EFactionFlag Faction => Rules.factions.factionFlags[FactionIndex].flag;

        public EFactionFlag AllianceFaction => gameSession.CurrentGameState.world.GetAllianceFaction(RealmIndex);

        private readonly GameSession gameSession;

        public SessionPlayer(GameSession session)
        {
            this.gameSession = session;
        }

        public bool CanSeePlannedAttacksOf(byte otherRealmIndex)
        {
            return gameSession.CurrentGameState.world.CanSeePlannedAttacksOf(RealmIndex, otherRealmIndex);
        }

        public bool CanSeePlannedAttacksOf(SessionPlayer otherSessionPlayer)
        {
            return CanSeePlannedAttacksOf(otherSessionPlayer.RealmIndex);
        }

        public bool CanSeePlannedConstructionsOf(SessionPlayer otherSessionPlayer)
        {
            return gameSession.CurrentGameState.world.CanSeePlannedConstructionsOf(RealmIndex, otherSessionPlayer.RealmIndex);
        }

        public bool CanSeePlannedConstructionsOf(byte otherRealmIndex)
        {
            return gameSession.CurrentGameState.world.CanSeePlannedConstructionsOf(RealmIndex, otherRealmIndex);
        }

        public bool GetPlannedConstructions(List<EBuilding> plannedBuildings)
        {
            int builds = 0;

            gameSession.AwaitingTransforms.ForEach(o =>
            {
                if (o is RegionBuildTransform build &&
                    build.owningRealm == RealmIndex) {
                    builds++;
                    plannedBuildings.Add(build.building);
                }
            });

            return builds > 0;
        }

        public ERegionAttackType GetAllowedAttackTypes()
        {
            ERegionAttackType type = ERegionAttackType.Standard;

            if (CanSlitherAttack()) {
                type |= ERegionAttackType.Slithering;
            }

            if (CanExtendAttack()) {
                type |= ERegionAttackType.Charge;
            }

            return type;
        }

        public bool CanSlitherAttack()
        {
            if (Faction.HasFlagSafe(EFactionFlag.SlitherAttacksBetweenRegions)) {
                return true;
            }

            return false;
        }

        public bool CanExtendAttack()
        {
            if (Faction.HasFlagSafe(EFactionFlag.Charge)) {
                for (int i = 0; i < gameSession.AwaitingTransforms.Count; i++) {
                    if (gameSession.AwaitingTransforms[i].owningRealm == RealmIndex &&
                        gameSession.AwaitingTransforms[i] is RegionAttackRegionTransform attack &&
                        attack.attackType == ERegionAttackType.Charge
                        ) {
                        return false;
                    }
                }

                return true;
            }

            return false;
        }


        public void PlanAttack(int fromRegionIndex, AttackTarget target)
        {
            PlanAttack(fromRegionIndex, target.regionIndex, target.attackType);
        }

        public void PlanAttack(int fromRegionIndex, int toRegionIndex, ERegionAttackType type)
        {
            if (gameSession.CurrentGameState.world.Regions[toRegionIndex].inert) {
                Logger.Warn($"Discarding attack plan {fromRegionIndex}=>{toRegionIndex}: target is inert");
                return;
            }

            if (gameSession.CurrentGameState.world.Regions[fromRegionIndex].inert) {
                Logger.Warn($"Discarding attack plan {fromRegionIndex}=>{toRegionIndex}: origin is inert");
                return;
            }

            List<int> neighbors = new List<int>(6);
            gameSession.CurrentGameState.world.GetNeighboringRegions(fromRegionIndex, neighbors);

            RegionAttackRegionTransform transform = new RegionAttackRegionTransform(
                fromRegionIndex,
                toRegionIndex,
                attackType: type,
                RealmIndex
            );

            if (AnyDecisionsRemaining() || transform.IsFree) {
                Act(transform);
            }
            else {
                Logger.Warn($"Discarding attack plan {fromRegionIndex}=>{toRegionIndex}: no decisions remaining for realm {RealmIndex}!");
                return;
            }
        }

        public bool GetPlannedAttacks(List<RegionAttackRegionTransform> plannedAttacks)
        {
            int attacks = 0;

            gameSession.AwaitingTransforms.ForEach(o =>
            {
                if (o is RegionAttackRegionTransform atk &&
                    atk.owningRealm == RealmIndex) {
                    attacks++;
                    plannedAttacks.Add(atk);
                }
            });

            return attacks > 0;
        }

        public bool HasAnyAttackPlanned(out int count)
        {
            int attacks = 0;

            gameSession.AwaitingTransforms.ForEach(o =>
            {
                if (o is RegionAttackRegionTransform atk &&
                    atk.owningRealm == RealmIndex) {
                    attacks++;
                }
            });

            count = attacks;
            return attacks > 0;
        }

        public bool IsUnderAttack(int regionIndex, out int attackCount, out ERegionAttackType attackTypes)
        {
            var transforms = gameSession.AwaitingTransforms.FindAll(
                o =>
                    o is RegionAttackRegionTransform attack &&
                    CanControlRealm(attack.owningRealm) &&
                    attack.targetRegionIndex == regionIndex
            );

            attackCount = transforms.Count;

            attackTypes =  ERegionAttackType.Standard;

            for (int i = 0; i < transforms.Count; i++) {
                if (transforms[i] is RegionAttackRegionTransform atk) {
                    attackTypes |= atk.attackType;
                }
            }

            return attackCount > 0;
        }

        public bool IsBuildingAnything(out int count)
        {
            int total = 0;
            gameSession.AwaitingTransforms.ForEach(o =>
            {
                if (o is RegionBuildTransform build && CanControlRealm(build.owningRealm)) {
                    total++;
                }
            });

            count = total;
            return count > 0;
        }

        public bool IsBuildingSomething(int regionIndex, out EBuilding building)
        {
            if (gameSession.AwaitingTransforms.Find(o =>
                o is RegionBuildTransform build &&
                build.actingRegionIndex == regionIndex &&
                CanControlRealm(build.owningRealm)) is RegionBuildTransform buildTransform
            ) {
                building = buildTransform.building;
                return true;
            }

            building = default;
            return false;
        }

        public int GetMaximumDecisions()
        {
            return gameSession.CurrentGameState.world.Realms[RealmIndex].availableDecisions + (AdminUpgradeIsPlanned() ? 1 : 0);
        }


        public int GetRemainingDecisions()
        {
            int maxDecisions = GetMaximumDecisions();
            int decisionsTaken = GetSumOfDecisionCosts();

            return maxDecisions - decisionsTaken;
        }

        public int GetTreasury()
        {
            int treasuryAtStartOfTurn = gameSession.CurrentGameState.world.GetSilverTreasury(RealmIndex);
            int silverSpent = gameSession.AwaitingTransforms.Sum(o => CanControlRealm(o.owningRealm) ? o.SilverCost : 0);

            return treasuryAtStartOfTurn - silverSpent;
        }

        public virtual bool CanPlay()
        {
            return true;
        }

        public virtual bool HasAnythingToPlay()
        {
            return AnyDecisionsRemaining() || CanUpgradeAdministration();
        }

        public bool AnyDecisionsRemaining()
        {
            return GetRemainingDecisions() > 0;
        }

        public bool CanAfford(int silverPrice)
        {
            return GetTreasury() >= silverPrice;
        }

        public bool CanPlayWithRegion(int regionIndex)
        {
            return
                !gameSession.CurrentGameState.IsRegionReserved(regionIndex) && 
                gameSession.CurrentGameState.world.Regions[regionIndex].GetOwner(out byte regionOwner) &&
                CanControlRealm(regionOwner) &&
                (
                    !gameSession.HasRegionPlayed(regionIndex) ||
                    gameSession.CurrentGameState.world.Regions[regionIndex].CanReplay(gameSession.Rules)
                )
                &&
                CanPlay() &&
                AnyDecisionsRemaining();
        }

        public bool CanUpgradeAdministration()
        {
            // Allow admin upgrade even if no decisions remain
            //if (!AnyDecisionsRemaining()) {
            //    return false;
            //}

            if (!CanAfford(GetAdministrationUpgradeSilverCost())) {
                return false;
            }

            if (AdminUpgradeIsPlanned()) {
                return false; // Already upgrading
            }

            if (!CanPlay()) {
                return false;
            }

            if (gameSession.CurrentGameState.world.Realms[RealmIndex].availableDecisions >= gameSession.Rules.maxDecisionCount) {
                return false;
            }

            return true;
        }

        public bool IsFavoured()
        {
            return gameSession.IsFavoured(RealmIndex);
        }

        public bool IsNextBuildingFree()
        {
            if (Faction.HasFlagSafe(EFactionFlag.FirstBuildingIsFree)) {
                if (!IsBuildingAnything(out int _)) {
                    return true;
                }
            }

            return false;
        }

        public void PlanConstruction(int regionIndex, EBuilding building)
        {
            if (AnyDecisionsRemaining()) {

                if (gameSession.CurrentGameState.world.Regions[regionIndex].RelevantBuilding != EBuilding.None) {
                    Logger.Warn($"Discarding construction plan {building} on {regionIndex}: target already hosts {gameSession.CurrentGameState.world.Regions[regionIndex].Building}");
                    return;
                }

                bool buildingIsFree = IsNextBuildingFree();

                // We add a build transform
                RegionBuildTransform transform = new RegionBuildTransform(
                    building,
                    buildingIsFree ? 0 : gameSession.Rules.GetBuilding(building).silverCost,
                    RealmIndex,
                    regionIndex,
                    RealmIndex
                );

                Act(transform);
            }
            else {
                Logger.Warn($"Discarding construction plan {building}=>{regionIndex}: no decisions remaining for realm {RealmIndex}!");
                return;
            }
        }

        public bool CanPayForFavours()
        {
            if (!AnyDecisionsRemaining()) {
                return false;
            }

            if (!CanAfford(gameSession.Rules.favourGoldPrice * 10)) {
                return false;
            }

            if (IsFavoured()) {
                return false;
            }

            if (FavoursArePlanned()) {
                return false;
            }

            if (!CanPlay()) {
                return false;
            }

            return true;
        }

        public bool FavoursArePlanned()
        {
            return gameSession.AwaitingTransforms.FindIndex(o =>
            o is PayFavoursTransform favour && favour.realmToFavour == ControllingRealmIndex) >= 0;
        }

        public bool AdminUpgradeIsPlanned()
        {
            // .FindIndex with cast creates an allocation apparently??
            // So we dance instead
            var transforms = gameSession.AwaitingTransforms;
            for (int i = 0; i < transforms.Count; i++) {
                if (transforms[i].Kind == Transform.ETransformKind.ImproveAdministration) {
                    AdminUpgradeTransform adminUpgrade = transforms[i] as AdminUpgradeTransform;
                    if (adminUpgrade.realmToUpgrade == RealmIndex) {
                        return true;
                    }
                }
            }

            return false;
        }

        public void WasteAction()
        {
            if (AnyDecisionsRemaining()) {
                Act(new DoNothingTransform(RealmIndex));
            }
            else {
                Logger.Warn($"Cannot {nameof(WasteAction)} for {RealmIndex} - no action remaining!");
            }
        }

        public void PayForFavours()
        {
            if (CanPayForFavours()) {
                Act(
                    new PayFavoursTransform(
                        ControllingRealmIndex,
                        gameSession.Rules.favourGoldPrice * 10,
                        RealmIndex
                    )
                );
            }
            else {
                Logger.Warn($"Discarding {nameof(PayForFavours)} plan by {RealmIndex}!");
            }
        }

        public void UpgradeAdministration()
        {
            if (CanUpgradeAdministration()) {
                Act(
                    new AdminUpgradeTransform(
                        RealmIndex,
                        GetAdministrationUpgradeSilverCost(),
                        RealmIndex
                    )
                );
            }
            else {
                Logger.Warn($"Discarding {nameof(UpgradeAdministration)} plan by {RealmIndex}!");
            }
        }

        public int GetAdministrationUpgradeSilverCost()
        {
            int startDecisions = gameSession.Rules.startingDecisionCount;
            int upgradeCount = gameSession.CurrentGameState.world.Realms[RealmIndex].availableDecisions - startDecisions;

            return (gameSession.Rules.enhanceAdminGoldPrice + upgradeCount * gameSession.Rules.enhanceAdminGoldPriceIncreasePerUpgrade) * 10;
        }

        public bool CanBuildOn(int regionIndex)
        {
            if (!CanPlayWithRegion(regionIndex)) {
                return false;
            }


            if (gameSession.CurrentGameState.world.Regions[regionIndex].RelevantBuilding != EBuilding.None) {
                return false;
            }

            return true;
        }

        public bool CanBuild(int regionIndex, EBuilding building)
        {
            if (!CanBuildOn(regionIndex)) {
                return false;
            }

            GameRules.BuildingSettings settings = gameSession.Rules.GetBuilding(building);
            if (!settings.canBeBuilt) {
                return false;
            }

            bool buildingIsFree = IsNextBuildingFree();

            if (!buildingIsFree && !CanAfford(settings.silverCost)) {
                return false;
            }

            return true;
        }

        public bool CanControlRealm(int otherRealmIndex)
        {
            if (otherRealmIndex == RealmIndex) {
                return true;
            }

            int localOwner = RealmIndex;

            // We have the same subjugator
            if (gameSession.CurrentGameState.world.Realms[otherRealmIndex].IsSubjugated(out byte theirOwner)) {
                if (theirOwner == RealmIndex) {
                    return true; // They are a subject of local owner
                }
                else {
                    otherRealmIndex = theirOwner;
                }
            }


            if (gameSession.CurrentGameState.world.Realms[localOwner].IsSubjugated(out byte myOwner)) {

                if (myOwner == otherRealmIndex) {
                    return true; // They subjugated me
                }
            }

            return false;
        }
        private int GetSumOfDecisionCosts()
        {
            var awaitingTransforms = gameSession.AwaitingTransforms;
            int result = 0;
            for (int i = 0; i < awaitingTransforms.Count; i++) {
                if (awaitingTransforms[i].owningRealm == RealmIndex) {
                    result += awaitingTransforms[i].DecisionCost;
                }
            }

            return result;
        }

        private void Act(Transform transform)
        {
            gameSession.AddTransform(transform);
        }
    }
}