using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace JavaFarmer.Plugin
{
    /// <summary>How a game AST node produced from Java pays for unlock dependencies (SPEC §7).</summary>
    public sealed class NodePolicy
    {
        /// <summary>The node checks no dependency at all.</summary>
        public bool Skip;
        /// <summary>When non-null, the node checks this dependency instead of its own.</summary>
        public string Override;
        public bool IsCall;
        public override string ToString() => Override != null ? "override:" + Override : (Skip ? "skip" : "user");
    }

    /// <summary>
    /// Per-node dependency policies, keyed by the node's BoxedNodeParams (DeepCopy shares the params object,
    /// so drone copies of a tree inherit the policy).
    /// </summary>
    public static class DependencyPolicies
    {
        private static readonly ConditionalWeakTable<BoxedNodeParams, NodePolicy> table = new ConditionalWeakTable<BoxedNodeParams, NodePolicy>();

        /// <summary>Dependency names that come from CheckDependencies (never from a CallNode).</summary>
        private static readonly HashSet<string> structuralDeps = new HashSet<string>
        {
            "variables", "operators", "if", "while", "functions", "dicts", "for", "import", "lists", "sets"
        };

        public static void Set(BoxedNodeParams p, NodePolicy policy)
        {
            if (p == null) return;
            table.Remove(p);
            if (policy != null) table.Add(p, policy);
        }

        public static NodePolicy Get(BoxedNodeParams p)
        {
            if (p == null) return null;
            return table.TryGetValue(p, out var v) ? v : null;
        }

        /// <summary>
        /// Called before a node's CheckDependencies. Returns true to run the game's own check.
        /// </summary>
        public static bool BeforeCheck(BoxedNodeParams p, List<(string, int, int)> deps)
        {
            var policy = Get(p);
            if (policy == null) return true;
            if (policy.Override != null)
            {
                deps?.Add((policy.Override, p.wordStart, p.wordEnd));
                return false;
            }
            return !policy.Skip;
        }

        /// <summary>
        /// CallNode adds its dependency (the called builtin's name) inside Execute, not in CheckDependencies.
        /// Wrap its enumerator so the entry it adds is removed (Skip) or renamed (Override).
        /// </summary>
        public static IEnumerable<double> WrapCall(IEnumerable<double> inner, BoxedNodeParams p, Func<List<(string, int, int)>> deps, NodePolicy policy)
        {
            if (inner == null) yield break;
            using (var e = inner.GetEnumerator())
            {
                bool fixedUp = false;
                while (true)
                {
                    var list = deps();
                    int before = list?.Count ?? 0;
                    bool has;
                    try
                    {
                        has = e.MoveNext();
                    }
                    finally
                    {
                        if (!fixedUp) fixedUp = FixUp(deps(), before, p, policy);
                    }
                    if (!has) yield break;
                    yield return e.Current;
                }
            }
        }

        /// <summary>Finds the call's own entry (last entry with the node's span) added since 'before' and applies the policy.</summary>
        public static bool FixUp(List<(string, int, int)> list, int before, BoxedNodeParams p, NodePolicy policy)
        {
            if (list == null || p == null || policy == null) return false;
            if (before > list.Count) before = list.Count;
            for (int i = list.Count - 1; i >= before; i--)
            {
                var (name, s, e) = list[i];
                if (s != p.wordStart || e != p.wordEnd || name == null || structuralDeps.Contains(name)) continue;
                if (policy.Override != null) list[i] = (policy.Override, s, e);
                else if (policy.Skip) list.RemoveAt(i);
                return true;
            }
            return false;
        }
    }
}
