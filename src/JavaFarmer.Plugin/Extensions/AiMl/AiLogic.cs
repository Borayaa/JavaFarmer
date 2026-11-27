// AI / ML puzzles (docs/EXTENSIONS.md §4). Pure logic: no game types. Datasets are regenerated deterministically
// from (save seed, kind, counter); only counters and the player's submitted values are saved.
using System;
using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Plugin.Extensions.AiMl
{
    /// <summary>Balance knobs of the AI / ML extension (all rewards in Insight). Reward = floor(Base * q^Power), q in [0, 1].</summary>
    public static class AiBalance
    {
        public const double MeanBase = 10, LineBase = 40, ClassifyBase = 800, ClusterBase = 1200, NnBase = 300, RlBase = 8000;
        public const int MeanPower = 4, LinePower = 8, ClassifyPower = 4, ClusterPower = 4, NnPower = 4, RlPower = 4;
        public static double Reward(double baseReward, double q, int power)
        {
            if (double.IsNaN(q) || q <= 0) return 0;
            if (q > 1) q = 1;
            return Math.Floor(baseReward * Math.Pow(q, power) + 1e-9);
        }
        public static double R2(double v) => Math.Round(v, 2);
    }

    // ---------------------------------------------------------------- regression dataset (machine_learning, linear_regression)

    public sealed class RegressionData
    {
        public long Counter, Submitted;
        public List<double> X { get; private set; }
        public List<double> Y { get; private set; }
        public double OptimalMse { get; private set; }
        public double OptimalR2 { get; private set; }
        public double OlsSlope { get; private set; }
        public double OlsIntercept { get; private set; }

        public void Ensure(ulong seed) { if (X == null) Generate(seed); }

        void Generate(ulong seed)
        {
            var r = ExtRandom.For(seed, "regression", Counter);
            int n = r.Range(20, 40);
            double a = r.Uniform(-3, 5), b = r.Uniform(2, 10), sigma = r.Uniform(0.5, 2.5);
            X = new List<double>(); Y = new List<double>();
            for (int i = 0; i < n; i++)
            {
                double x = AiBalance.R2(r.Uniform(0, 10));
                X.Add(x);
                Y.Add(AiBalance.R2(a * x + b + 10 + sigma * r.Gaussian()));
            }
            double mx = X.Average(), my = Y.Average();
            OptimalMse = Y.Sum(y => (y - my) * (y - my)) / n;
            double sxy = 0, sxx = 0;
            for (int i = 0; i < n; i++) { sxy += (X[i] - mx) * (Y[i] - my); sxx += (X[i] - mx) * (X[i] - mx); }
            OlsSlope = sxx == 0 ? 0 : sxy / sxx;
            OlsIntercept = my - OlsSlope * mx;
            OptimalR2 = RSquared(OlsSlope, OlsIntercept);
        }

        public void Next(ulong seed) { Counter++; Generate(seed); }

        public double Mse(double v) => Y.Sum(y => (y - v) * (y - v)) / Y.Count;

        public double RSquared(double slope, double intercept)
        {
            double my = Y.Average(), ssTot = 0, ssRes = 0;
            for (int i = 0; i < Y.Count; i++)
            {
                double e = Y[i] - (slope * X[i] + intercept);
                ssRes += e * e;
                ssTot += (Y[i] - my) * (Y[i] - my);
            }
            return ssTot == 0 ? 1 : 1 - ssRes / ssTot;
        }

        /// <summary>Scores a constant prediction, starts the next dataset. Returns the MSE.</summary>
        public double SubmitPrediction(ulong seed, double v, out double reward)
        {
            double mse = Mse(v);
            double q = mse <= 0 ? 1 : OptimalMse / mse;
            reward = AiBalance.Reward(AiBalance.MeanBase, q, AiBalance.MeanPower);
            Submitted++;
            Next(seed);
            return mse;
        }

        /// <summary>Scores a line, starts the next dataset. Returns R².</summary>
        public double SubmitLine(ulong seed, double slope, double intercept, out double reward)
        {
            double r2 = RSquared(slope, intercept);
            double q = OptimalR2 <= 0 ? 0 : r2 / OptimalR2;
            reward = AiBalance.Reward(AiBalance.LineBase, q, AiBalance.LinePower);
            Submitted++;
            Next(seed);
            return r2;
        }

        public Dictionary<string, object> ToJson() => new Dictionary<string, object> { ["counter"] = (double)Counter, ["submitted"] = (double)Submitted };
        public void FromJson(Dictionary<string, object> o) { Counter = (long)ExtJson.Num(o, "counter"); Submitted = (long)ExtJson.Num(o, "submitted"); X = null; }
    }

    // ---------------------------------------------------------------- classification (perceptron)

    public sealed class ClassificationData
    {
        public const int TestSize = 100;
        public long Counter, Submitted;
        public List<double> X { get; private set; }
        public List<double> Y { get; private set; }
        public List<double> Label { get; private set; }
        public List<double> TestX { get; private set; }
        public List<double> TestY { get; private set; }
        public List<double> TestLabel { get; private set; }
        /// <summary>The hidden true boundary w·p + b = 0 (for tests).</summary>
        public double Wx, Wy, B;

        public void Ensure(ulong seed) { if (X == null) Generate(seed); }

        void Generate(ulong seed)
        {
            var r = ExtRandom.For(seed, "classification", Counter);
            double theta = r.Uniform(0, 2 * Math.PI);
            Wx = Math.Cos(theta); Wy = Math.Sin(theta);
            double px = r.Uniform(3, 7), py = r.Uniform(3, 7);
            B = -(Wx * px + Wy * py);
            X = new List<double>(); Y = new List<double>(); Label = new List<double>();
            TestX = new List<double>(); TestY = new List<double>(); TestLabel = new List<double>();
            Fill(r, 40, X, Y, Label, 0.03);
            Fill(r, TestSize, TestX, TestY, TestLabel, 0);
        }

        void Fill(ExtRandom r, int n, List<double> xs, List<double> ys, List<double> ls, double noise)
        {
            while (xs.Count < n)
            {
                double x = AiBalance.R2(r.Uniform(0, 10)), y = AiBalance.R2(r.Uniform(0, 10));
                double d = Wx * x + Wy * y + B;
                if (Math.Abs(d) < 0.4) continue;
                double label = d >= 0 ? 1 : -1;
                if (noise > 0 && r.Chance(noise)) label = -label;
                xs.Add(x); ys.Add(y); ls.Add(label);
            }
        }

        public void Next(ulong seed) { Counter++; Generate(seed); }

        public double Accuracy(double wx, double wy, double b)
        {
            int ok = 0;
            for (int i = 0; i < TestX.Count; i++)
            {
                double pred = wx * TestX[i] + wy * TestY[i] + b >= 0 ? 1 : -1;
                if (pred == TestLabel[i]) ok++;
            }
            return (double)ok / TestX.Count;
        }

        public double Submit(ulong seed, double wx, double wy, double b, out double reward)
        {
            double acc = Accuracy(wx, wy, b);
            reward = AiBalance.Reward(AiBalance.ClassifyBase, (acc - 0.5) / 0.5, AiBalance.ClassifyPower);
            Submitted++;
            Next(seed);
            return acc;
        }

        public Dictionary<string, object> ToJson() => new Dictionary<string, object> { ["counter"] = (double)Counter, ["submitted"] = (double)Submitted };
        public void FromJson(Dictionary<string, object> o) { Counter = (long)ExtJson.Num(o, "counter"); Submitted = (long)ExtJson.Num(o, "submitted"); X = null; }
    }

    // ---------------------------------------------------------------- clustering (k-means)

    public sealed class ClusterData
    {
        public long Counter, Submitted;
        public int K { get; private set; }
        public List<double> X { get; private set; }
        public List<double> Y { get; private set; }
        public double ReferenceInertia { get; private set; }
        /// <summary>Submitted centroids (NaN = not submitted).</summary>
        public List<double> Cx = new List<double>(), Cy = new List<double>();

        public void Ensure(ulong seed) { if (X == null) Generate(seed); }

        void Generate(ulong seed)
        {
            var r = ExtRandom.For(seed, "clustering", Counter);
            K = r.Range(3, 5);
            var centers = new List<(double x, double y)>();
            while (centers.Count < K)
            {
                var c = (r.Uniform(2, 18), r.Uniform(2, 18));
                if (centers.All(o => (o.Item1 - c.Item1) * (o.Item1 - c.Item1) + (o.Item2 - c.Item2) * (o.Item2 - c.Item2) >= 25)) centers.Add(c);
            }
            X = new List<double>(); Y = new List<double>();
            var order = new List<(double, double)>();
            foreach (var c in centers)
            {
                int n = r.Range(15, 25);
                double s = r.Uniform(0.8, 1.6);
                for (int i = 0; i < n; i++) order.Add((AiBalance.R2(c.x + s * r.Gaussian()), AiBalance.R2(c.y + s * r.Gaussian())));
            }
            r.Shuffle(order);
            foreach (var p in order) { X.Add(p.Item1); Y.Add(p.Item2); }
            ReferenceInertia = Reference(r);
            if (Cx.Count != K) ClearCentroids();
        }

        public void ClearCentroids()
        {
            Cx = Enumerable.Repeat(double.NaN, K).ToList();
            Cy = Enumerable.Repeat(double.NaN, K).ToList();
        }

        /// <summary>Best inertia of 10 k-means++ runs (the score's reference).</summary>
        double Reference(ExtRandom r)
        {
            double best = double.MaxValue;
            int n = X.Count;
            for (int run = 0; run < 10; run++)
            {
                var cx = new List<double>(); var cy = new List<double>();
                int first = r.Next(n);
                cx.Add(X[first]); cy.Add(Y[first]);
                while (cx.Count < K)
                {
                    var d = new double[n];
                    double sum = 0;
                    for (int i = 0; i < n; i++) { d[i] = Nearest(X[i], Y[i], cx, cy); sum += d[i]; }
                    double pick = r.NextDouble() * sum;
                    int idx = 0;
                    for (; idx < n - 1; idx++) { pick -= d[idx]; if (pick <= 0) break; }
                    cx.Add(X[idx]); cy.Add(Y[idx]);
                }
                for (int iter = 0; iter < 50; iter++)
                {
                    var sx = new double[K]; var sy = new double[K]; var cnt = new int[K];
                    for (int i = 0; i < n; i++)
                    {
                        int j = NearestIndex(X[i], Y[i], cx, cy);
                        sx[j] += X[i]; sy[j] += Y[i]; cnt[j]++;
                    }
                    for (int j = 0; j < K; j++) if (cnt[j] > 0) { cx[j] = sx[j] / cnt[j]; cy[j] = sy[j] / cnt[j]; }
                }
                best = Math.Min(best, Inertia(cx, cy));
            }
            return best;
        }

        static double Nearest(double x, double y, List<double> cx, List<double> cy)
        {
            double best = double.MaxValue;
            for (int j = 0; j < cx.Count; j++)
            {
                if (double.IsNaN(cx[j])) continue;
                double d = (x - cx[j]) * (x - cx[j]) + (y - cy[j]) * (y - cy[j]);
                if (d < best) best = d;
            }
            return best;
        }

        static int NearestIndex(double x, double y, List<double> cx, List<double> cy)
        {
            int bi = 0;
            double best = double.MaxValue;
            for (int j = 0; j < cx.Count; j++)
            {
                double d = (x - cx[j]) * (x - cx[j]) + (y - cy[j]) * (y - cy[j]);
                if (d < best) { best = d; bi = j; }
            }
            return bi;
        }

        public double Inertia(List<double> cx, List<double> cy)
        {
            double s = 0;
            for (int i = 0; i < X.Count; i++) s += Nearest(X[i], Y[i], cx, cy);
            return s;
        }

        public bool AnySubmitted => Cx.Any(v => !double.IsNaN(v));

        public void SetCentroid(int j, double x, double y) { Cx[j] = x; Cy[j] = y; }

        /// <summary>Scores the submitted centroids, starts the next dataset. Returns the inertia.</summary>
        public double Evaluate(ulong seed, out double reward)
        {
            double inertia = Inertia(Cx, Cy);
            double q = inertia <= 0 ? 1 : ReferenceInertia / inertia;
            reward = AiBalance.Reward(AiBalance.ClusterBase, q, AiBalance.ClusterPower);
            Submitted++;
            Counter++;
            Cx.Clear(); Cy.Clear();
            Generate(seed);
            return inertia;
        }

        public Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            ["counter"] = (double)Counter, ["submitted"] = (double)Submitted,
            ["cx"] = Cx.Select(v => double.IsNaN(v) ? null : (object)v).ToList(), ["cy"] = Cy.Select(v => double.IsNaN(v) ? null : (object)v).ToList(),
        };
        public void FromJson(Dictionary<string, object> o)
        {
            Counter = (long)ExtJson.Num(o, "counter"); Submitted = (long)ExtJson.Num(o, "submitted");
            Cx = ExtJson.List(o, "cx").Select(v => v is double d ? d : double.NaN).ToList();
            Cy = ExtJson.List(o, "cy").Select(v => v is double d ? d : double.NaN).ToList();
            X = null;
        }
    }

    // ---------------------------------------------------------------- neural network (2-2-1, XOR)

    public sealed class NeuralNet
    {
        public const int TestSize = 100;
        public long Counter, Submitted;
        /// <summary>W0[i * 2 + j]: input i (0 = x, 1 = y) to hidden j; W1[i]: hidden i to the output.</summary>
        public double[] W0 = new double[4], B0 = new double[2], W1 = new double[2];
        public double B1;
        public List<double> X { get; private set; }
        public List<double> Y { get; private set; }
        public List<double> Label { get; private set; }
        public List<double> TestX { get; private set; }
        public List<double> TestY { get; private set; }
        public List<double> TestLabel { get; private set; }
        /// <summary>Transformation of the task (for tests): p = (Ox, Oy) + rotate(Theta) * (Scale * corner).</summary>
        public double Ox, Oy, Theta, Scale;

        public static double Sigmoid(double x)
        {
            if (x < -60) return 0;
            if (x > 60) return 1;
            return 1.0 / (1.0 + Math.Exp(-x));
        }

        public void Ensure(ulong seed) { if (X == null) Generate(seed); }

        long transformOf = -1;

        /// <summary>
        /// Task k's square is task k-1's square moved a little (angle +-0.25, shift +-0.8, scale x0.9..1.1): fine-tuning the
        /// previous network is cheaper than training from scratch (transfer learning).
        /// </summary>
        void Transform(ulong seed)
        {
            if (transformOf == Counter) return;
            if (transformOf < 0 || transformOf > Counter)
            {
                var b = ExtRandom.For(seed, "xor-base", 0);
                Scale = b.Uniform(2, 6); Theta = b.Uniform(0, 2 * Math.PI); Ox = b.Uniform(-5, 5); Oy = b.Uniform(-5, 5);
                transformOf = 0;
            }
            for (long k = transformOf + 1; k <= Counter; k++)
            {
                var d = ExtRandom.For(seed, "xor-drift", k);
                Theta += d.Uniform(-0.25, 0.25);
                Ox = Math.Max(-6, Math.Min(6, Ox + d.Uniform(-0.8, 0.8)));
                Oy = Math.Max(-6, Math.Min(6, Oy + d.Uniform(-0.8, 0.8)));
                Scale = Math.Max(2, Math.Min(6, Scale * d.Uniform(0.9, 1.1)));
            }
            transformOf = Counter;
        }

        void Generate(ulong seed)
        {
            Transform(seed);
            var r = ExtRandom.For(seed, "xor", Counter);
            X = new List<double>(); Y = new List<double>(); Label = new List<double>();
            TestX = new List<double>(); TestY = new List<double>(); TestLabel = new List<double>();
            Fill(r, 40, X, Y, Label);
            Fill(r, TestSize, TestX, TestY, TestLabel);
        }

        void Fill(ExtRandom r, int n, List<double> xs, List<double> ys, List<double> ls)
        {
            double c = Math.Cos(Theta), s = Math.Sin(Theta);
            var corners = new List<int>();
            for (int i = 0; i < n; i++) corners.Add(i % 4);
            r.Shuffle(corners);
            for (int i = 0; i < n; i++)
            {
                int ux = corners[i] % 2, uy = corners[i] / 2;
                double px = Scale * (ux + 0.12 * r.Gaussian()), py = Scale * (uy + 0.12 * r.Gaussian());
                xs.Add(AiBalance.R2(Ox + c * px - s * py));
                ys.Add(AiBalance.R2(Oy + s * px + c * py));
                ls.Add(ux ^ uy);
            }
        }

        public double Output(double x, double y)
        {
            double h0 = Sigmoid(W0[0] * x + W0[2] * y + B0[0]);
            double h1 = Sigmoid(W0[1] * x + W0[3] * y + B0[1]);
            return Sigmoid(W1[0] * h0 + W1[1] * h1 + B1);
        }

        public double Accuracy()
        {
            int ok = 0;
            for (int i = 0; i < TestX.Count; i++)
                if ((Output(TestX[i], TestY[i]) >= 0.5 ? 1 : 0) == TestLabel[i]) ok++;
            return (double)ok / TestX.Count;
        }

        /// <summary>Scores the network on the hidden test points, starts the next task (weights are kept). Returns the accuracy.</summary>
        public double Evaluate(ulong seed, out double reward)
        {
            double acc = Accuracy();
            reward = AiBalance.Reward(AiBalance.NnBase, (acc - 0.5) / 0.5, AiBalance.NnPower);
            Submitted++;
            Counter++;
            Generate(seed);
            return acc;
        }

        public Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            ["counter"] = (double)Counter, ["submitted"] = (double)Submitted,
            ["w0"] = W0.Cast<object>().ToList(), ["b0"] = B0.Cast<object>().ToList(), ["w1"] = W1.Cast<object>().ToList(), ["b1"] = B1,
        };
        public void FromJson(Dictionary<string, object> o)
        {
            Counter = (long)ExtJson.Num(o, "counter"); Submitted = (long)ExtJson.Num(o, "submitted");
            Copy(ExtJson.Nums(o, "w0"), W0); Copy(ExtJson.Nums(o, "b0"), B0); Copy(ExtJson.Nums(o, "w1"), W1); B1 = ExtJson.Num(o, "b1");
            X = null;
            transformOf = -1;
        }
        static void Copy(List<double> from, double[] to) { for (int i = 0; i < to.Length; i++) to[i] = i < from.Count ? from[i] : 0; }
    }

    // ---------------------------------------------------------------- reinforcement learning (grid world)

    public sealed class GridWorld
    {
        public const int Size = 5, States = Size * Size, Actions = 4, MaxSteps = 50, EvalEpisodes = 50;
        public const double StepReward = -1, GoalReward = 20, PitReward = -20, Slip = 0.1;
        public long Counter, Submitted, Episode;
        public int State, Steps;
        public bool Done = true;
        public List<double> Policy = Enumerable.Repeat(0.0, States).ToList();
        public int Goal { get; private set; }
        public HashSet<int> Pits { get; private set; }
        ExtRandom env;
        ulong seed;

        public void Ensure(ulong seed)
        {
            this.seed = seed;
            if (Pits == null) Generate(seed);
        }

        void Generate(ulong seed)
        {
            this.seed = seed;
            var r = ExtRandom.For(seed, "gridworld", Counter);
            while (true)
            {
                do Goal = r.Next(States); while (Goal % Size + Goal / Size < 5);
                Pits = new HashSet<int>();
                while (Pits.Count < 3)
                {
                    int p = r.Next(States);
                    if (p != 0 && p != Goal) Pits.Add(p);
                }
                if (Reachable()) break;
            }
            if (Policy.Count != States) Policy = Enumerable.Repeat(0.0, States).ToList();
        }

        bool Reachable()
        {
            var seen = new HashSet<int> { 0 };
            var q = new Queue<int>();
            q.Enqueue(0);
            while (q.Count > 0)
            {
                int s = q.Dequeue();
                if (s == Goal) return true;
                for (int a = 0; a < Actions; a++)
                {
                    int t = Move(s, a);
                    if (!Pits.Contains(t) && seen.Add(t)) q.Enqueue(t);
                }
            }
            return false;
        }

        /// <summary>0 North (y + 1), 1 East (x + 1), 2 South, 3 West. Walking into the fence keeps the drone in place.</summary>
        public static int Move(int s, int a)
        {
            int x = s % Size, y = s / Size;
            switch (a)
            {
                case 0: if (y < Size - 1) y++; break;
                case 1: if (x < Size - 1) x++; break;
                case 2: if (y > 0) y--; break;
                case 3: if (x > 0) x--; break;
            }
            return y * Size + x;
        }

        /// <summary>One step: (next state, reward, done). Always draws exactly two random numbers (restorable).</summary>
        static (int, double, bool) Step(int s, int a, int steps, int goal, HashSet<int> pits, ExtRandom r)
        {
            double slip = r.NextDouble();
            int other = r.Next(Actions);
            if (slip < Slip) a = other;
            int t = Move(s, a);
            if (t == goal) return (t, GoalReward, true);
            if (pits.Contains(t)) return (t, PitReward, true);
            return (t, StepReward, steps + 1 >= MaxSteps);
        }

        public int Reset()
        {
            Episode++;
            env = ExtRandom.For(seed, "gridworld-env" + Counter, Episode);
            State = 0; Steps = 0; Done = false;
            return State;
        }

        public double DoStep(int a)
        {
            if (env == null) RestoreEnv();
            var (t, rew, done) = Step(State, a, Steps, Goal, Pits, env);
            State = t; Steps++; Done = done;
            return rew;
        }

        void RestoreEnv()
        {
            env = ExtRandom.For(seed, "gridworld-env" + Counter, Episode);
            for (int i = 0; i < Steps; i++) { env.NextDouble(); env.Next(Actions); }
        }

        /// <summary>Average return of a policy over the evaluation episodes (same random sequence for every policy of this world).</summary>
        public double Score(IList<double> policy)
        {
            var r = ExtRandom.For(seed, "gridworld-eval", Counter);
            double total = 0;
            for (int ep = 0; ep < EvalEpisodes; ep++)
            {
                int s = 0, steps = 0;
                bool done = false;
                while (!done)
                {
                    var (t, rew, d) = Step(s, (int)policy[s], steps, Goal, Pits, r);
                    total += rew; s = t; steps++; done = d;
                }
            }
            return total / EvalEpisodes;
        }

        /// <summary>Greedy policy of value iteration (the reference "optimal" policy).</summary>
        public List<double> OptimalPolicy()
        {
            var v = new double[States];
            var policy = new List<double>(new double[States]);
            for (int iter = 0; iter < 200; iter++)
            {
                for (int s = 0; s < States; s++)
                {
                    if (s == Goal || Pits.Contains(s)) { v[s] = 0; continue; }
                    double best = double.MinValue;
                    int bestA = 0;
                    for (int a = 0; a < Actions; a++)
                    {
                        double q = 0;
                        for (int real = 0; real < Actions; real++)
                        {
                            double p = (real == a ? 1 - Slip : 0) + Slip / Actions;
                            int t = Move(s, real);
                            double rew = t == Goal ? GoalReward : Pits.Contains(t) ? PitReward : StepReward;
                            q += p * (rew + (t == Goal || Pits.Contains(t) ? 0 : v[t]));
                        }
                        if (q > best) { best = q; bestA = a; }
                    }
                    v[s] = best;
                    policy[s] = bestA;
                }
            }
            return policy;
        }

        /// <summary>Scores the submitted policy, starts a new world. Returns the average return.</summary>
        public double Evaluate(ulong seed, out double reward)
        {
            double score = Score(Policy);
            double opt = Score(OptimalPolicy());
            double q = (score + MaxSteps) / (opt + MaxSteps);
            reward = AiBalance.Reward(AiBalance.RlBase, q, AiBalance.RlPower);
            Submitted++;
            Counter++;
            Episode = 0; Steps = 0; State = 0; Done = true; env = null;
            Policy = Enumerable.Repeat(0.0, States).ToList();
            Generate(seed);
            return score;
        }

        public Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            ["counter"] = (double)Counter, ["submitted"] = (double)Submitted, ["episode"] = (double)Episode, ["state"] = (double)State,
            ["steps"] = (double)Steps, ["done"] = Done, ["policy"] = Policy.Cast<object>().ToList(),
        };
        public void FromJson(Dictionary<string, object> o)
        {
            Counter = (long)ExtJson.Num(o, "counter"); Submitted = (long)ExtJson.Num(o, "submitted"); Episode = (long)ExtJson.Num(o, "episode");
            State = ExtJson.Int(o, "state"); Steps = ExtJson.Int(o, "steps"); Done = ExtJson.Bool(o, "done", true);
            Policy = ExtJson.Nums(o, "policy");
            if (Policy.Count != States) Policy = Enumerable.Repeat(0.0, States).ToList();
            Pits = null; env = null;
        }
    }
}
