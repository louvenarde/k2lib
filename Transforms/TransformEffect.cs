
namespace LouveSystems.K2.Lib
{
    using System;

    public interface ITransformEffect
    {
        public struct ConquestEffect : ITransformEffect
        {
            //public bool IsFactionFeat => factionHighlights != EFactionFlag.None;

            public bool Success => newOwningRealm != previousOwningRealm || 
                (newOwningRealm == previousOwningRealm && factionHighlights.HasFlagSafe(EFactionFlag.SelfAttack));

            public byte attackingRealm;
            public int regionIndex;
            public int[] attackingRegionsIndices;
            public byte previousOwningRealm;
            public byte newOwningRealm;
            public EFactionFlag factionHighlights;
            public bool isACoinFlip;
            public byte[] otherMajorityAttackersWhoLostCoinFlip;
            public int silverLooted;
            public bool hadBuilding;

            public override string ToString()
            {
                return $"{GetType()} by realm {attackingRealm} on region {regionIndex} from regions [{string.Join(", ", attackingRegionsIndices)}], changing ownership from {previousOwningRealm} to {newOwningRealm} - coin flip? {isACoinFlip}, to whom? [{string.Join(", ", otherMajorityAttackersWhoLostCoinFlip)}]. Region had building? {hadBuilding}. Looted anything? {silverLooted} silver.";
            }

            public void Apply(in GameState previous, ref GameState next)
            {
                if (Success) {
                    if (previous.world.Regions[regionIndex].Building.HasFlagSafe(EBuilding.Capital)) {
                        // Do not modify
                        return;
                    }

                    next.world.ConquestRegion(regionIndex, newOwningRealm);
                }
            }

            public byte GetNewOwnerFactionIndex(in GameState state)
            {
                return state.world.Realms[newOwningRealm].factionIndex;
            }

            public bool IsFactionFeatForAttacker(in GameState state)
            {
                return IsFactionFeatFor(state.world.GetRealmFaction(attackingRealm));
            }

            public bool IsFactionFeatForNewOwner(in GameState state)
            {
                return 
                    newOwningRealm != byte.MaxValue &&
                    IsFactionFeatFor(state.world.GetRealmFaction(newOwningRealm));
            }

            public bool IsFactionFeatFor(EFactionFlag flag)
            {
                if (this.factionHighlights == EFactionFlag.None) {
                    return false;
                }

                return flag.HasFlagSafe(this.factionHighlights);
            }
        }

        public struct StarvationEffect : ITransformEffect
        {
            public int regionIndex;
            public byte newOwningRealm;
            public bool hasNewOwner;
            public byte waveIndex;
            public bool wasCoinFlip;

            public void Apply(in GameState previous, ref GameState next)
            {
                next.world.StarveRegion(regionIndex, hasNewOwner, newOwningRealm);
            }

            public override string ToString()
            {
                if (hasNewOwner)
                    return $"{GetType()} on region {regionIndex} with new owner: {newOwningRealm} [wave {waveIndex} - coinFlip? {wasCoinFlip}]";
                else
                    return $"{GetType()} on region {regionIndex} no new owner [wave {waveIndex} - coinFlip? {wasCoinFlip}]";
            }
        }

        public struct ConstructionEffect : ITransformEffect
        {
            public bool IsFactionFeat => isFactionHighlight && isSuccessful;

            public int regionIndex;
            public EBuilding building;
            public byte forOwner;
            public int silverPricePaid;
            public bool isFactionHighlight;
            public bool isSuccessful;

            public void Apply(in GameState previous, ref GameState next)
            {
                // ownership check
                if (!isSuccessful) {
                    return;
                }

                byte regionOwner = next.world.Regions[regionIndex].ownerIndex;
                {
                    if (next.world.Realms[regionOwner].IsSubjugated(out byte subjugator)) {
                        regionOwner = subjugator;
                    }
                }

                next.world.ConstructBuilding(regionIndex, building, silverPricePaid);
                next.world.AddSilverTreasury(regionOwner, -silverPricePaid);
            }

            public byte GetOwnerFactionIndex(in GameState state)
            {
                return state.world.Realms[forOwner].factionIndex;
            }
        }

        public struct FavourPaymentEffect : ITransformEffect
        {
            public byte realmIndex;
            public int silverPricePaid;

            public void Apply(in GameState previous, ref GameState next)
            {
                next.world.SetRealmFavoured(realmIndex, true);
                next.world.AddSilverTreasury(realmIndex, -silverPricePaid);
            }
        }

        public struct AdministrationUpgradeEffect : ITransformEffect
        {
            public byte realmIndex;
            public int silverPricePaid;

            public void Apply(in GameState previous, ref GameState next)
            {
                next.world.IncreaseMaxDecisions(realmIndex);
                next.world.AddSilverTreasury(realmIndex, -silverPricePaid);
            }
        }

        public struct SubjugationEffect : ITransformEffect
        {
            public byte attackingRealmIndex;
            public byte targetRealmIndex;
            public bool isFactionHighlight;
            public byte attacksBefore;
            public byte attacksAfter;
            public bool subjugationCompleted;

            public void Apply(in GameState previous, ref GameState next)
            {
                if (attackingRealmIndex == targetRealmIndex) {
                    return; /// Should never happen
                }

                attacksBefore = previous.world.Realms[targetRealmIndex].GetSubjugatingAttacksReceived(attackingRealmIndex);
                subjugationCompleted = next.world.AttemptSubjugation(attackingRealmIndex, targetRealmIndex);
                attacksAfter = next.world.Realms[targetRealmIndex].GetSubjugatingAttacksReceived(attackingRealmIndex);
            }

            public override string ToString()
            {
                return $"{GetType()} by realm {attackingRealmIndex} (highlight {isFactionHighlight}) on {targetRealmIndex} (attackBefore: {attacksBefore} attackAfter: {attacksAfter}). SubjugationCompleted? {subjugationCompleted}";
            }
        }

        public struct PartialSubjugationEvolutionEffect : ITransformEffect
        {
            public bool Stalled => attacksBefore == attacksAfter;

            public byte attackingRealmIndex;
            public byte targetRealmIndex;

            public bool LinkedToAttacker => linkedAttackerRegions?.Length > 0;

            public int[] linkedAttackerRegions;

            public byte attacksBefore;
            public byte attacksAfter;

            public void Apply(in GameState previous, ref GameState next)
            {
                attacksBefore = previous.world.Realms[targetRealmIndex].GetSubjugatingAttacksReceived(attackingRealmIndex);

                if (LinkedToAttacker) {
                    // Do nothing
                }
                else {
                    next.world.CancelPartialSubjugation(attackingRealmIndex, targetRealmIndex);
                }

                attacksAfter = next.world.Realms[targetRealmIndex].GetSubjugatingAttacksReceived(attackingRealmIndex);
            }
        }

        public bool IsFactionFeat => false;

        public void Apply(in GameState previous, ref GameState next);
    }
}