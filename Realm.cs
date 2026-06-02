
namespace LouveSystems.K2.Lib
{
    using System.IO;
    using System.Collections.Generic;
    using System.Linq;
    using System.Collections;
    using System.Numerics;
    using System;

    public struct Realm : IBinarySerializableWithVersion
    {
        // No reftype buffers? I'll make my reftype buffer.
        public struct ExtremelyNormalConstantSizeList : IList<byte>
        {
            private UInt64 value0;
            private UInt64 value8;

            #region honestly embarassing stuff
            public byte this[int index] {
                get {
                    switch (index) {
                        case 0:
                            return (byte)((value0 & 0xFF) >> index * 8);

                        case 1:
                            return (byte)((value0 & 0xFF00) >> index * 8);

                        case 2:
                            return (byte)((value0 & 0xFF0000) >> index * 8);

                        case 3:
                            return (byte)((value0 & 0xFF000000) >> index * 8);

                        case 4:
                            return (byte)((value0 & 0xFF00000000) >> index * 8);

                        case 5:
                            return (byte)((value0 & 0xFF0000000000) >> index * 8);

                        case 6:
                            return (byte)((value0 & 0xFF000000000000) >> index * 8);

                        case 7:
                            return (byte)((value0 & 0xFF00000000000000) >> index * 8);

                        case 0 + 8:
                            return (byte)((value8 & 0xFF) >> index * 8);

                        case 1 + 8:
                            return (byte)((value8 & 0xFF00) >> index * 8);

                        case 2 + 8:
                            return (byte)((value8 & 0xFF0000) >> index * 8);

                        case 3 + 8:
                            return (byte)((value8 & 0xFF000000) >> index * 8);

                        case 4 + 8:
                            return (byte)((value8 & 0xFF00000000) >> index * 8);

                        case 5 + 8:
                            return (byte)((value8 & 0xFF0000000000) >> index * 8);

                        case 6 + 8:
                            return (byte)((value8 & 0xFF000000000000) >> index * 8);

                        case 7 + 8:
                            return (byte)((value8 & 0xFF00000000000000) >> index * 8);

                    }

                    throw new System.ArgumentOutOfRangeException();
                }
                set {

                    {
                        switch (index) {
                            case 0:
                                value0 &= ~(ulong)0xFF;
                                value0 |= (((ulong)value << (index * 8)) & 0xFF);
                                break;

                            case 1:
                                value0 &= ~(ulong)0xFF00;
                                value0 |= (((ulong)value << (index * 8)) & 0xFF00);
                                break;

                            case 2:
                                value0 &= ~(ulong)0xFF0000;
                                value0 |= (((ulong)value << (index * 8)) & 0xFF0000);
                                break;

                            case 3:
                                value0 &= ~(ulong)0xFF000000;
                                value0 |= (((ulong)value << (index * 8)) & 0xFF000000);
                                break;

                            case 4:
                                value0 &= ~(ulong)0xFF00000000;
                                value0 |= (((ulong)value << (index * 8)) & 0xFF00000000);
                                break;

                            case 5:
                                value0 &= ~(ulong)0xFF0000000000;
                                value0 |= (((ulong)value << (index * 8)) & 0xFF0000000000);
                                break;

                            case 6:
                                value0 &= ~(ulong)0xFF000000000000;
                                value0 |= (((ulong)value << (index * 8)) & 0xFF000000000000);
                                break;

                            case 7:
                                value0 &= ~(ulong)0xFF00000000000000;
                                value0 |= (((ulong)value << (index * 8)) & 0xFF00000000000000);
                                break;

                            case 0 + 8:
                                value8 &= ~(ulong)0xFF;
                                value8 |= (((ulong)value << (index * 8)) & 0xFF);
                                break;

                            case 1 + 8:
                                value8 &= ~(ulong)0xFF00;
                                value8 |= (((ulong)value << (index * 8)) & 0xFF00);
                                break;

                            case 2 + 8:
                                value8 &= ~(ulong)0xFF0000;
                                value8 |= (((ulong)value << (index * 8)) & 0xFF0000);
                                break;

                            case 3 + 8:
                                value8 &= ~(ulong)0xFF000000;
                                value8 |= (((ulong)value << (index * 8)) & 0xFF000000);
                                break;

                            case 4 + 8:
                                value8 &= ~(ulong)0xFF00000000;
                                value8 |= (((ulong)value << (index * 8)) & 0xFF00000000);
                                break;

                            case 5 + 8:
                                value8 &= ~(ulong)0xFF0000000000;
                                value8 |= (((ulong)value << (index * 8)) & 0xFF0000000000);
                                break;

                            case 6 + 8:
                                value8 &= ~(ulong)0xFF000000000000;
                                value8 |= (((ulong)value << (index * 8)) & 0xFF000000000000);
                                break;

                            case 7 + 8:
                                value8 &= ~(ulong)0xFF00000000000000;
                                value8 |= (((ulong)value << (index * 8)) & 0xFF00000000000000);
                                break;
                        }

                    }
                }
            }

            public int Count => 16;

            public bool IsReadOnly => false;

            public void Add(byte item)
            {
                throw new System.NotImplementedException();
            }

            public void Clear()
            {
                for (int i = 0; i < Count; i++) {
                    this[i] = default;
                }
            }

            public bool Contains(byte item)
            {
                for (int i = 0; i < Count; i++) {
                    if (this[i] == item) return true;
                }

                return false;
            }

            public void CopyTo(byte[] array, int arrayIndex)
            {
                for (int i = 0; i < Count; i++) {
                    array[i + arrayIndex] = this[i];
                }
            }

            public IEnumerator<byte> GetEnumerator()
            {
                int position = 0; // state
                while (position < Count-1) {
                    position++;
                    yield return this[position];
                }
            }

            public int IndexOf(byte item)
            {
                for (int i = 0; i < Count; i++) {
                    if (this[i] == item) {
                        return i;
                    }
                }

                return -1;
            }

            public void Insert(int index, byte item)
            {
                throw new System.NotImplementedException();
            }

            public bool Remove(byte item)
            {
                throw new System.NotImplementedException();
            }

            public void RemoveAt(int index)
            {
                throw new System.NotImplementedException();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                int position = 0; // state
                while (position < Count-1) {
                    position++;
                    yield return this[position];
                }
            }
#endregion
        }

        public int silverTreasury;
        public int availableDecisions;
        public bool isFavoured;
        public byte factionIndex;

        public bool isSubjugated;
        public byte subjugatedBy;

        public byte beastsAttacked;

        private ExtremelyNormalConstantSizeList subjugatingAttacksReceived;

        public int GetHash()
        {
            Logger.Trace($"Obtaining hash of {this}...");
            Logger.Trace($"-> Hash of {nameof(silverTreasury)} is {Extensions.Hash(silverTreasury):X8}");
            Logger.Trace($"-> Hash of {nameof(availableDecisions)} is {Extensions.Hash(availableDecisions):X8}");
            Logger.Trace($"-> Hash of {nameof(isFavoured)} is {Extensions.Hash((isFavoured ? 1 : 0)):X8}");
            Logger.Trace($"-> Hash of {nameof(factionIndex)} is {Extensions.Hash(factionIndex):X8}");
            Logger.Trace($"-> Hash of {nameof(subjugatingAttacksReceived)} is...");
            for (int i = 0; i < subjugatingAttacksReceived.Count; i++) {
                Logger.Trace($"---> Hash of {nameof(subjugatingAttacksReceived)}[{i}] is {Extensions.Hash(subjugatingAttacksReceived[i])}");
            }

            Logger.Trace($"-> Hash of {nameof(beastsAttacked)} is {Extensions.Hash(beastsAttacked):X8}");


            int hash = Extensions.Hash(
                silverTreasury,
                availableDecisions,
                isFavoured ? 1 : 0,
                factionIndex,
                Extensions.Hash(subjugatingAttacksReceived.ToArray())
            );

            Logger.Trace($"-> Resulting hash is {hash:X8}");
            return hash;
        }

        public bool IsUnderThreatOfSubjugation(out byte[] potentialSubjugators)
        {
            int threats = subjugatingAttacksReceived.Count(o => o > 0);
            if (threats > 0) {

                // Keys are the subjugators
                List<byte> potentialSubjugatorsList = new List<byte>(threats);
                for (byte realmIndex = 0; realmIndex < subjugatingAttacksReceived.Count; realmIndex++) {
                    if (subjugatingAttacksReceived[realmIndex] > 0) {
                        potentialSubjugatorsList.Add(realmIndex);
                    }
                }
                
                potentialSubjugators = potentialSubjugatorsList.ToArray();

                return true;
            }

            potentialSubjugators = default;
            return false;
        }

        public byte GetSubjugatingAttacksReceived(byte fromRealmIndex)
        {
            return subjugatingAttacksReceived[fromRealmIndex];
        }

        public void RemoveSubjugatingAttackFrom(byte fromRealmIndex)
        {
            subjugatingAttacksReceived[fromRealmIndex]--;
        }

        public void ClearSubjugatingAttacks(byte fromRealmIndex)
        {
            subjugatingAttacksReceived[fromRealmIndex] = 0;
        }

        public void ClearSubjugatingAttacks()
        {
            subjugatingAttacksReceived.Clear();
        }

        public void AddSubjugatingAttackFrom(byte realmIndex)
        {
            subjugatingAttacksReceived[realmIndex]++;
        }

        public bool IsSubjugated(out byte subjugatingRealmIndex)
        {
            if (isSubjugated) {
                subjugatingRealmIndex = subjugatedBy;
                return true;
            }

            subjugatingRealmIndex = default;
            return false;
        }

        public void Read(byte version, BinaryReader from)
        {
            silverTreasury = from.ReadInt32();
            availableDecisions = from.ReadInt32();
            isFavoured = from.ReadBoolean();
            factionIndex = from.ReadByte();
            beastsAttacked = from.ReadByte();
        }

        public void Write(BinaryWriter into)
        {
            into.Write(silverTreasury);
            into.Write(availableDecisions);
            into.Write(isFavoured);
            into.Write(factionIndex);
            into.Write(beastsAttacked);
        }

        public override string ToString()
        {
            return $"Realm [faction {factionIndex}] [{silverTreasury / 10f:n1} $] [favoured? {isFavoured}] [decisions: {availableDecisions}]";
        }
    }
}