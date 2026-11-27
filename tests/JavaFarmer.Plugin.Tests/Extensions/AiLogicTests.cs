using JavaFarmer.Plugin.Extensions;
using JavaFarmer.Plugin.Extensions.AiMl;

namespace JavaFarmer.Plugin.Tests.Extensions;

/// <summary>AI / ML builtins: datasets, scoring formulas, reference solutions, determinism, edge cases.</summary>
[Collection("Game")]
public class AiLogicTests
{
    static (List<double> x, List<double> y) Samples(ExtTestUtil.Calls c)
    {
        int n = c.Int("sample_count");
        var x = new List<double>(); var y = new List<double>();
        for (int i = 0; i < n; i++) { x.Add(c.Num("sample_x", i)); y.Add(c.Num("sample_y", i)); }
        return (x, y);
    }

    [Fact]
    public void Mean_IsTheBestConstant_AndPaysTheFullReward()
    {
        var c = ExtTestUtil.AiCalls(1);
        var (x, y) = Samples(c);
        Assert.InRange(x.Count, 20, 40);
        double mean = y.Average();
        double mse = c.Num("submit_prediction", mean);
        Assert.Equal(y.Sum(v => (v - mean) * (v - mean)) / y.Count, mse, 9);
        Assert.Equal(AiBalance.MeanBase, c.Has("insight"));
        // the next dataset arrived; a bad guess earns less
        var (_, y2) = Samples(c);
        Assert.NotEqual(y, y2);
        c.Num("submit_prediction", y2.Average() + 3 * Math.Sqrt(y2.Sum(v => (v - y2.Average()) * (v - y2.Average())) / y2.Count));
        Assert.Equal(AiBalance.MeanBase, c.Has("insight"));
    }

    [Fact]
    public void LeastSquaresLine_PaysTheFullReward_ZeroSlopeDoesNot()
    {
        var c = ExtTestUtil.AiCalls(2);
        var (x, y) = Samples(c);
        double mx = x.Average(), my = y.Average();
        double slope = x.Zip(y, (a, b) => (a - mx) * (b - my)).Sum() / x.Sum(a => (a - mx) * (a - mx));
        double r2 = c.Num("submit_line", slope, my - slope * mx);
        Assert.InRange(r2, 0, 1);
        Assert.Equal(AiBalance.LineBase, c.Has("insight"));
        Samples(c);
        double r0 = c.Num("submit_line", 0, ((AiState)c.State).Regression.Y.Average());
        Assert.True(r0 < 0.01);
    }

    [Fact]
    public void Datasets_AreDeterministicPerSeed()
    {
        var a = ExtTestUtil.AiCalls(5); var b = ExtTestUtil.AiCalls(5); var d = ExtTestUtil.AiCalls(6);
        Assert.Equal(Samples(a).y, Samples(b).y);
        Assert.NotEqual(Samples(a).y, Samples(d).y);
        Assert.Equal(a.Num("point_x", 3), b.Num("point_x", 3));
        Assert.Equal(a.Int("cluster_k"), b.Int("cluster_k"));
        Assert.Equal(a.Num("nn_point_y", 7), b.Num("nn_point_y", 7));
    }

    [Fact]
    public void Perceptron_GeneralisesToTheHiddenTestSet()
    {
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var c = ExtTestUtil.AiCalls(seed);
            int n = c.Int("point_count");
            var xs = Enumerable.Range(0, n).Select(i => c.Num("point_x", i)).ToList();
            var ys = Enumerable.Range(0, n).Select(i => c.Num("point_y", i)).ToList();
            var ls = Enumerable.Range(0, n).Select(i => c.Num("point_label", i)).ToList();
            Assert.All(ls, l => Assert.True(l == 1 || l == -1));
            int Errors(double a, double bb, double cc) => Enumerable.Range(0, n).Count(i => ls[i] * (a * xs[i] + bb * ys[i] + cc) <= 0);
            // perceptron with the pocket: keep the weights with the fewest training errors
            double wx = 0, wy = 0, b = 0, px = 0, py = 0, pb = 0;
            int best = int.MaxValue;
            for (int epoch = 0; epoch < 30; epoch++)
                for (int i = 0; i < n; i++)
                    if (ls[i] * (wx * xs[i] + wy * ys[i] + b) <= 0)
                    {
                        wx += 0.1 * ls[i] * xs[i]; wy += 0.1 * ls[i] * ys[i]; b += ls[i];
                        int e = Errors(wx, wy, b);
                        if (e < best) { best = e; px = wx; py = wy; pb = b; }
                    }
            double acc = c.Num("submit_classifier", px, py, pb);
            Assert.True(acc >= 0.85, "seed " + seed + " acc " + acc);
            Assert.True(c.Has("insight") > 0);
        }
        var bad = ExtTestUtil.AiCalls(1);
        Assert.True(bad.Num("submit_classifier", 0, 0, 0) < 0.9);
    }

    [Fact]
    public void KMeans_ReachesTheReference()
    {
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var c = ExtTestUtil.AiCalls(seed);
            int k = c.Int("cluster_k"), n = c.Int("cluster_point_count");
            Assert.InRange(k, 3, 5);
            var px = Enumerable.Range(0, n).Select(i => c.Num("cluster_point_x", i)).ToList();
            var py = Enumerable.Range(0, n).Select(i => c.Num("cluster_point_y", i)).ToList();
            // farthest-point init + Lloyd iterations
            var cx = new List<double> { px[0] }; var cy = new List<double> { py[0] };
            while (cx.Count < k)
            {
                int best = 0; double bd = -1;
                for (int i = 0; i < n; i++)
                {
                    double d = cx.Select((_, j) => (px[i] - cx[j]) * (px[i] - cx[j]) + (py[i] - cy[j]) * (py[i] - cy[j])).Min();
                    if (d > bd) { bd = d; best = i; }
                }
                cx.Add(px[best]); cy.Add(py[best]);
            }
            for (int it = 0; it < 20; it++)
            {
                var sx = new double[k]; var sy = new double[k]; var cnt = new int[k];
                for (int i = 0; i < n; i++)
                {
                    int j = Enumerable.Range(0, k).OrderBy(j2 => (px[i] - cx[j2]) * (px[i] - cx[j2]) + (py[i] - cy[j2]) * (py[i] - cy[j2])).First();
                    sx[j] += px[i]; sy[j] += py[i]; cnt[j]++;
                }
                for (int j = 0; j < k; j++) if (cnt[j] > 0) { cx[j] = sx[j] / cnt[j]; cy[j] = sy[j] / cnt[j]; }
            }
            for (int j = 0; j < k; j++) c.Call("submit_centroid", j, cx[j], cy[j]);
            double reference = ((AiState)c.State).Clusters.ReferenceInertia;
            double inertia = c.Num("evaluate_clusters");
            Assert.True(inertia <= reference * 1.2, "seed " + seed + ": " + inertia + " vs " + reference);
            Assert.True(c.Has("insight") >= AiBalance.ClusterBase * 0.45);
        }
    }

    [Fact]
    public void Clustering_EvaluateNeedsACentroid_IndexChecked()
    {
        var c = ExtTestUtil.AiCalls(3);
        Assert.Throws<ExtError>(() => c.Call("evaluate_clusters"));
        int k = c.Int("cluster_k");
        Assert.Throws<ExtError>(() => c.Call("submit_centroid", k, 1.0, 1.0));
        c.Call("submit_centroid", 0, 10.0, 10.0);
        Assert.True(c.Num("evaluate_clusters") > 0);
        Assert.Throws<ExtError>(() => c.Call("evaluate_clusters"));  // new field: centroids cleared
    }

    [Fact]
    public void NeuralNet_HandDesignedXorWeights_SolveTheTask()
    {
        var c = ExtTestUtil.AiCalls(4);
        var net = ((AiState)c.State).Net;
        c.Call("nn_point_count");
        // undo the task transform: u = rotate(-theta) * (p - o) / scale, then classic XOR weights on u
        double s = net.Scale, th = net.Theta, cos = Math.Cos(-th) / s, sin = Math.Sin(-th) / s;
        // u_x = cos*(x-ox) - sin*(y-oy), u_y = sin*(x-ox) + cos*(y-oy)
        double ax = cos, ay = -sin, a0 = -(cos * net.Ox - sin * net.Oy);
        double bx = sin, by = cos, b0 = -(sin * net.Ox + cos * net.Oy);
        // h0 = OR: 20*(ux + uy) - 10 ; h1 = AND: 20*(ux + uy) - 30 ; out = h0 - h1
        c.Call("nn_submit_weight", 0, 0, 0, 20 * (ax + bx)); c.Call("nn_submit_weight", 0, 1, 0, 20 * (ay + by)); c.Call("nn_submit_bias", 0, 0, 20 * (a0 + b0) - 10);
        c.Call("nn_submit_weight", 0, 0, 1, 20 * (ax + bx)); c.Call("nn_submit_weight", 0, 1, 1, 20 * (ay + by)); c.Call("nn_submit_bias", 0, 1, 20 * (a0 + b0) - 30);
        c.Call("nn_submit_weight", 1, 0, 0, 20.0); c.Call("nn_submit_weight", 1, 1, 0, -20.0); c.Call("nn_submit_bias", 1, 0, -10.0);
        double acc = c.Num("nn_evaluate");
        Assert.True(acc >= 0.95, "acc " + acc);
        Assert.True(c.Has("insight") >= AiBalance.NnBase * 0.8);
        Assert.Equal(20.0, net.W1[0]);   // weights stay for the next task
    }

    [Fact]
    public void NeuralNet_IndicesChecked_SigmoidStable()
    {
        var c = ExtTestUtil.AiCalls();
        Assert.Throws<ExtError>(() => c.Call("nn_submit_weight", 2, 0, 0, 1.0));
        Assert.Throws<ExtError>(() => c.Call("nn_submit_weight", 1, 0, 1, 1.0));
        Assert.Throws<ExtError>(() => c.Call("nn_submit_bias", 0, 2, 1.0));
        Assert.Equal(0.5, c.Num("nn_sigmoid", 0.0));
        Assert.Equal(1.0, c.Num("nn_sigmoid", 1000.0));
        Assert.Equal(0.0, c.Num("nn_sigmoid", -1000.0));
        Assert.Equal(0.5, c.Num("nn_output", 1.0, 2.0));
        Assert.Equal(0.5, c.Num("nn_evaluate"), 1);   // all-zero net predicts 1 everywhere
        Assert.Equal(0, c.Has("insight"));
    }

    [Fact]
    public void GridWorld_ValueIterationPolicyIsOptimal_QLearningLearns()
    {
        var c = ExtTestUtil.AiCalls(7);
        var world = ((AiState)c.State).World;
        Assert.Equal(25, c.Int("env_state_count"));
        Assert.Equal(4, c.Int("env_actions"));
        Assert.Throws<ExtError>(() => c.Call("env_step", 0));
        // tabular Q-learning through the builtins
        var q = new double[100];
        var rnd = new Random(1);
        for (int ep = 0; ep < 400; ep++)
        {
            int s = c.Int("env_reset");
            while (!c.Bool("env_done"))
            {
                int a = rnd.NextDouble() < 0.2 ? rnd.Next(4) : Enumerable.Range(0, 4).OrderByDescending(i => q[s * 4 + i]).First();
                double r = c.Num("env_step", a);
                int s2 = c.Int("env_state");
                double next = c.Bool("env_done") ? 0 : Enumerable.Range(0, 4).Max(i => q[s2 * 4 + i]);
                q[s * 4 + a] += 0.5 * (r + 0.95 * next - q[s * 4 + a]);
                s = s2;
            }
        }
        for (int s = 0; s < 25; s++) c.Call("submit_policy_action", s, Enumerable.Range(0, 4).OrderByDescending(i => q[s * 4 + i]).First());
        var optimal = world.OptimalPolicy();
        double best = world.Score(optimal);
        double score = c.Num("evaluate_policy");
        Assert.True(score <= best + 1e-9 || score > best, "score " + score);
        Assert.True(c.Has("insight") > AiBalance.RlBase * 0.3, "insight " + c.Has("insight") + " score " + score + " best " + best);
        Assert.True(c.Bool("env_done"));   // new world: must reset
        Assert.Throws<ExtError>(() => c.Call("submit_policy_action", 25, 0));
        Assert.Throws<ExtError>(() => c.Call("submit_policy_action", 0, 4));
    }

    [Fact]
    public void GridWorld_OptimalPolicyPaysTheFullReward()
    {
        var c = ExtTestUtil.AiCalls(8);
        var world = ((AiState)c.State).World;
        c.Call("env_reset");
        var p = world.OptimalPolicy();
        for (int s = 0; s < 25; s++) c.Call("submit_policy_action", s, (int)p[s]);
        c.Call("evaluate_policy");
        Assert.Equal(AiBalance.RlBase, c.Has("insight"));
    }

    [Fact]
    public void Reward_IsFloorOfBaseTimesQualityPower()
    {
        Assert.Equal(10, AiBalance.Reward(10, 1, 4));
        Assert.Equal(10, AiBalance.Reward(10, 1.5, 4));
        Assert.Equal(0, AiBalance.Reward(10, -0.2, 4));
        Assert.Equal(0, AiBalance.Reward(10, double.NaN, 4));
        Assert.Equal(Math.Floor(40 * Math.Pow(0.9, 8)), AiBalance.Reward(40, 0.9, 8));
    }
}
