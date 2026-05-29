
namespace LouveSystems.K2.Lib
{

    public interface IBoard : IBinarySerializable
    {
        public EBoardType Type { get; }

        public IBoard Duplicate();

        public void ComputeEffects(ManagedRandom random, in GameState state, out ITransformEffect[] effects);

        public static IBoard CreateBoard(GameRules.GlobalBoardSettings settings, in World world)
        {
            switch(settings.type) {
                default:
                    return new EmptyBoard(settings.type);

                case EBoardType.BeastWorld:
                    return new BeastWorldBoard(settings.beastWorld, world);
            }
        }
    }
}