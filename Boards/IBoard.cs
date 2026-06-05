
namespace LouveSystems.K2.Lib
{
    using System.Collections.Generic;

    public interface IBoard : IBinarySerializable
    {
        public EBoardType Type { get; }

        public IBoard Duplicate();

        public void ComputeEffects(ManagedRandom random, in GameState state, in List<ITransformEffect> effects);

        public bool IsRegionReserved(in GameState state, int regionIndex);

        public static IBoard CreateBoard(GameRules.GlobalBoardSettings settings, in World world)
        {
            switch(settings.type) {
                default:
                    return new EmptyBoard(settings.type);

                case EBoardType.CouncilRegion:
                    return new CouncilBoard();

                case EBoardType.BeastWorld:
                    return new BeastWorldBoard(settings.beastWorld, world);
            }
        }
    }
}