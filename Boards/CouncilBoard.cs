
namespace LouveSystems.K2.Lib
{
    using System.Collections.Generic;
    using System.IO;

    public struct CouncilBoard : IBoard
    {
        public EBoardType Type => EBoardType.CouncilRegion;


        public IBoard Duplicate()
        {
            return new CouncilBoard();
        }

        public void ComputeEffects(ManagedRandom random, in GameState state, in List<ITransformEffect> effects)
        {
        }

        public void Write(BinaryWriter into)
        {
        }

        public void Read(BinaryReader from)
        {
        }

        public bool IsRegionReserved(in GameState state, int regionIndex)
        {
            return state.world.IsCouncilRegion(regionIndex);
        }
    }
}