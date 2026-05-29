
namespace LouveSystems.K2.Lib
{
    using System.IO;
    using System.Linq;

    public struct BeastWorldBoard : IBoard
    {
        public struct Beast
        {
            public bool IsEnraged => enragedTurnsRemaining > 0;

            public int regionIndex;

            public byte enragedTurnsRemaining;

            public byte lastIdleMoveXTurnsAgo;
        }

        private Beast[] beasts;

        public EBoardType Type => EBoardType.BeastWorld;

        public BeastWorldBoard(GameRules.GlobalBoardSettings.BeastWorldBoardSettings globalBoardSettings, in World world) : this()
        {
            beasts = new Beast[globalBoardSettings.beastCount];

            if (beasts.Length == 1) {
                beasts[0].regionIndex = world.Regions.Count /2;
            }
            else {
                for (int i = 0; i < beasts.Length; i++) {
                    // TODO

                }
            }
        }

        public void ComputeEffects(ManagedRandom random, in GameState state, out ITransformEffect[] effects)
        {

        }

        public IBoard Duplicate()
        {
            return new BeastWorldBoard() {
                beasts = beasts.ToArray()
            };
        }

        public void Read(BinaryReader from)
        {

        }

        public void Write(BinaryWriter into)
        {

        }

    }
}