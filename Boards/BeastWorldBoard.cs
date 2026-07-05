
namespace LouveSystems.K2.Lib
{
    using NUnit.Framework;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    public struct BeastWorldBoard : IBoard
    {
        const byte VERSION = 1;
        public struct SetEnragedBeastEffect : ITransformEffect
        {
            public byte beastIndex;
            public bool enraged;
            public bool fromShockwave;
            public int[] optionalAngerSources;

            public void Apply(in GameState previous, ref GameState next)
            {
                if (next.board is BeastWorldBoard beastWorld) {
                    ref Beast beast = ref beastWorld.beasts[beastIndex];

                    if (enraged) {
                        beast.enragedTurnsRemaining = previous.rules.board.beastWorld.rageDuration;
                        beast.hotPath.Clear();

                        next.world.NeutralizeRegionFromHazard(beast.regionIndex);
                    }
                    else {
                        beast.enragedTurnsRemaining--;
                        if (!beast.IsEnraged) {
                            beast.hotPath.Clear();
                        }
                    }


                    if (optionalAngerSources != null && !fromShockwave) {
                        for (int i = 0; i < optionalAngerSources.Length; i++) {
                            beast.hotPath.Enqueue(optionalAngerSources[i]);


                            if (next.world.Regions[optionalAngerSources[i]].GetOwner(out byte realmIndex)) {
                                next.world.NotifyBeastAttacked(realmIndex);
                            }
                        }
                    }
                }
            }
        }

        public struct MoveBeastEffect : ITransformEffect
        {
            public byte beastIndex;
            public int previousRegionIndex;
            public int newRegionIndex;

            public void Apply(in GameState previous, ref GameState next)
            {
                if (next.board is BeastWorldBoard beastWorld) {
                    ref Beast beast = ref beastWorld.beasts[beastIndex];
                    beast.hotPath.Enqueue(beast.regionIndex);
                    beast.regionIndex = newRegionIndex;

                    if (beast.hotPath.Count > previous.rules.board.beastWorld.hotPathLength) {
                        beast.hotPath.Dequeue();
                    }

                    if (beast.IsEnraged) {
                        next.world.NeutralizeRegionFromHazard(beast.regionIndex);
                    }
                }
            }
        }

        public struct Beast : IBinarySerializableWithVersion
        {
            public bool IsEnraged => enragedTurnsRemaining > 0;

            public int regionIndex;

            public byte enragedTurnsRemaining;

            public byte lastIdleMoveXTurnsAgo;

            public Queue<int> hotPath;

            public GameRules.GlobalBoardSettings.BeastWorldBoardSettings.NavigationalPreferences Navigation { private set; get; }

            public Beast Duplicate()
            {
                return new Beast() {
                    regionIndex = regionIndex,
                    enragedTurnsRemaining = enragedTurnsRemaining,
                    lastIdleMoveXTurnsAgo = lastIdleMoveXTurnsAgo,
                    hotPath = new Queue<int>(hotPath)
                };
            }

            public void RefreshNavigation(in GameState state)
            {
                Navigation =
                        IsEnraged ?
                         state.rules.board.beastWorld.navigationWhenEnraged :
                         state.rules.board.beastWorld.navigationWhenCalm;
            }

            public void Write(BinaryWriter into)
            {
                into.Write(regionIndex);
                into.Write(enragedTurnsRemaining);
                into.Write(lastIdleMoveXTurnsAgo);
                into.WriteIntegers(hotPath.ToArray());
            }

            public void Read(byte version, BinaryReader from)
            {
                regionIndex = from.ReadInt32();
                enragedTurnsRemaining = from.ReadByte();
                lastIdleMoveXTurnsAgo = from.ReadByte();
                hotPath = new Queue<int>(from.ReadIntegers());
            }
        }

        public IReadOnlyList<Beast> Beasts => beasts;

        private Beast[] beasts;

        public EBoardType Type => EBoardType.BeastWorld;

        public BeastWorldBoard(GameRules.GlobalBoardSettings.BeastWorldBoardSettings boardSettings, in World world) : this()
        {
            beasts = new Beast[System.Math.Min(boardSettings.beastCount, (byte)6)];

            if (beasts.Length == 1) {
                beasts[0].regionIndex = world.Regions.Count / 2;
            }
            else {
                var centralNeighbors = new List<int>();
                world.GetNeighboringRegions(world.Regions.Count / 2, centralNeighbors);
                for (byte i = 0; i < beasts.Length; i++) {

                    int neighborIndex = (centralNeighbors.Count / beasts.Length) * i;

                    // The min() here is useless but it helps me sleep
                    beasts[i].regionIndex = centralNeighbors[System.Math.Min(neighborIndex, centralNeighbors.Count-1)];
                }
            }

            for (int i = 0; i < beasts.Length; i++) {
                beasts[i].hotPath = new Queue<int>(boardSettings.hotPathLength);
            }
        }


        public void GetConquestEffects(in GameState state, in ITransformEffect.ConquestEffect conquest, in List<ITransformEffect> effects)
        {
            if (conquest.Success) {

                if (state.board is BeastWorldBoard beastWorld) {
                    bool enragedOne = false;
                    for (byte i = 0; i < beastWorld.beasts.Length; i++) {

                        if (beastWorld.beasts[i].regionIndex == conquest.regionIndex) {
                            if (conquest.attackingRegionsIndices != null) {
                                effects.Add(new SetEnragedBeastEffect() {
                                    optionalAngerSources = conquest.attackingRegionsIndices,
                                    beastIndex = i,
                                    enraged = true
                                });
                            }

                            enragedOne |= state.rules.board.beastWorld.shockwaveAngerBeasts;
                        }
                    }

                    // Add them afterwards
                    if (enragedOne) {
                        for (byte i = 0; i < beastWorld.beasts.Length; i++) {
                            if (!beastWorld.beasts[i].IsEnraged && beastWorld.beasts[i].regionIndex != conquest.regionIndex) {
                                effects.Add(new SetEnragedBeastEffect() {
                                    optionalAngerSources = new int[] { conquest.regionIndex },
                                    beastIndex = i,
                                    enraged = true,
                                    fromShockwave = true
                                });
                            }
                        }
                    }
                }
            }
        }

        public void ComputeEffects(ManagedRandom random, in GameState state, in List<ITransformEffect> effects)
        {
            if (state.board is BeastWorldBoard beastWorld) {
                {
                    List<ITransformEffect> newEffects = new List<ITransformEffect>();

                    for (byte i = 0; i < beastWorld.beasts.Length; i++) {

                        ComputeBeastEffects(i, random, in state, newEffects);
                    }

                    effects.AddRange(newEffects);
                }

                HashSet<byte> beastWillEnrage = new(beastWorld.beasts.Length);

                // We group all "enraged" effects into one
                for (int i = effects.Count - 1; i >= 0; i--) {
                    if (effects[i] is SetEnragedBeastEffect enrage && enrage.enraged) {

                        if (beastWillEnrage.Add(enrage.beastIndex)) {
                            // OK, this is the last one
                        }
                        else {
                            // this is an extra "howl" event, we want to remove it because it's useless
                            effects.RemoveAt(i);
                        }

                    }
                }
            }

        }

        public void GetPredictedMoves(byte beastIndex, in GameState state, in List<int> predictedMoves)
        {
            byte movements = beasts[beastIndex].IsEnraged ?
                   state.rules.board.beastWorld.movementsWhenEnraged :
                   state.rules.board.beastWorld.movementsWhenCalm;

            int startIndex = predictedMoves.Count;
            state.world.GetNeighboringRegions(beasts[beastIndex].regionIndex, movements, predictedMoves);

            for (int i = predictedMoves.Count - 1; i >= startIndex; i--) {
                if (!CanCrossRegion(beasts[beastIndex], predictedMoves[i], state)) {
                    predictedMoves.RemoveAt(i);
                }
            }
        }

        private void ComputeBeastEffects(byte beastIndex, ManagedRandom random, in GameState state, in List<ITransformEffect> effects)
        {
            ComputeBeastMovements(beastIndex, random, state, effects);
            ComputeBeastRage(beastIndex, in state, effects);

        }

        private void ComputeBeastRage(byte beastIndex, in GameState state, in List<ITransformEffect> effects)
        {
            GameState prediction = state.Duplicate();
            state.ApplyEffects(effects, ref prediction);
            if (prediction.board is BeastWorldBoard beastWorld) {
                if (beastWorld.beasts[beastIndex].IsEnraged) {
                    effects.Add(new SetEnragedBeastEffect() {
                        beastIndex = beastIndex,
                        enraged = false
                    });
                }
            }
        }

        private void ComputeBeastMovements(byte beastIndex, ManagedRandom random, in GameState state, in List<ITransformEffect> effects)
        {
            GameState prediction = state.Duplicate();
            state.ApplyEffects(effects, ref prediction);

            Logger.Debug($"Beast {beastIndex} computing movements...");

            if (prediction.board is BeastWorldBoard beastWorld) {
                ref Beast beast = ref beastWorld.beasts[beastIndex];

                beast.RefreshNavigation(in prediction);

                bool isEnraged = beast.IsEnraged;
                int movements = isEnraged ?
                    prediction.rules.board.beastWorld.movementsWhenEnraged :
                    prediction.rules.board.beastWorld.movementsWhenCalm;


                for (int i = 0; i < movements; i++) {

                    Logger.Debug($"Beast {beastIndex} computing movement {i + 1} out of {movements}");
                    if (MoveBeast(beastIndex, random, prediction, out ITransformEffect effect)) {
                        // cool
                        Logger.Debug($"Beast {beastIndex} moved successfully to {(effect is MoveBeastEffect mb ? mb.newRegionIndex.ToString() : "??")}");
                    }
                    else if (!isEnraged) {

                        Logger.Debug($"Beast {beastIndex} could not move and will become enraged");
                        effect = new SetEnragedBeastEffect() {
                            beastIndex = beastIndex,
                            enraged = true
                        };

                        isEnraged = true;

                        // Reset movement to do angry movements once we're enraged
                        Logger.Debug($"Beast {beastIndex} must now perform {movements} movements");
                        movements = prediction.rules.board.beastWorld.movementsWhenEnraged;
                        i = 0;
                    }
                    else {
                        Logger.Debug($"Beast {beastIndex} could not move and is already enraged, no change");
                    }

                    if (effect != default) {
                        effects.Add(effect);
                        prediction.ApplyEffects(new ITransformEffect[] { effect }, ref prediction);
                    }
                }
            }
        }

        static readonly List<int> neighboringRegionsCache = new List<int>(6);
        private bool MoveBeast(byte beastIndex, ManagedRandom random, in GameState state, out ITransformEffect effect)
        {
            effect = default;
            if (state.board is BeastWorldBoard beastWorld) {

                var regions = state.world.Regions;
                ref Beast beast = ref beastWorld.beasts[beastIndex];
                bool prefersFields = beast.Navigation.preferFields;

                neighboringRegionsCache.Clear();

                state.world.GetNeighboringRegions(beast.regionIndex, in neighboringRegionsCache);

                // Avoid desyncs ?
                neighboringRegionsCache.Sort();

                // Remove forbidden regions
                for (int i = neighboringRegionsCache.Count - 1; i >= 0; i--) {
                    if (!CanCrossRegion(beast, neighboringRegionsCache[i], state)) {
                        neighboringRegionsCache.RemoveAt(i);
                    }
                }

                if (neighboringRegionsCache.Count > 0)
                // We need to copy everything for a sorting operation because c# is a professional language
                {
                    Beast bull = beast;
                    GameState s = state;
                    neighboringRegionsCache.Sort((a, b) => SortForBeast(bull, s, a, b));

                    Logger.Debug($"Beast {beastIndex} options for movement (pre filtering):\n - {string.Join("\n - ", neighboringRegionsCache.Select(o => $"{o}=>{regions[o]}"))}");

                    for (int i = 0; i < neighboringRegionsCache.Count - 1; i++) {
                        if (SortForBeast(in beast, in state, neighboringRegionsCache[i], neighboringRegionsCache[i + 1]) != 0) {
                            neighboringRegionsCache.RemoveRange(i + 1, neighboringRegionsCache.Count - (i + 1));
                            break;
                        }
                    }

                    Logger.Debug($"Beast {beastIndex} options for movement (post filtering):\n - {string.Join("\n - ", neighboringRegionsCache.Select(o => $"{o}=>{regions[o]}"))}");

                    int nextRegion = neighboringRegionsCache[0];

                    if (neighboringRegionsCache.Count > 1) {

                        Position centralPosition = s.world.Position(s.world.Regions.Count / 2);
                        // Sort by distance - always go towards center
                        neighboringRegionsCache.Sort((a,b) => s.world.Position(a).DistanceWith(centralPosition).CompareTo(s.world.Position(b).DistanceWith(centralPosition)));

                        for (int i = neighboringRegionsCache.Count - 1; i >= 0; i--) {
                            if (s.world.Position(neighboringRegionsCache[0]).DistanceWith(centralPosition) <
                                s.world.Position(neighboringRegionsCache[i]).DistanceWith(centralPosition)) {
                                neighboringRegionsCache.RemoveAt(i);
                            }
                        }

                        Logger.Debug($"Beast {beastIndex} options for movement (kept only closest):\n - {string.Join("\n - ", neighboringRegionsCache.Select(o => $"{o}=>{regions[o]}"))}");

                        nextRegion = neighboringRegionsCache[0];
                        if (neighboringRegionsCache.Count > 1) {
                            neighboringRegionsCache.Sort();
                            nextRegion = neighboringRegionsCache[random.Next(neighboringRegionsCache.Count - 1)];
                        }
                    }


                    Logger.Debug($"Beast {beastIndex} picked region {nextRegion} for next movement: {regions[nextRegion]}");

                    effect = new MoveBeastEffect() {
                        beastIndex = beastIndex,
                        newRegionIndex = nextRegion,
                        previousRegionIndex = beast.regionIndex
                    };

                    return true;
                }
                else {
                    Logger.Debug($"Beast {beastIndex} has no option for movement!");
                }
            }

            return false;
        }

        private bool CanCrossRegion(in Beast beast, int regionIndex, in GameState state)
        {
            if (beast.Navigation.hardHotPathAvoidance) {
                if (beast.hotPath.Contains(regionIndex)) {
                    return false;
                }
            }

            if (beast.Navigation.hardTakenLandsAvoidance) {
                if (state.world.Regions[regionIndex].isOwned && (!beast.Navigation.preferFields || state.world.Regions[regionIndex].Building != EBuilding.Fields)) {
                    return false;
                }
            }

            if (state.board is BeastWorldBoard board && board.beasts.Any(b => b.regionIndex == regionIndex)) {
                return false;
            }

            if (state.world.Regions[regionIndex].Building.HasFlagSafe(EBuilding.Capital)) {
                return false;
            }

            return true;
        }

        public IBoard Duplicate()
        {
            return new BeastWorldBoard() {
                beasts = beasts.Select(o => o.Duplicate()).ToArray()
            };
        }

        public void Read(BinaryReader from)
        {
            byte version = from.ReadByte();
            from.Read(version, ref beasts);
        }

        public void Write(BinaryWriter into)
        {
            into.Write(VERSION);
            into.Write(beasts);
        }

        public bool IsAnyBeastOn(int regionIndex, out byte beastIndex)
        {
            for (byte i = 0; i < beasts.Length; i++) {
                if (beasts[i].regionIndex == regionIndex) {
                    beastIndex = i;
                    return true;
                }
            }

            beastIndex = default;
            return false;
        }

        private static int SortForBeast(in Beast beast, in GameState state, int regionA, int regionB)
        {
            int comparator = 0;


            // Prefer (or avoid) fields
            comparator = (state.world.Regions[regionA].RelevantBuilding == EBuilding.Fields)
                .CompareTo((state.world.Regions[regionB].RelevantBuilding == EBuilding.Fields));
            if (comparator != 0) {
                return beast.Navigation.preferFields ? -comparator : comparator;
            }

            // Avoid (or prefer) owned regions
            comparator = state.world.Regions[regionA].isOwned.CompareTo(state.world.Regions[regionB].isOwned);
            if (comparator != 0) {
                return beast.Navigation.avoidsTakenLands ? comparator : -comparator;
            }


            // Avoid (or prefer) buildings
            comparator = (state.world.Regions[regionA].RelevantBuilding != EBuilding.None && state.world.Regions[regionA].RelevantBuilding != EBuilding.Fields)
                .CompareTo((state.world.Regions[regionB].RelevantBuilding != EBuilding.None && state.world.Regions[regionB].RelevantBuilding != EBuilding.Fields));

            if (comparator != 0) {
                return beast.Navigation.avoidsNonFieldBuildings ? comparator : -comparator;
            }

            // Avoid hot path
            comparator = beast.hotPath.Contains(regionA)
               .CompareTo(beast.hotPath.Contains(regionB));

            if (comparator != 0) {
                return comparator;
            }

            return 0;
        }

        public bool IsRegionReserved(in GameState state, int regionIndex)
        {
            if (state.board is BeastWorldBoard beastWorld) {
                for (int i = 0; i < beastWorld.beasts.Length; i++) {
                    if (beastWorld.beasts[i].regionIndex == regionIndex) {
                        if (beastWorld.beasts[i].IsEnraged) {
                            return state.rules.board.beastWorld.enragedBeastTileIsReserved;
                        }
                        else {
                            return state.rules.board.beastWorld.calmBeastTileIsReserved;
                        }
                    }
                }
            }

            return false;
        }
    }
}