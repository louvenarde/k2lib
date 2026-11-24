
namespace LouveSystems.K2.Lib
{
    using System.Collections.Generic;
    using System.IO;

    public struct EmptyBoard : IBoard
    {
        public EBoardType Type { get; }

        public EmptyBoard(EBoardType type)
        {
            this.Type = type;
        }

        public IBoard Duplicate()
        {
            return new EmptyBoard(Type);
        }

        public void ComputeEffects(ManagedRandom random, in GameState state, in List<ITransformEffect> effects)
        {
        }

        public void Write(BinaryWriter into)
        {
        }

        public void Read(byte version, BinaryReader from)
        {
        }

        public bool IsRegionReserved(in GameState state, int regionIndex)
        {
            return false;
        }
    }
}