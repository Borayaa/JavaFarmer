// Cybersecurity puzzles (docs/EXTENSIONS.md §3). Pure logic: no game types. Each puzzle is regenerated
// deterministically from (save seed, kind, counter, level); only counters and progress are saved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace JavaFarmer.Plugin.Extensions.Cyber
{
    /// <summary>Balance knobs of the Cybersecurity extension (all rewards in Bits).</summary>
    public static class CyberBalance
    {
        public const double VaultTryTicks = 200, CheckDigitTicks = 25;
        /// <summary>Bits for cracking a PIN of length L: 5 * L^2 (2 digits: 20, 3: 45, 4: 80).</summary>
        public static double VaultReward(int pinLength) => 5.0 * pinLength * pinLength;
        public static int PinLength(int level) => 1 + Math.Max(1, Math.Min(3, level));

        public static readonly double[] CryptoReward = { 0, 1000, 3000, 15000 };
        public static readonly double[] HashReward = { 0, 40, 400 };
        public static readonly double[] IdsReward = { 0, 150, 200 }, IdsPenalty = { 0, 100, 150 };
        public static readonly double[] FirewallMultiplier = { 0, 3, 5, 8 };
        public const double FwAllowOk = 3, FwDenyOk = 2, FwAllowBad = -6, FwDenyBad = -3;
        public const int FloodLimit = 8, MaxPacketSize = 1500;
    }

    // ---------------------------------------------------------------- vault (brute force / side channel)

    public sealed class Vault
    {
        public long Counter;
        public int Level;
        public long Cracked;
        int pin = -1;

        public int PinLength => CyberBalance.PinLength(Level);
        /// <summary>Current PIN (for tests).</summary>
        public int Pin => pin;

        public void Ensure(ulong seed, int level)
        {
            level = Math.Max(1, Math.Min(3, level));
            if (pin >= 0 && level == Level) return;
            if (pin >= 0) Counter++;
            Level = level;
            Generate(seed);
        }

        void Generate(ulong seed)
        {
            var r = ExtRandom.For(seed, "vault", Counter);
            pin = r.Next((int)Math.Pow(10, PinLength));
        }

        /// <summary>Digit i (0 = leftmost) of the PIN, written with exactly PinLength digits.</summary>
        public int Digit(int i) => (int)(pin / Math.Pow(10, PinLength - 1 - i)) % 10;

        /// <summary>True + reward when guess is the PIN (a new PIN is then set).</summary>
        public bool Try(ulong seed, double guess, out double reward)
        {
            reward = 0;
            if (guess != Math.Floor(guess) || guess != pin) return false;
            reward = CyberBalance.VaultReward(PinLength);
            Cracked++;
            Counter++;
            Generate(seed);
            return true;
        }

        public Dictionary<string, object> ToJson() => new Dictionary<string, object> { ["counter"] = (double)Counter, ["level"] = (double)Level, ["cracked"] = (double)Cracked };
        public void FromJson(Dictionary<string, object> o)
        {
            Counter = (long)ExtJson.Num(o, "counter"); Level = ExtJson.Int(o, "level"); Cracked = (long)ExtJson.Num(o, "cracked"); pin = -1;
            if (Level < 1) Level = 0;
        }
        /// <summary>After FromJson: regenerate the PIN of the saved counter/level.</summary>
        public void Restore(ulong seed) { if (Level >= 1) Generate(seed); }
    }

    // ---------------------------------------------------------------- cryptography (Caesar / Vigenere / frequency analysis)

    public static class CryptoText
    {
        /// <summary>English letter frequencies in percent, A..Z (the table printed on the Cryptography page).</summary>
        public static readonly double[] English = { 8.2, 1.5, 2.8, 4.3, 12.7, 2.2, 2.0, 6.1, 7.0, 0.15, 0.8, 4.0, 2.4, 6.7, 7.5, 1.9, 0.1, 6.0, 6.3, 9.1, 2.8, 1.0, 2.4, 0.15, 2.0, 0.07 };

        public static readonly string[] Words = (
            "THE AND TO OF A IN IS ON AT BY FOR WITH FROM THEN WHEN ALL EVERY EACH NEXT FIRST SECOND LAST " +
            "PLANT HARVEST WATER TILL GROW SEND MOVE CHECK FIND COLLECT PROTECT KEEP OPEN CLOSE FEED COUNT DIG WAIT MEET RETURN " +
            "CARROTS PUMPKINS SUNFLOWERS TREES BUSHES GRASS CACTUS HAY WOOD GOLD SEEDS SOIL FIELD BARN FENCE GATE HEDGE MAZE " +
            "TREASURE RIVER HILL GARDEN ORCHARD MEADOW DRONE DRONES PIGGY CROPS ROW ROWS NORTH SOUTH EAST WEST CORNER CENTER " +
            "DAWN NOON DUSK NIGHT TONIGHT TOMORROW BEFORE AFTER RAIN STORM SUN SECRET MESSAGE SIGNAL NEW OLD GREEN TALL RIPE SWEET " +
            "FAST SLOW DEEP NEAR UNDER BEHIND THERE HERE THIS THAT OUR YOUR TWO THREE FOUR SEVEN TEN HUNDRED SAFE READY " +
            "FARMER NEEDS MORE LESS EXTRA SOON NOW").Split(' ');

        public static string Sentence(ExtRandom r, int minLetters)
        {
            var sb = new StringBuilder();
            int letters = 0;
            while (letters < minLetters)
            {
                var w = Words[r.Next(Words.Length)];
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(w);
                letters += w.Length;
            }
            return sb.ToString();
        }

        /// <summary>Vigenere: letter j of the text (spaces are kept and do not advance the key) is shifted by key[j % len].</summary>
        public static string Encrypt(string plain, string key, int sign = 1)
        {
            var sb = new StringBuilder(plain.Length);
            int j = 0;
            foreach (char c in plain)
            {
                if (c < 'A' || c > 'Z') { sb.Append(c); continue; }
                int k = key[j % key.Length] - 'A';
                sb.Append((char)('A' + ((c - 'A' + sign * k) % 26 + 26) % 26));
                j++;
            }
            return sb.ToString();
        }

        public static string Decrypt(string cipher, string key) => Encrypt(cipher, key, -1);

        /// <summary>Reference solver (frequency analysis with chi-squared per key column). Used to make sure every message is solvable.</summary>
        public static string SolveKey(string cipher, int keyLength)
        {
            var key = new StringBuilder();
            var letters = cipher.Where(c => c >= 'A' && c <= 'Z').ToArray();
            for (int col = 0; col < keyLength; col++)
            {
                var counts = new double[26];
                int n = 0;
                for (int j = col; j < letters.Length; j += keyLength) { counts[letters[j] - 'A']++; n++; }
                int best = 0;
                double bestScore = double.MaxValue;
                for (int shift = 0; shift < 26; shift++)
                {
                    double chi = 0;
                    for (int l = 0; l < 26; l++)
                    {
                        double expected = English[l] / 100.0 * n;
                        double observed = counts[(l + shift) % 26];
                        chi += (observed - expected) * (observed - expected) / expected;
                    }
                    if (chi < bestScore) { bestScore = chi; best = shift; }
                }
                key.Append((char)('A' + best));
            }
            return key.ToString();
        }
    }

    public sealed class Crypto
    {
        public long Counter;
        public int Level;
        public long Solved;
        public string Plain { get; private set; }
        public string Cipher { get; private set; }
        public string Key { get; private set; }

        public void Ensure(ulong seed, int level)
        {
            level = Math.Max(1, Math.Min(3, level));
            if (Plain != null && level == Level) return;
            if (Plain != null) Counter++;
            Level = level;
            Generate(seed);
        }

        void Generate(ulong seed)
        {
            for (int attempt = 0; ; attempt++)
            {
                var r = ExtRandom.For(seed, "crypto" + attempt, Counter);
                int keyLen = Level == 1 ? 1 : Level == 2 ? r.Range(2, 3) : r.Range(4, 6);
                int minLetters = Level == 1 ? 40 : Level == 2 ? 90 : 230;
                var key = new StringBuilder();
                for (int i = 0; i < keyLen; i++) key.Append((char)('A' + r.Range(1, 25)));
                Plain = CryptoText.Sentence(r, minLetters);
                Key = key.ToString();
                Cipher = CryptoText.Encrypt(Plain, Key);
                // levels 2 and 3 must be solvable by the frequency analysis taught on the page
                if (Level == 1 || CryptoText.SolveKey(Cipher, keyLen) == Key || attempt >= 30) return;
            }
        }

        public bool Submit(ulong seed, string text, out double reward)
        {
            reward = 0;
            if (text == null || text.Trim().ToUpperInvariant() != Plain) return false;
            reward = CyberBalance.CryptoReward[Level];
            Solved++;
            Counter++;
            Generate(seed);
            return true;
        }

        public Dictionary<string, object> ToJson() => new Dictionary<string, object> { ["counter"] = (double)Counter, ["level"] = (double)Level, ["solved"] = (double)Solved };
        public void FromJson(Dictionary<string, object> o) { Counter = (long)ExtJson.Num(o, "counter"); Level = ExtJson.Int(o, "level"); Solved = (long)ExtJson.Num(o, "solved"); Plain = null; }
        public void Restore(ulong seed) { if (Level >= 1) Generate(seed); }
    }

    // ---------------------------------------------------------------- hashing (dictionary attack, salts)

    public static class HashText
    {
        public const long Modulus = 1000000007;

        /// <summary>h = 0; for each character c: h = (h * 31 + char_code(c)) % 1000000007</summary>
        public static double Hash(string s)
        {
            long h = 0;
            foreach (char c in s ?? "") h = (h * 31 + c) % Modulus;
            return h;
        }

        public static readonly string[] Wordlist = (
            "password dragon sunshine letmein monkey shadow master qwerty football baseball welcome princess flower hunter " +
            "freedom whatever trustno starwars cheese computer coffee summer winter spring autumn banana orange apple purple " +
            "silver golden tractor harvest carrot pumpkin cactus sunflower bush tree hay grass soil water fertilizer maze " +
            "treasure dinosaur bone drone piggy farmer barn field seed sprout garden meadow orchard valley river forest " +
            "mountain thunder lightning rainbow cookie butter honey pepper ginger mango lemon cherry peach melon grape kiwi " +
            "tomato potato onion garlic wheat barley corn rice bean pea turnip radish cabbage lettuce spinach celery parsley " +
            "basil mint rose tulip daisy lily violet orchid clover acorn maple willow oak pine cedar birch falcon eagle raven " +
            "robin sparrow badger rabbit beaver otter turtle lizard spider beetle cricket butterfly goat sheep horse donkey " +
            "cow chicken duck goose pony kitten puppy tiger lion bear wolf fox panda koala zebra giraffe hippo rhino camel " +
            "llama alpaca pirate ninja wizard knight castle dragonfly rocket planet galaxy comet meteor nebula orbit laser " +
            "robot cyborg matrix hacker secret hidden shield access admin root login guest default changeme iloveyou trustme " +
            "superman batman pokemon minecraft diamond emerald ruby sapphire crystal magic mystery puzzle riddle banjo guitar " +
            "piano violin drums jazz blues disco tango").Split(' ');
    }

    public sealed class Hashing
    {
        public const int Entries = 8;
        public long Counter;
        public int Level;
        public long CrackedTotal;
        public List<bool> Cracked = new List<bool>();
        public List<double> Hashes { get; private set; }
        public List<string> Salts { get; private set; }
        public List<string> Passwords { get; private set; }

        public void Ensure(ulong seed, int level)
        {
            level = Math.Max(1, Math.Min(2, level));
            if (Hashes != null && level == Level) return;
            if (Hashes != null) { Counter++; Cracked.Clear(); }
            Level = level;
            Generate(seed);
        }

        void Generate(ulong seed)
        {
            var r = ExtRandom.For(seed, "hash", Counter);
            Hashes = new List<double>(); Salts = new List<string>(); Passwords = new List<string>();
            // popular passwords are more common (weight 1 / (rank + 4))
            var words = HashText.Wordlist;
            double total = 0;
            for (int i = 0; i < words.Length; i++) total += 1.0 / (i + 4);
            for (int e = 0; e < Entries; e++)
            {
                double x = r.NextDouble() * total;
                int pick = 0;
                for (; pick < words.Length - 1; pick++) { x -= 1.0 / (pick + 4); if (x <= 0) break; }
                string salt = "";
                if (Level >= 2) for (int k = 0; k < 4; k++) salt += (char)('a' + r.Next(26));
                Passwords.Add(words[pick]);
                Salts.Add(salt);
                Hashes.Add(HashText.Hash(salt + words[pick]));
            }
            while (Cracked.Count < Entries) Cracked.Add(false);
            if (Cracked.Count > Entries) Cracked.RemoveRange(Entries, Cracked.Count - Entries);
        }

        public bool Submit(ulong seed, int i, string password, out double reward)
        {
            reward = 0;
            if (Cracked[i] || HashText.Hash(Salts[i] + (password ?? "")) != Hashes[i]) return false;
            Cracked[i] = true;
            CrackedTotal++;
            reward = CyberBalance.HashReward[Level];
            if (Cracked.All(c => c))
            {
                Counter++;
                Cracked.Clear();
                Generate(seed);
            }
            return true;
        }

        public Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            ["counter"] = (double)Counter, ["level"] = (double)Level, ["total"] = (double)CrackedTotal, ["cracked"] = Cracked.Cast<object>().ToList(),
        };
        public void FromJson(Dictionary<string, object> o)
        {
            Counter = (long)ExtJson.Num(o, "counter"); Level = ExtJson.Int(o, "level"); CrackedTotal = (long)ExtJson.Num(o, "total");
            Cracked = ExtJson.Bools(o, "cracked"); Hashes = null;
        }
        public void Restore(ulong seed) { if (Level >= 1) Generate(seed); }
    }

    // ---------------------------------------------------------------- intrusion detection (log analysis)

    public sealed class LogEntry
    {
        public string Ip, User;
        public bool Success;
    }

    public sealed class Ids
    {
        public long Counter;
        public int Level;
        public long Caught, FalseAlarms;
        public List<string> Blocked = new List<string>();
        public List<LogEntry> Log { get; private set; }
        public HashSet<string> Attackers { get; private set; }

        static readonly string[] Users =
            "alice bob carol dave erin frank grace heidi ivan judy kevin laura mike nina oscar peggy quinn rita sam trent ursula victor wendy yusuf zoe".Split(' ');

        public void Ensure(ulong seed, int level)
        {
            level = Math.Max(1, Math.Min(2, level));
            if (Log != null && level == Level) return;
            if (Log != null) { Counter++; Blocked.Clear(); }
            Level = level;
            Generate(seed);
        }

        public void Next(ulong seed)
        {
            Counter++;
            Blocked.Clear();
            Generate(seed);
        }

        static string PublicIp(ExtRandom r, HashSet<string> used)
        {
            string[] prefixes = { "203.0.113.", "198.51.100.", "192.0.2.", "45.33.", "81.2.", "91.198.", "62.210.", "185.12." };
            while (true)
            {
                var p = prefixes[r.Next(prefixes.Length)];
                var ip = p.Count(c => c == '.') == 3 ? p + r.Range(2, 254) : p + r.Range(0, 255) + "." + r.Range(2, 254);
                if (used.Add(ip)) return ip;
            }
        }

        void Generate(ulong seed)
        {
            var r = ExtRandom.For(seed, "ids", Counter);
            var used = new HashSet<string>();
            var actors = new List<List<LogEntry>>();
            Attackers = new HashSet<string>();
            var users = Users.ToList();
            r.Shuffle(users);
            int legit = Level == 1 ? r.Range(6, 8) : r.Range(8, 10);
            var legitUsers = users.Take(legit).ToList();
            // legit users: own IP, 2..5 logins, a typo now and then (always followed by a success)
            int maxTypos = Level == 1 ? 2 : 3;
            for (int u = 0; u < legit; u++)
            {
                string ip;
                do ip = r.Chance(0.5) ? "10.0." + r.Range(0, 9) + "." + r.Range(2, 250) : PublicIp(r, used);
                while (!used.Add(ip) && ip.StartsWith("10.", StringComparison.Ordinal));
                var seq = new List<LogEntry>();
                int logins = r.Range(2, 5);
                int budget = r.Chance(0.6) ? r.Range(1, maxTypos) : 0;   // failures this address makes in total
                for (int k = 0; k < logins; k++)
                {
                    int typos = budget > 0 && (k == logins - 1 || r.Chance(0.4)) ? r.Range(1, budget) : 0;
                    budget -= typos;
                    for (int t = 0; t < typos; t++) seq.Add(new LogEntry { Ip = ip, User = legitUsers[u], Success = false });
                    seq.Add(new LogEntry { Ip = ip, User = legitUsers[u], Success = true });
                }
                actors.Add(seq);
            }
            if (Level >= 2)
            {
                // an office behind one NAT address: several legit users share it (many logins, few failures)
                string nat = "10.1.0.1";
                used.Add(nat);
                var seq = new List<LogEntry>();
                var office = users.Skip(legit).Take(4).ToList();
                foreach (var name in office)
                    for (int k = r.Range(2, 4); k > 0; k--) seq.Add(new LogEntry { Ip = nat, User = name, Success = true });
                seq.Insert(r.Next(seq.Count), new LogEntry { Ip = nat, User = office[0], Success = false });
                r.Shuffle(seq);
                actors.Add(seq);
            }
            // attackers
            var targets = new List<string> { "admin", "root" };
            targets.AddRange(legitUsers);
            int attackers = Level == 1 ? 1 : r.Range(2, 3);
            for (int a = 0; a < attackers; a++)
            {
                string ip = PublicIp(r, used);
                Attackers.Add(ip);
                var seq = new List<LogEntry>();
                int kind = Level == 1 ? 0 : a == 0 ? 0 : r.Range(1, 2);
                if (kind == 0)
                {
                    // brute force: many failures against one or two accounts
                    int n = r.Range(Level == 1 ? 12 : 10, 20);
                    string t1 = targets[r.Next(targets.Count)], t2 = targets[r.Next(targets.Count)];
                    for (int k = 0; k < n; k++) seq.Add(new LogEntry { Ip = ip, User = r.Chance(0.7) ? t1 : t2, Success = false });
                    if (r.Chance(0.2)) seq.Add(new LogEntry { Ip = ip, User = t1, Success = true });
                }
                else if (kind == 1)
                {
                    // password spraying: one failed attempt for each of many accounts
                    var all = Users.Concat(new[] { "admin", "root" }).ToList();
                    r.Shuffle(all);
                    int n = r.Range(8, 12);
                    for (int k = 0; k < n; k++) seq.Add(new LogEntry { Ip = ip, User = all[k], Success = false });
                }
                else
                {
                    // slow brute force: a handful of failures, sometimes a success at the end
                    string t = targets[r.Next(targets.Count)];
                    for (int k = r.Range(6, 8); k > 0; k--) seq.Add(new LogEntry { Ip = ip, User = t, Success = false });
                    if (r.Chance(0.3)) seq.Add(new LogEntry { Ip = ip, User = t, Success = true });
                }
                actors.Add(seq);
            }
            // merge the actors' sequences in a random interleaving (each actor keeps its own order)
            Log = new List<LogEntry>();
            var pos = new int[actors.Count];
            int left = actors.Sum(s => s.Count);
            while (left > 0)
            {
                int pick = r.Next(left);
                for (int i = 0; i < actors.Count; i++)
                {
                    int rem = actors[i].Count - pos[i];
                    if (pick < rem) { Log.Add(actors[i][pos[i]++]); break; }
                    pick -= rem;
                }
                left--;
            }
        }

        /// <summary>+1 attacker blocked, -1 legit address blocked (penalty), 0 nothing (unknown or already blocked).</summary>
        public int Block(string ip)
        {
            ip = (ip ?? "").Trim();
            if (Blocked.Contains(ip) || !Log.Any(e => e.Ip == ip)) return 0;
            Blocked.Add(ip);
            if (Attackers.Contains(ip)) { Caught++; return 1; }
            FalseAlarms++;
            return -1;
        }

        public Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            ["counter"] = (double)Counter, ["level"] = (double)Level, ["caught"] = (double)Caught, ["false_alarms"] = (double)FalseAlarms,
            ["blocked"] = Blocked.Cast<object>().ToList(),
        };
        public void FromJson(Dictionary<string, object> o)
        {
            Counter = (long)ExtJson.Num(o, "counter"); Level = ExtJson.Int(o, "level"); Caught = (long)ExtJson.Num(o, "caught");
            FalseAlarms = (long)ExtJson.Num(o, "false_alarms"); Blocked = ExtJson.Strs(o, "blocked"); Log = null;
        }
        public void Restore(ulong seed) { if (Level >= 1) Generate(seed); }
    }

    // ---------------------------------------------------------------- firewall (rule-based, stateful filtering)

    public sealed class Packet
    {
        public int Port, Size;
        public string Src;
        public bool Malicious;
    }

    public sealed class Firewall
    {
        public long Counter;
        public int Level;
        /// <summary>Index of the current packet (-1 = before the first).</summary>
        public int Index = -1;
        /// <summary>Per packet: 0 undecided (= deny), 1 allow, 2 deny.</summary>
        public List<double> Decisions = new List<double>();
        public long Batches;
        public double LastScore;
        public List<Packet> Packets { get; private set; }

        public void Ensure(ulong seed, int level)
        {
            level = Math.Max(1, Math.Min(3, level));
            if (Packets != null && level == Level) return;
            if (Packets != null) { Counter++; Index = -1; Decisions.Clear(); }
            Level = level;
            Generate(seed);
        }

        /// <summary>The farm's network policy for a level (what the page documents). seen = packets of this source before this one.</summary>
        public static bool PolicyDenies(int level, int port, string src, int size, int seen)
        {
            if (level >= 3 && seen >= CyberBalance.FloodLimit) return true;
            if (level >= 2 && size > CyberBalance.MaxPacketSize) return true;
            if (port == 80 || port == 443 || port == 53) return false;
            if (level >= 2 && port == 22 && src.StartsWith("10.", StringComparison.Ordinal)) return false;
            return true;
        }

        void Generate(ulong seed)
        {
            var r = ExtRandom.For(seed, "firewall", Counter);
            int n = Level == 1 ? 30 : Level == 2 ? 40 : 60;
            var internalHosts = new List<string>();
            for (int i = 0; i < 6; i++) internalHosts.Add("10.0." + r.Range(0, 3) + "." + r.Range(2, 250));
            var outside = new List<string>();
            for (int i = 0; i < 10; i++) outside.Add(r.Range(11, 223) + "." + r.Range(0, 255) + "." + r.Range(0, 255) + "." + r.Range(2, 254));
            string flooder = outside[0];
            int[] badPorts = { 21, 23, 25, 135, 139, 445, 1433, 3389, 5900, 6667, 31337 };
            var list = new List<Packet>();
            for (int i = 0; i < n; i++)
            {
                var p = new Packet();
                double roll = r.NextDouble();
                string anySrc = r.Chance(0.4) ? internalHosts[r.Next(internalHosts.Count)] : outside[1 + r.Next(outside.Count - 1)];
                if (Level >= 3 && roll < 0.25) { p.Src = flooder; p.Port = r.Chance(0.5) ? 80 : 443; p.Size = r.Range(40, 600); }
                else if (roll < 0.55) { p.Src = anySrc; p.Port = new[] { 80, 443, 443, 53 }[r.Next(4)]; p.Size = r.Range(40, 1400); }
                else if (Level >= 2 && roll < 0.68) { p.Src = r.Chance(0.6) ? internalHosts[r.Next(internalHosts.Count)] : anySrc; p.Port = 22; p.Size = r.Range(60, 900); }
                else if (Level >= 2 && roll < 0.76) { p.Src = anySrc; p.Port = new[] { 80, 443, 53 }[r.Next(3)]; p.Size = r.Range(1501, 9000); }
                else { p.Src = anySrc; p.Port = r.Chance(0.8) ? badPorts[r.Next(badPorts.Length)] : r.Range(1024, 65535); p.Size = r.Range(40, 1400); }
                list.Add(p);
            }
            var seen = new Dictionary<string, int>();
            foreach (var p in list)
            {
                seen.TryGetValue(p.Src, out var k);
                p.Malicious = PolicyDenies(Level, p.Port, p.Src, p.Size, k);
                seen[p.Src] = k + 1;
            }
            Packets = list;
        }

        public Packet Current => Index >= 0 && Index < Packets.Count ? Packets[Index] : null;

        /// <summary>Moves to the next packet. At the end of the batch: scores it (reward), starts a new batch, returns false.</summary>
        public bool Next(ulong seed, out double reward)
        {
            reward = 0;
            if (Index + 1 < Packets.Count)
            {
                Index++;
                while (Decisions.Count <= Index) Decisions.Add(0);
                return true;
            }
            reward = Score();
            LastScore = reward;
            Batches++;
            Counter++;
            Index = -1;
            Decisions.Clear();
            Generate(seed);
            return false;
        }

        /// <summary>Reward of the current batch with the decisions made so far (undecided = denied).</summary>
        public double Score()
        {
            double s = 0;
            for (int i = 0; i < Packets.Count; i++)
            {
                bool allowed = i < Decisions.Count && Decisions[i] == 1;
                var p = Packets[i];
                s += allowed ? (p.Malicious ? CyberBalance.FwAllowBad : CyberBalance.FwAllowOk) : (p.Malicious ? CyberBalance.FwDenyOk : CyberBalance.FwDenyBad);
            }
            return Math.Floor(Math.Max(0, s) * CyberBalance.FirewallMultiplier[Level]);
        }

        public void Decide(bool allow)
        {
            while (Decisions.Count <= Index) Decisions.Add(0);
            Decisions[Index] = allow ? 1 : 2;
        }

        public Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            ["counter"] = (double)Counter, ["level"] = (double)Level, ["index"] = (double)Index, ["batches"] = (double)Batches,
            ["last_score"] = LastScore, ["decisions"] = Decisions.Cast<object>().ToList(),
        };
        public void FromJson(Dictionary<string, object> o)
        {
            Counter = (long)ExtJson.Num(o, "counter"); Level = ExtJson.Int(o, "level"); Index = ExtJson.Int(o, "index", -1);
            Batches = (long)ExtJson.Num(o, "batches"); LastScore = ExtJson.Num(o, "last_score"); Decisions = ExtJson.Nums(o, "decisions"); Packets = null;
        }
        public void Restore(ulong seed)
        {
            if (Level < 1) return;
            Generate(seed);
            if (Index >= Packets.Count) Index = -1;
        }
    }
}
