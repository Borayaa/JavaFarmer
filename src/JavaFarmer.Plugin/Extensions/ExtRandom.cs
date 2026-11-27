// Deterministic random numbers for extension puzzles (SplitMix64): identical on Mono and .NET, so a puzzle depends
// only on (per-save seed, puzzle kind, counter) and tests can pin exact values.
using System;

namespace JavaFarmer.Plugin.Extensions
{
    public sealed class ExtRandom
    {
        ulong state;

        public ExtRandom(ulong seed) { state = seed; }

        /// <summary>RNG for puzzle number counter of the given kind ("vault", "crypto" ...).</summary>
        public static ExtRandom For(ulong seed, string kind, long counter)
        {
            ulong h = 1469598103934665603UL;
            foreach (char c in kind ?? "") { h ^= c; h *= 1099511628211UL; }
            var r = new ExtRandom(seed ^ Mix(h) ^ Mix((ulong)counter * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL));
            r.NextULong();
            return r;
        }

        static ulong Mix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public ulong NextULong()
        {
            state += 0x9E3779B97F4A7C15UL;
            return Mix(state);
        }

        /// <summary>[0, 1)</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

        /// <summary>[0, n)</summary>
        public int Next(int n) => n <= 1 ? 0 : (int)(NextULong() % (ulong)n);

        /// <summary>[lo, hi] inclusive</summary>
        public int Range(int lo, int hi) => lo + Next(hi - lo + 1);

        public double Uniform(double lo, double hi) => lo + (hi - lo) * NextDouble();

        public bool Chance(double p) => NextDouble() < p;

        /// <summary>Standard normal (Box-Muller).</summary>
        public double Gaussian()
        {
            double u1 = 1.0 - NextDouble(), u2 = NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }

        public void Shuffle<T>(System.Collections.Generic.IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Next(i + 1);
                var t = list[i]; list[i] = list[j]; list[j] = t;
            }
        }

        /// <summary>A fresh per-save seed (not deterministic: used once, when a save first uses an extension).</summary>
        public static ulong NewSeed()
        {
            var g = Guid.NewGuid().ToByteArray();
            ulong a = BitConverter.ToUInt64(g, 0), b = BitConverter.ToUInt64(g, 8);
            return Mix(a ^ Mix(b ^ (ulong)DateTime.UtcNow.Ticks));
        }
    }
}
