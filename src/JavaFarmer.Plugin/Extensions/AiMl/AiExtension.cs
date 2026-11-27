// The AI / ML extension (docs/EXTENSIONS.md §4): currency Insight, root "machine_learning" under vanilla "utilities".
using System;
using System.Collections.Generic;

namespace JavaFarmer.Plugin.Extensions.AiMl
{
    public sealed class AiState : ExtState
    {
        public RegressionData Regression = new RegressionData();
        public ClassificationData Classification = new ClassificationData();
        public ClusterData Clusters = new ClusterData();
        public NeuralNet Net = new NeuralNet();
        public GridWorld World = new GridWorld();

        public override Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            ["version"] = 1.0, ["seed"] = Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["regression"] = Regression.ToJson(), ["classification"] = Classification.ToJson(), ["clustering"] = Clusters.ToJson(),
            ["neural_network"] = Net.ToJson(), ["gridworld"] = World.ToJson(),
        };

        public override void FromJson(Dictionary<string, object> o)
        {
            Seed = ExtJson.ULong(o, "seed", Seed);
            Regression = new RegressionData(); Regression.FromJson(Obj(o, "regression"));
            Classification = new ClassificationData(); Classification.FromJson(Obj(o, "classification"));
            Clusters = new ClusterData(); Clusters.FromJson(Obj(o, "clustering"));
            Net = new NeuralNet(); Net.FromJson(Obj(o, "neural_network"));
            World = new GridWorld(); World.FromJson(Obj(o, "gridworld"));
        }
    }

    public sealed class AiExtension : ContentExtension
    {
        public const string ExtId = "ai_ml", Currency = "insight";
        public override string Id => ExtId;
        public override string DisplayName => "AI / ML";
        public override string Description => "Train models on farm data (regression, classifiers, k-means, neural nets, Q-learning) to earn Insight.";

        readonly List<ItemDef> items;
        readonly List<UnlockDef> unlocks;
        readonly List<BuiltinDef> builtins;
        public override IReadOnlyList<ItemDef> Items => items;
        public override IReadOnlyList<UnlockDef> Unlocks => unlocks;
        public override IReadOnlyList<BuiltinDef> Builtins => builtins;
        public override ExtState NewState(ulong seed) => new AiState { Seed = seed };

        static string Page(string name) => "docs/mods/" + ExtId + "/" + name + ".md";

        public AiExtension()
        {
            items = new List<ItemDef>
            {
                new ItemDef { Name = Currency, Priority = 200001, Docs = Page("index"), Icon = "insight.png", FallbackIcon = "weird_substance" },
            };
            unlocks = new List<UnlockDef>
            {
                new UnlockDef { Name = "machine_learning", Parent = "utilities", Order = 10, Docs = Page("machine_learning"), DisplayCode = "sample_y()",
                    Cost = Cost.Of("pumpkin", 3000, "carrot", 3000),
                    Grants = { Currency, "sample_count", "sample_x", "sample_y", "new_dataset", "submit_prediction" } },
                new UnlockDef { Name = "linear_regression", Parent = "machine_learning", Order = 0, Docs = Page("linear_regression"), DisplayCode = "submit_line()",
                    Cost = Cost.Of(Currency, 20000), Grants = { "submit_line" } },
                new UnlockDef { Name = "clustering", Parent = "machine_learning", Order = 1, Docs = Page("clustering"), DisplayCode = "cluster_k()",
                    Cost = Cost.Of(Currency, 25000),
                    Grants = { "cluster_k", "cluster_point_count", "cluster_point_x", "cluster_point_y", "submit_centroid", "evaluate_clusters" } },
                new UnlockDef { Name = "classification", Parent = "linear_regression", Order = 0, Docs = Page("classification"), DisplayCode = "w * x + b",
                    Cost = Cost.Of(Currency, 60000), Grants = { "point_count", "point_x", "point_y", "point_label", "submit_classifier" } },
                new UnlockDef { Name = "reinforcement_learning", Parent = "clustering", Order = 0, Docs = Page("reinforcement_learning"), DisplayCode = "env_step()",
                    Cost = Cost.Of(Currency, 100000),
                    Grants = { "env_reset", "env_state", "env_state_count", "env_actions", "env_step", "env_done", "submit_policy_action", "evaluate_policy" } },
                new UnlockDef { Name = "neural_networks", Parent = "classification", Order = 0, Docs = Page("neural_networks"), DisplayCode = "nn_sigmoid()",
                    Cost = Cost.Of(Currency, 150000),
                    Grants = { "nn_point_count", "nn_point_x", "nn_point_y", "nn_point_label", "nn_sigmoid", "nn_submit_weight", "nn_submit_bias", "nn_output", "nn_evaluate" } },
                new UnlockDef { Name = "smart_farm", Parent = "neural_networks", Order = 0, Docs = Page("smart_farm"), DisplayCode = "speed * 1.05",
                    MaxLevel = 5, Cost = Cost.Of(Currency, 1000000, "gold", 5000), LevelCosts = { Cost.Of(Currency, 1500000, "gold", 7500) }, LevelFactor = 1.5f,
                    BonusPerLevel = 0.05, BonusKey = "ext_bonus_growth" },
            };
            builtins = BuildBuiltins();
            Link();
        }

        static AiState St(ExtCall c) => c.StateAs<AiState>();
        static RegressionData Reg(ExtCall c) { var s = St(c); s.Regression.Ensure(s.Seed); return s.Regression; }
        static ClassificationData Cls(ExtCall c) { var s = St(c); s.Classification.Ensure(s.Seed); return s.Classification; }
        static ClusterData Clu(ExtCall c) { var s = St(c); s.Clusters.Ensure(s.Seed); return s.Clusters; }
        static NeuralNet Net(ExtCall c) { var s = St(c); s.Net.Ensure(s.Seed); return s.Net; }
        static GridWorld World(ExtCall c) { var s = St(c); s.World.Ensure(s.Seed); return s.World; }

        static int Index(ExtCall c, int arg, int count, string what)
        {
            double v = c.Num(arg);
            if (v < 0 || v >= count) throw c.Error((arg == 0 ? "" : "argument " + (arg + 1) + ": ") + Cyber.CyberExtension.Fmt(v) + " is out of range (" + what + ": 0 to " + (count - 1) + ")");
            return (int)v;
        }

        static object Grant(ExtCall c, double reward, double result)
        {
            if (reward > 0) c.Grant(Currency, reward);
            return result;
        }

        List<BuiltinDef> BuildBuiltins()
        {
            const double A = 200;
            return new List<BuiltinDef>
            {
                // --- machine_learning
                Def("sample_count", "", "int", 1, "Number of samples in the current dataset.", c => (double)Reg(c).X.Count),
                Def("sample_x", "i: int", "float", 1, "Feature x of sample i (water given to the plot).", c => { var d = Reg(c); return d.X[Index(c, 0, d.X.Count, "sample index")]; }),
                Def("sample_y", "i: int", "float", 1, "Target y of sample i (the plot's yield).", c => { var d = Reg(c); return d.Y[Index(c, 0, d.Y.Count, "sample index")]; }),
                Def("new_dataset", "", "void", A, "Discards the current dataset and loads the next one.", c => { Reg(c).Next(St(c).Seed); return null; }),
                Def("submit_prediction", "value: float", "float", A, "Predicts value for every sample; returns the mean squared error and loads the next dataset.", c =>
                {
                    double mse = Reg(c).SubmitPrediction(St(c).Seed, c.Num(0), out var reward);
                    return Grant(c, reward, mse);
                }),
                // --- linear_regression
                Def("submit_line", "slope: float, intercept: float", "float", A, "Predicts y = slope * x + intercept; returns R² and loads the next dataset.", c =>
                {
                    double r2 = Reg(c).SubmitLine(St(c).Seed, c.Num(0), c.Num(1), out var reward);
                    return Grant(c, reward, r2);
                }),
                // --- classification
                Def("point_count", "", "int", 1, "Number of labelled training points.", c => (double)Cls(c).X.Count),
                Def("point_x", "i: int", "float", 1, "x of training point i.", c => { var d = Cls(c); return d.X[Index(c, 0, d.X.Count, "point index")]; }),
                Def("point_y", "i: int", "float", 1, "y of training point i.", c => { var d = Cls(c); return d.Y[Index(c, 0, d.Y.Count, "point index")]; }),
                Def("point_label", "i: int", "int", 1, "Label of training point i: 1 = crop, -1 = weed.", c => { var d = Cls(c); return d.Label[Index(c, 0, d.Label.Count, "point index")]; }),
                Def("submit_classifier", "w_x: float, w_y: float, bias: float", "float", A, "Classifies hidden test points with w_x * x + w_y * y + bias >= 0; returns the accuracy and loads the next dataset.", c =>
                {
                    double acc = Cls(c).Submit(St(c).Seed, c.Num(0), c.Num(1), c.Num(2), out var reward);
                    return Grant(c, reward, acc);
                }),
                // --- clustering
                Def("cluster_k", "", "int", 1, "Number of clusters (drones) for the current field.", c => (double)Clu(c).K),
                Def("cluster_point_count", "", "int", 1, "Number of points to cluster.", c => (double)Clu(c).X.Count),
                Def("cluster_point_x", "i: int", "float", 1, "x of point i.", c => { var d = Clu(c); return d.X[Index(c, 0, d.X.Count, "point index")]; }),
                Def("cluster_point_y", "i: int", "float", 1, "y of point i.", c => { var d = Clu(c); return d.Y[Index(c, 0, d.Y.Count, "point index")]; }),
                Def("submit_centroid", "j: int, x: float, y: float", "void", 1, "Places centroid j (0 to cluster_k() - 1) at (x, y).", c =>
                {
                    var d = Clu(c);
                    d.SetCentroid(Index(c, 0, d.K, "centroid index"), c.Num(1), c.Num(2));
                    return null;
                }),
                Def("evaluate_clusters", "", "float", A, "Scores the centroids: returns the inertia (sum of squared distances to the nearest centroid) and loads the next field.", c =>
                {
                    var d = Clu(c);
                    if (!d.AnySubmitted) throw c.Error("no centroid submitted yet: call submit_centroid(j, x, y) first");
                    double inertia = d.Evaluate(St(c).Seed, out var reward);
                    return Grant(c, reward, inertia);
                }),
                // --- reinforcement_learning
                Def("env_reset", "", "int", 1, "Starts a new episode; returns the start state.", c => (double)World(c).Reset()),
                Def("env_state", "", "int", 1, "Current state (y * 5 + x).", c => (double)World(c).State),
                Def("env_state_count", "", "int", 1, "Number of states (25).", c => (double)GridWorld.States),
                Def("env_actions", "", "int", 1, "Number of actions (4: 0 North, 1 East, 2 South, 3 West).", c => (double)GridWorld.Actions),
                Def("env_step", "a: int", "float", 1, "Takes action a; returns the reward of the step.", c =>
                {
                    var g = World(c);
                    int a = Index(c, 0, GridWorld.Actions, "action");
                    if (g.Done) throw c.Error("the episode is over: call env_reset() to start a new one");
                    return g.DoStep(a);
                }),
                Def("env_done", "", "bool", 1, "True if the episode is over.", c => World(c).Done),
                Def("submit_policy_action", "state: int, a: int", "void", 1, "Sets the action of the policy in a state.", c =>
                {
                    var g = World(c);
                    int s = Index(c, 0, GridWorld.States, "state");
                    int a = Index(c, 1, GridWorld.Actions, "action");
                    g.Policy[s] = a;
                    return null;
                }),
                Def("evaluate_policy", "", "float", A, "Scores the policy: returns its average return and starts a new world.", c =>
                {
                    double score = World(c).Evaluate(St(c).Seed, out var reward);
                    return Grant(c, reward, score);
                }),
                // --- neural_networks
                Def("nn_point_count", "", "int", 1, "Number of training points of the XOR task.", c => (double)Net(c).X.Count),
                Def("nn_point_x", "i: int", "float", 1, "x of training point i.", c => { var n = Net(c); return n.X[Index(c, 0, n.X.Count, "point index")]; }),
                Def("nn_point_y", "i: int", "float", 1, "y of training point i.", c => { var n = Net(c); return n.Y[Index(c, 0, n.Y.Count, "point index")]; }),
                Def("nn_point_label", "i: int", "int", 1, "Label (0 or 1) of training point i.", c => { var n = Net(c); return n.Label[Index(c, 0, n.Label.Count, "point index")]; }),
                Def("nn_sigmoid", "x: float", "float", 1, "The logistic function 1 / (1 + e^-x).", c => NeuralNet.Sigmoid(c.Num(0)), pure: true),
                Def("nn_submit_weight", "layer: int, i: int, j: int, w: float", "void", 1, "Sets a weight: layer 0 = input i (0 = x, 1 = y) to hidden j, layer 1 = hidden i to the output (j = 0).", c =>
                {
                    var n = Net(c);
                    int layer = Index(c, 0, 2, "layer");
                    int i = Index(c, 1, 2, "i");
                    int j = Index(c, 2, layer == 0 ? 2 : 1, "j");
                    if (layer == 0) n.W0[i * 2 + j] = c.Num(3); else n.W1[i] = c.Num(3);
                    return null;
                }),
                Def("nn_submit_bias", "layer: int, j: int, b: float", "void", 1, "Sets a bias: layer 0 = hidden unit j, layer 1 = the output (j = 0).", c =>
                {
                    var n = Net(c);
                    int layer = Index(c, 0, 2, "layer");
                    int j = Index(c, 1, layer == 0 ? 2 : 1, "j");
                    if (layer == 0) n.B0[j] = c.Num(2); else n.B1 = c.Num(2);
                    return null;
                }),
                Def("nn_output", "x: float, y: float", "float", 5, "Output (0 to 1) of the network for the input (x, y).", c => Net(c).Output(c.Num(0), c.Num(1))),
                Def("nn_evaluate", "", "float", A, "Scores the network on hidden test points: returns the accuracy and loads the next task (the weights stay).", c =>
                {
                    double acc = Net(c).Evaluate(St(c).Seed, out var reward);
                    return Grant(c, reward, acc);
                }),
            };
        }
    }
}
