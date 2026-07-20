
namespace LouveSystems.K2.Lib
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public static class Extensions
    {
        public static bool IsDe(this char c)
        {
            c = char.ToLowerInvariant(c);
            return !c.IsVowel();
        }

        public static bool IsVowel(this char c)
        {
            c = char.ToLowerInvariant(c);
            return c == 'a' || c == 'e' || c == 'i' || c == 'u' || c == 'o' || c == 'y' ||
                c == 'ô' || c == 'é' || c == 'è' || c == 'ù' || c == 'ë' || c == '^';
        }

        public static bool HasFlagSafe<T>(this T value, T flag) where T : Enum
        {
            return value.HasFlag(flag);
        }

        public static void ForEach<T>(this IReadOnlyList<T> list, Action<T> action)
        {
            for (int i = 0; i < list.Count; i++) {
                action(list[i]);
            }
        }

        public static List<T> FindAll<T>(this IReadOnlyList<T> list, Predicate<T> action)
        {
            List<T> results = new List<T>();
            for (int i = 0; i < list.Count; i++) {
                if (action(list[i])) {
                    results.Add(list[i]);
                }
            }

            return results;
        }


        public static T Find<T>(this IReadOnlyList<T> list, Predicate<T> action)
        {
            for (int i = 0; i < list.Count; i++) {
                if (action(list[i])) {
                    return list[i];
                }
            }

            return default;
        }

        public static int FindIndex<T>(this IReadOnlyList<T> list, Predicate<T> action)
        {
            for (int i = 0; i < list.Count; i++) {
                if (action(list[i])) {
                    return i;
                }
            }

            return -1;
        }

        public static int Sum<T>(this IReadOnlyList<T> list, Func<T, int> getter)
        {
            int result = 0;
            for (int i = 0; i < list.Count; i++) {
                result += getter(list[i]);
            }

            return result;
        }

        public static void Shuffle<T>(this IList<T> list)
        {
            int r = (int)(UnityEngine.Random.value * ushort.MaxValue);
            int n = list.Count;
            while (n > 1) {
                n--;
                int k = Math.Abs(r) % n;
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }

        public static void Shuffle<T>(this IList<T> list, ManagedRandom random)
        {
            int n = list.Count;
            while (n > 1) {
                n--;
                int k = random.Next(n);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }

        public static int MultiplyByPercentage(this int integer, int percentage0100)
        {
            integer *= percentage0100;

            return integer / 100;
        }

        public static int Hash<T>(params T[][] hashables) where T : IHashable
        {
            int[] hashes = new int[hashables.Length];
            for (int i = 0; i < hashables.Length; i++) {
                hashes[i] = Hash(hashables[i]);
            }

            return Hash(hashes);
        }

        public static int Hash<T>(params T[] hashables) where T : IHashable
        {
            int hash = 17;

            void addToHash(int i)
            {
                unchecked // Overflow is fine, just wrap
                {
                    hash = hash * 23 + i;
                }
            }

            for (int i = 0; i < hashables.Length; i++) {
                int hashResult = hashables[i].GetHash();
                Logger.Trace($"Hash of {hashables[i]} is {hashResult:X8}");
                addToHash(hashResult);
            }

            return hash;
        }

        public static int Hash(params byte[] integers)
        {
            int hash = 17;

            void addToHash(int i)
            {
                unchecked // Overflow is fine, just wrap
                {
                    hash = hash * 23 + i;
                }
            }

            for (int i = 0; i < integers.Length; i++) {
                addToHash(integers[i]);
            }

            return hash;
        }

        public static int Hash(params int[] integers)
        {
            int hash = 17;

            void addToHash(int i)
            {
                unchecked // Overflow is fine, just wrap
                {
                    hash = hash * 23 + i;
                }
            }

            for (int i = 0; i < integers.Length; i++) {
                addToHash(integers[i]);
            }

            return hash;
        }

        // 100 first integers
        private static readonly IReadOnlyList<int> sqrtLookupTable = new int[] { 0, 1, 1, 1, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9 };

        public static int IntegerSqrt(this int number)
        {
            if (sqrtLookupTable.Count > number) {
                return sqrtLookupTable[number];
            }

            // start iteration from 1 until the 
            // square of a number exceeds n
            int root = 1;
            while (root * root <= number) {
                root++;
            }

            // return the largest integer whose 
            // square is less than or equal to n
            return root - 1;
        }

        public static void AddRange<T>(this ICollection<T> collection, IReadOnlyCollection<T> toAdd)
        {
            foreach(var elem in toAdd) {
                collection.Add(elem);
            }
        }

        public static byte EnsurePositiveEqualOrUnder(this byte value, byte ceiling)
        {
            if (value > ceiling) { // means it has to be > 0 too
                return ceiling;
            }

            return value;
        }

        public static int EnsurePositiveEqualOrUnder(this int value, int ceiling)
        {
            if (value < 0) {
                return 0;
            }

            if (value >= ceiling) {
                return ceiling;
            }

            return value;
        }

        public static int EnsurePositiveUnder(this int value, int ceiling)
        {
            int clamped = value.EnsurePositiveEqualOrUnder(ceiling);

            if (clamped >= ceiling) {
                return ceiling - 1;
            }

            return clamped;
        }

        public static IEnumerable<T> ForEach<T>(this IEnumerable<T> enumeration, Func<T, T> action)
        {
            return enumeration.Select(o => action(o));
        }

        public static ERegionAttackType ToAttackType(this EFactionFlag faction)
        {
            ERegionAttackType t = ERegionAttackType.Standard;

            if (faction.HasFlagSafe(EFactionFlag.Charge)) {
                t |= ERegionAttackType.Charge;
            }

            if (faction.HasFlagSafe(EFactionFlag.SlitherAttacksBetweenRegions)) {
                t |= ERegionAttackType.Slithering;
            }

            return t;
        }

        public static bool IsDecoy(this EBuilding building)
        {
            return building.HasFlagSafe(EBuilding.Decoy);
        }

        public static EBuilding GetBuildingOrRawDecoy(this EBuilding building)
        {
            return building.IsDecoy() ? EBuilding.Decoy : building;
        }

        public static EBuilding GetPretensedBuilding(this EBuilding building)
        {
            return building & ~EBuilding.Decoy;
        }
    }
}