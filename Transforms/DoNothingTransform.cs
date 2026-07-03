
namespace LouveSystems.K2.Lib
{
    public class DoNothingTransform : Transform
    {
        public override ETransformKind Kind => ETransformKind.DoNothing;

        public DoNothingTransform(byte owningRealmIndex) : base(owningRealmIndex) { }

        public DoNothingTransform() { }
    }
}