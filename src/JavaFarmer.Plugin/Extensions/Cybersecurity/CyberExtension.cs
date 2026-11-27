// The Cybersecurity extension (docs/EXTENSIONS.md §3): currency Bits, root "cybersecurity" under vanilla "lists".
using System;
using System.Collections.Generic;

namespace JavaFarmer.Plugin.Extensions.Cyber
{
    public sealed class CyberState : ExtState
    {
        public Vault Vault = new Vault();
        public Crypto Crypto = new Crypto();
        public Hashing Hashing = new Hashing();
        public Ids Ids = new Ids();
        public Firewall Firewall = new Firewall();

        public override Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            ["version"] = 1.0, ["seed"] = Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["vault"] = Vault.ToJson(), ["crypto"] = Crypto.ToJson(), ["hashing"] = Hashing.ToJson(), ["ids"] = Ids.ToJson(), ["firewall"] = Firewall.ToJson(),
        };

        public override void FromJson(Dictionary<string, object> o)
        {
            Seed = ExtJson.ULong(o, "seed", Seed);
            Vault = new Vault(); Vault.FromJson(Obj(o, "vault")); Vault.Restore(Seed);
            Crypto = new Crypto(); Crypto.FromJson(Obj(o, "crypto")); Crypto.Restore(Seed);
            Hashing = new Hashing(); Hashing.FromJson(Obj(o, "hashing")); Hashing.Restore(Seed);
            Ids = new Ids(); Ids.FromJson(Obj(o, "ids")); Ids.Restore(Seed);
            Firewall = new Firewall(); Firewall.FromJson(Obj(o, "firewall")); Firewall.Restore(Seed);
        }
    }

    public sealed class CyberExtension : ContentExtension
    {
        public const string ExtId = "cybersecurity", Currency = "bits";
        public override string Id => ExtId;
        public override string DisplayName => "Cybersecurity";
        public override string Description => "Crack vaults, break ciphers and defend the farm network to earn Bits.";

        readonly List<ItemDef> items;
        readonly List<UnlockDef> unlocks;
        readonly List<BuiltinDef> builtins;
        public override IReadOnlyList<ItemDef> Items => items;
        public override IReadOnlyList<UnlockDef> Unlocks => unlocks;
        public override IReadOnlyList<BuiltinDef> Builtins => builtins;
        public override ExtState NewState(ulong seed) => new CyberState { Seed = seed };

        static string Page(string name) => "docs/mods/" + ExtId + "/" + name + ".md";

        public CyberExtension()
        {
            items = new List<ItemDef>
            {
                new ItemDef { Name = Currency, Priority = 200000, Docs = Page("index"), Icon = "bits.png", FallbackIcon = "power" },
            };
            unlocks = new List<UnlockDef>
            {
                new UnlockDef { Name = "cybersecurity", Parent = "lists", Order = 10, Docs = Page("cybersecurity"), DisplayCode = "vault_try()",
                    MaxLevel = 3, Cost = Cost.Of("pumpkin", 2000, "carrot", 2000),
                    LevelCosts = { Cost.Of(Currency, 5000), Cost.Of(Currency, 20000) },
                    Grants = { Currency, "vault_pin_length", "vault_try" } },
                new UnlockDef { Name = "side_channels", Parent = "cybersecurity", Order = 0, Docs = Page("side_channels"), DisplayCode = "pin[i]",
                    Cost = Cost.Of(Currency, 1500), Grants = { "vault_check_digit" } },
                new UnlockDef { Name = "cryptography", Parent = "cybersecurity", Order = 1, Docs = Page("cryptography"), DisplayCode = "intercept_message()",
                    MaxLevel = 3, Cost = Cost.Of(Currency, 50000), LevelCosts = { Cost.Of(Currency, 100000), Cost.Of(Currency, 200000) },
                    Grants = { "intercept_message", "cipher_key_length", "decrypt_submit", "char_code", "from_char_code" } },
                new UnlockDef { Name = "hashing", Parent = "cryptography", Order = 0, Docs = Page("hashing"), DisplayCode = "hash_text()",
                    MaxLevel = 2, Cost = Cost.Of(Currency, 150000), LevelCosts = { Cost.Of(Currency, 400000) },
                    Grants = { "hash_text", "leaked_hash_count", "leaked_hash", "leaked_salt", "wordlist_size", "wordlist_word", "crack_submit" } },
                new UnlockDef { Name = "intrusion_detection", Parent = "side_channels", Order = 0, Docs = Page("intrusion_detection"), DisplayCode = "block_ip()",
                    MaxLevel = 2, Cost = Cost.Of(Currency, 300000), LevelCosts = { Cost.Of(Currency, 800000) },
                    Grants = { "log_count", "log_ip", "log_user", "log_success", "block_ip", "log_next" } },
                new UnlockDef { Name = "firewall", Parent = "intrusion_detection", Order = 0, Docs = Page("firewall"), DisplayCode = "packet_deny()",
                    MaxLevel = 3, Cost = Cost.Of(Currency, 600000), LevelCosts = { Cost.Of(Currency, 1500000), Cost.Of(Currency, 3000000) },
                    Grants = { "packet_next", "packet_port", "packet_src", "packet_size", "packet_allow", "packet_deny" } },
                new UnlockDef { Name = "secure_farm", Parent = "firewall", Order = 0, Docs = Page("secure_farm"), DisplayCode = "yield * 1.05",
                    MaxLevel = 5, Cost = Cost.Of(Currency, 1000000, "gold", 5000), LevelCosts = { Cost.Of(Currency, 1500000, "gold", 7500) }, LevelFactor = 1.5f,
                    BonusPerLevel = 0.05, BonusKey = "ext_bonus_yield" },
            };
            builtins = BuildBuiltins();
            Link();
        }

        static CyberState St(ExtCall c) => c.StateAs<CyberState>();

        Vault VaultOf(ExtCall c) { var s = St(c); s.Vault.Ensure(s.Seed, c.Level("cybersecurity")); return s.Vault; }
        Crypto CryptoOf(ExtCall c) { var s = St(c); s.Crypto.Ensure(s.Seed, c.Level("cryptography")); return s.Crypto; }
        Hashing HashOf(ExtCall c) { var s = St(c); s.Hashing.Ensure(s.Seed, c.Level("hashing")); return s.Hashing; }
        Ids IdsOf(ExtCall c) { var s = St(c); s.Ids.Ensure(s.Seed, c.Level("intrusion_detection")); return s.Ids; }
        Firewall FwOf(ExtCall c) { var s = St(c); s.Firewall.Ensure(s.Seed, c.Level("firewall")); return s.Firewall; }

        static int Index(ExtCall c, int arg, int count, string what)
        {
            double v = c.Num(arg);
            if (v < 0 || v >= count) throw c.Error("index " + Fmt(v) + " is out of range (" + what + " has " + count + (count == 1 ? " entry" : " entries") + ": 0 to " + (count - 1) + ")");
            return (int)v;
        }

        internal static string Fmt(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

        List<BuiltinDef> BuildBuiltins()
        {
            var B = CyberBalance.VaultTryTicks;
            return new List<BuiltinDef>
            {
                // --- cybersecurity (brute force)
                Def("vault_pin_length", "", "int", 1, "Number of digits of the vault PIN.", c => (double)VaultOf(c).PinLength),
                Def("vault_try", "pin: int", "bool", B, "Tries a PIN on the vault; True and Bits if it was right (a new PIN is set).", c =>
                {
                    var v = VaultOf(c);
                    bool ok = v.Try(St(c).Seed, c.Num(0), out var reward);
                    if (ok) c.Grant(Currency, reward);
                    return ok;
                }),
                // --- side_channels
                Def("vault_check_digit", "i: int, d: int", "bool", CyberBalance.CheckDigitTicks, "Timing leak: True if digit i (0 = leftmost) of the PIN is d.", c =>
                {
                    var v = VaultOf(c);
                    int i = Index(c, 0, v.PinLength, "the PIN");
                    return v.Digit(i) == c.Num(1);
                }),
                // --- cryptography
                Def("intercept_message", "", "string", 1, "The intercepted encrypted message.", c => CryptoOf(c).Cipher),
                Def("cipher_key_length", "", "int", 1, "Length of the key of the intercepted message (1 = Caesar).", c => (double)CryptoOf(c).Key.Length),
                Def("decrypt_submit", "text: string", "bool", B, "Submits the decrypted message; True and Bits if it is right (a new message arrives).", c =>
                {
                    var k = CryptoOf(c);
                    bool ok = k.Submit(St(c).Seed, c.Str(0), out var reward);
                    if (ok) c.Grant(Currency, reward);
                    return ok;
                }),
                Def("char_code", "s: string, i: int", "int", 1, "Character code of s[i] (A = 65, a = 97, space = 32).", c =>
                {
                    var s = c.Str(0);
                    double i = c.Num(1);
                    if (i < 0 || i >= s.Length) throw c.Error("index " + Fmt(i) + " is out of range for a string of length " + s.Length);
                    return (double)s[(int)i];
                }, pure: true),
                Def("from_char_code", "n: int", "string", 1, "The one-character string with character code n (65 = A).", c =>
                {
                    double n = c.Num(0);
                    if (n < 32 || n > 126) throw c.Error("character code " + Fmt(n) + " is not a printable character (32 to 126)");
                    return ((char)(int)n).ToString();
                }, pure: true),
                // --- hashing
                Def("hash_text", "s: string", "int", 5, "Hash of a text: h = (h * 31 + char_code) % 1000000007 over its characters.", c => HashText.Hash(c.Str(0)), pure: true),
                Def("leaked_hash_count", "", "int", 1, "Number of entries in the leaked password database.", c => (double)Hashing.Entries),
                Def("leaked_hash", "i: int", "int", 1, "Stored hash of entry i.", c => { var h = HashOf(c); return h.Hashes[Index(c, 0, Hashing.Entries, "the leak")]; }),
                Def("leaked_salt", "i: int", "string", 1, "Salt of entry i (empty without salts).", c => { var h = HashOf(c); return h.Salts[Index(c, 0, Hashing.Entries, "the leak")]; }),
                Def("wordlist_size", "", "int", 1, "Number of words in the attacker's wordlist.", c => (double)HashText.Wordlist.Length, pure: true),
                Def("wordlist_word", "i: int", "string", 1, "Word i of the wordlist.", c => HashText.Wordlist[Index(c, 0, HashText.Wordlist.Length, "the wordlist")], pure: true),
                Def("crack_submit", "i: int, password: string", "bool", B, "Submits the password of entry i; True and Bits if it matches the hash.", c =>
                {
                    var h = HashOf(c);
                    int i = Index(c, 0, Hashing.Entries, "the leak");
                    bool ok = h.Submit(St(c).Seed, i, c.Str(1), out var reward);
                    if (ok) c.Grant(Currency, reward);
                    return ok;
                }),
                // --- intrusion_detection
                Def("log_count", "", "int", 1, "Number of entries in the login log.", c => (double)IdsOf(c).Log.Count),
                Def("log_ip", "i: int", "string", 1, "Source address of log entry i.", c => { var d = IdsOf(c); return d.Log[Index(c, 0, d.Log.Count, "the log")].Ip; }),
                Def("log_user", "i: int", "string", 1, "Account name of log entry i.", c => { var d = IdsOf(c); return d.Log[Index(c, 0, d.Log.Count, "the log")].User; }),
                Def("log_success", "i: int", "bool", 1, "True if the login of entry i succeeded.", c => { var d = IdsOf(c); return d.Log[Index(c, 0, d.Log.Count, "the log")].Success; }),
                Def("block_ip", "ip: string", "bool", B, "Blocks an address: True and Bits for an attacker, False (and a Bits penalty) for a legit user.", c =>
                {
                    var d = IdsOf(c);
                    int r = d.Block(c.Str(0));
                    if (r > 0) c.Grant(Currency, CyberBalance.IdsReward[d.Level]);
                    else if (r < 0) c.Take(Currency, CyberBalance.IdsPenalty[d.Level]);
                    return r > 0;
                }),
                Def("log_next", "", "void", B, "Archives the current log and starts the next one.", c => { var d = IdsOf(c); d.Next(St(c).Seed); return null; }),
                // --- firewall
                Def("packet_next", "", "bool", 10, "Moves to the next packet; False at the end of a batch (the batch is scored, a new one starts).", c =>
                {
                    var f = FwOf(c);
                    bool more = f.Next(St(c).Seed, out var reward);
                    if (!more && reward > 0) c.Grant(Currency, reward);
                    return more;
                }),
                Def("packet_port", "", "int", 1, "Destination port of the current packet.", c => (double)CurrentPacket(c).Port),
                Def("packet_src", "", "string", 1, "Source address of the current packet.", c => CurrentPacket(c).Src),
                Def("packet_size", "", "int", 1, "Size of the current packet in bytes.", c => (double)CurrentPacket(c).Size),
                Def("packet_allow", "", "void", 10, "Lets the current packet through.", c => { CurrentPacket(c); FwOf(c).Decide(true); return null; }),
                Def("packet_deny", "", "void", 10, "Drops the current packet.", c => { CurrentPacket(c); FwOf(c).Decide(false); return null; }),
            };
        }

        Packet CurrentPacket(ExtCall c)
        {
            var p = FwOf(c).Current;
            if (p == null) throw c.Error("there is no current packet: call packet_next() first");
            return p;
        }
    }
}
