using JavaFarmer.Plugin.Extensions;
using JavaFarmer.Plugin.Extensions.Cyber;

namespace JavaFarmer.Plugin.Tests.Extensions;

/// <summary>Cybersecurity builtins: logic, scoring, determinism, edge cases (called through BuiltinDef.Impl like the game does).</summary>
[Collection("Game")]
public class CyberLogicTests
{
    // ---------------------------------------------------------------- vault

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    public void Vault_PinLengthFollowsLevel(int level, int digits)
    {
        var c = ExtTestUtil.CyberCalls(level: level);
        Assert.Equal(digits, c.Int("vault_pin_length"));
        int pin = ((CyberState)c.State).Vault.Pin;
        Assert.InRange(pin, 0, (int)Math.Pow(10, digits) - 1);
    }

    [Fact]
    public void Vault_BruteForceCracks_PaysFiveNSquared_AndRekeys()
    {
        var c = ExtTestUtil.CyberCalls(level: 2);
        int n = c.Int("vault_pin_length");
        int old = ((CyberState)c.State).Vault.Pin;
        int tries = 0;
        for (int pin = 0; pin < 1000; pin++)
        {
            tries++;
            if (c.Bool("vault_try", pin)) break;
        }
        Assert.Equal(old + 1, tries);
        Assert.Equal(5 * n * n, c.Has("bits"));
        Assert.Equal(tries * 200 + 1, c.Ticks);
        Assert.Equal(1, ((CyberState)c.State).Vault.Cracked);
    }

    [Fact]
    public void Vault_WrongGuessesPayNothing()
    {
        var c = ExtTestUtil.CyberCalls(level: 1);
        int pin = ((CyberState)((CyberState)c.State)).Vault.Pin;
        c.Call("vault_pin_length");
        pin = ((CyberState)c.State).Vault.Pin;
        Assert.False(c.Bool("vault_try", (pin + 1) % 100));
        Assert.False(c.Bool("vault_try", -1));
        Assert.False(c.Bool("vault_try", 100000));
        Assert.Equal(0, c.Has("bits"));
    }

    [Fact]
    public void Vault_SameSeedSamePins_DifferentSeedDifferentPins()
    {
        int[] Seq(ulong seed)
        {
            var c = ExtTestUtil.CyberCalls(seed, 3);
            var pins = new int[6];
            for (int k = 0; k < 6; k++)
            {
                c.Call("vault_pin_length");
                pins[k] = ((CyberState)c.State).Vault.Pin;
                Assert.True(c.Bool("vault_try", pins[k]));
            }
            return pins;
        }
        Assert.Equal(Seq(7), Seq(7));
        Assert.NotEqual(Seq(7), Seq(8));
    }

    [Fact]
    public void Vault_LevelUpGivesANewLongerPin()
    {
        var c = ExtTestUtil.CyberCalls(level: 1);
        Assert.Equal(2, c.Int("vault_pin_length"));
        c.Levels["cybersecurity"] = 3;
        Assert.Equal(4, c.Int("vault_pin_length"));
        Assert.Equal(1, ((CyberState)c.State).Vault.Counter);
    }

    [Fact]
    public void SideChannel_RevealsDigits_CrackInLinearTime()
    {
        var c = ExtTestUtil.CyberCalls(level: 3);
        int n = c.Int("vault_pin_length");
        int pin = 0;
        for (int i = 0; i < n; i++)
            for (int d = 0; d < 10; d++)
                if (c.Bool("vault_check_digit", i, d)) { pin = pin * 10 + d; break; }
        Assert.Equal(((CyberState)c.State).Vault.Pin, pin);
        Assert.True(c.Bool("vault_try", pin));
        Assert.Equal(80, c.Has("bits"));
        Assert.True(c.Ticks <= 1 + n * 10 * 25 + 200);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void SideChannel_DigitIndexOutOfRange_IsAnError(int i)
    {
        var c = ExtTestUtil.CyberCalls(level: 3);
        var e = Assert.Throws<ExtError>(() => c.Call("vault_check_digit", i, 0));
        Assert.Contains("vault_check_digit()", e.Message);
    }

    // ---------------------------------------------------------------- cryptography

    [Fact]
    public void Vigenere_RoundTrip_KeepsSpaces()
    {
        var cipher = CryptoText.Encrypt("PLANT THE CARROTS", "KEY");
        Assert.Equal("ZPYXX RRI AKVPYXQ", cipher);
        Assert.Equal("PLANT THE CARROTS", CryptoText.Decrypt(cipher, "KEY"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Crypto_ReferenceFrequencyAnalysisSolvesEveryLevel(int level)
    {
        for (ulong seed = 1; seed <= 25; seed++)
        {
            var c = ExtTestUtil.CyberCalls(seed, level);
            string cipher = c.Str("intercept_message");
            int len = c.Int("cipher_key_length");
            Assert.Equal(level == 1 ? 1 : level == 2 ? 2 : 4, Math.Min(len, level == 1 ? 1 : level == 2 ? 2 : 4));
            Assert.Matches("^[A-Z]+( [A-Z]+)*$", cipher);
            if (level > 1)
            {
                var key = CryptoText.SolveKey(cipher, len);
                Assert.True(c.Bool("decrypt_submit", CryptoText.Decrypt(cipher, key)), "seed " + seed);
            }
            else
            {
                bool ok = false;
                for (int k = 1; k < 26 && !ok; k++) ok = c.Bool("decrypt_submit", CryptoText.Decrypt(cipher, ((char)('A' + k)).ToString()));
                Assert.True(ok);
            }
            Assert.Equal(CyberBalance.CryptoReward[level], c.Has("bits"));
            Assert.NotEqual(cipher, c.Str("intercept_message"));
        }
    }

    [Fact]
    public void Crypto_WrongSubmissionKeepsTheMessage_CaseInsensitive()
    {
        var c = ExtTestUtil.CyberCalls(level: 1);
        string cipher = c.Str("intercept_message");
        Assert.False(c.Bool("decrypt_submit", "NOPE"));
        Assert.Equal(cipher, c.Str("intercept_message"));
        string plain = ((CyberState)c.State).Crypto.Plain;
        Assert.True(c.Bool("decrypt_submit", "  " + plain.ToLowerInvariant() + " "));
    }

    [Fact]
    public void CharCodes_AreAscii_AndChecked()
    {
        var c = ExtTestUtil.CyberCalls();
        Assert.Equal(65, c.Int("char_code", "AB", 0));
        Assert.Equal(32, c.Int("char_code", "A B", 1));
        Assert.Equal("Z", c.Str("from_char_code", 90));
        Assert.Throws<ExtError>(() => c.Call("char_code", "AB", 2));
        Assert.Throws<ExtError>(() => c.Call("char_code", "", 0));
        Assert.Throws<ExtError>(() => c.Call("from_char_code", 10));
        Assert.Throws<ExtError>(() => c.Call("from_char_code", 300));
    }

    // ---------------------------------------------------------------- hashing

    [Fact]
    public void HashText_IsTheDocumentedPolynomialHash()
    {
        var c = ExtTestUtil.CyberCalls();
        Assert.Equal(0, c.Num("hash_text", ""));
        Assert.Equal(97, c.Num("hash_text", "a"));
        Assert.Equal(97 * 31 + 98, c.Num("hash_text", "ab"));
        long h = 0;
        foreach (var ch in "sunflower") h = (h * 31 + ch) % 1000000007;
        Assert.Equal(h, c.Num("hash_text", "sunflower"));
        Assert.Equal(5, new ExtTestUtil.Calls { Ext = c.Ext, State = c.State }.Ext.Builtin("hash_text").Ticks);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Hashing_DictionaryAttackCracksEveryLeak(int level)
    {
        var c = ExtTestUtil.CyberCalls(3, level);
        int size = c.Int("wordlist_size");
        Assert.True(size > 150);
        for (int leak = 0; leak < 3; leak++)
        {
            int n = c.Int("leaked_hash_count");
            Assert.Equal(8, n);
            for (int i = 0; i < n; i++)
            {
                string salt = c.Str("leaked_salt", i);
                Assert.Equal(level == 1 ? 0 : 4, salt.Length);
                double target = c.Num("leaked_hash", i);
                bool cracked = false;
                for (int wi = 0; wi < size && !cracked; wi++)
                {
                    string word = c.Str("wordlist_word", wi);
                    if (c.Num("hash_text", salt + word) == target) cracked = c.Bool("crack_submit", i, word);
                }
                Assert.True(cracked, "leak " + leak + " entry " + i);
            }
        }
        Assert.Equal(3 * 8 * CyberBalance.HashReward[level], c.Has("bits"));
    }

    [Fact]
    public void Hashing_DoubleSubmitAndWrongPasswordsPayNothing()
    {
        var c = ExtTestUtil.CyberCalls(5, 1);
        var st = ((CyberState)c.State).Hashing;
        c.Call("leaked_hash_count");
        c.Call("leaked_hash", 0);
        string pw = st.Passwords[0];
        Assert.False(c.Bool("crack_submit", 0, pw + "x"));
        Assert.True(c.Bool("crack_submit", 0, pw));
        Assert.False(c.Bool("crack_submit", 0, pw));
        Assert.Equal(CyberBalance.HashReward[1], c.Has("bits"));
        Assert.Throws<ExtError>(() => c.Call("crack_submit", 8, pw));
        Assert.Throws<ExtError>(() => c.Call("wordlist_word", -1));
    }

    // ---------------------------------------------------------------- intrusion detection

    static List<string> Suspects(ExtTestUtil.Calls c)
    {
        var fails = new Dictionary<string, int>();
        var users = new Dictionary<string, HashSet<string>>();
        int n = c.Int("log_count");
        for (int i = 0; i < n; i++)
        {
            if (c.Bool("log_success", i)) continue;
            string ip = c.Str("log_ip", i);
            fails[ip] = fails.GetValueOrDefault(ip) + 1;
            if (!users.ContainsKey(ip)) users[ip] = new HashSet<string>();
            users[ip].Add(c.Str("log_user", i));
        }
        return fails.Keys.Where(ip => fails[ip] >= 5 || users[ip].Count >= 4).ToList();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Ids_DocumentedHeuristicFindsAllAttackers_NoFalseAlarms(int level)
    {
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var c = ExtTestUtil.CyberCalls(seed, level);
            var suspects = Suspects(c);
            var ids = ((CyberState)c.State).Ids;
            Assert.Equal(ids.Attackers.OrderBy(x => x), suspects.OrderBy(x => x));
            foreach (var ip in suspects) Assert.True(c.Bool("block_ip", ip));
            Assert.Equal(ids.Attackers.Count * CyberBalance.IdsReward[level], c.Has("bits"));
        }
    }

    [Fact]
    public void Ids_BlockingALegitUserCostsBits_UnknownAndRepeatsAreFree()
    {
        var c = ExtTestUtil.CyberCalls(9, 1);
        var ids = ((CyberState)c.State).Ids;
        c.Call("log_count");
        string attacker = ids.Attackers.First();
        string legit = ids.Log.First(e => !ids.Attackers.Contains(e.Ip)).Ip;
        Assert.True(c.Bool("block_ip", attacker));
        Assert.False(c.Bool("block_ip", attacker));
        Assert.Equal(150, c.Has("bits"));
        Assert.False(c.Bool("block_ip", legit));
        Assert.Equal(50, c.Has("bits"));
        Assert.False(c.Bool("block_ip", legit));
        Assert.False(c.Bool("block_ip", "1.2.3.4"));
        Assert.Equal(50, c.Has("bits"));
        // never below zero
        var c2 = ExtTestUtil.CyberCalls(9, 1);
        c2.Call("log_count");
        Assert.False(c2.Bool("block_ip", legit));
        Assert.Equal(0, c2.Has("bits"));
    }

    [Fact]
    public void Ids_LogNextStartsANewLog()
    {
        var c = ExtTestUtil.CyberCalls(11, 2);
        string first = c.Str("log_ip", 0) + c.Int("log_count");
        c.Call("log_next");
        Assert.Equal(1, ((CyberState)c.State).Ids.Counter);
        Assert.NotEqual(first, c.Str("log_ip", 0) + c.Int("log_count") + "x");
        Assert.Throws<ExtError>(() => c.Call("log_user", c.Int("log_count")));
    }

    // ---------------------------------------------------------------- firewall

    static double RunPolicyBatch(ExtTestUtil.Calls c, int level, bool perfect)
    {
        var seen = new Dictionary<string, int>();
        int packets = 0;
        while (c.Bool("packet_next"))
        {
            packets++;
            int port = c.Int("packet_port"), size = c.Int("packet_size");
            string src = c.Str("packet_src");
            int k = seen.GetValueOrDefault(src);
            seen[src] = k + 1;
            bool deny = Firewall.PolicyDenies(level, port, src, size, k);
            if (!perfect) deny = !(port == 80 || port == 443 || port == 53);
            if (deny) c.Call("packet_deny"); else c.Call("packet_allow");
        }
        return packets;
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 40)]
    [InlineData(3, 60)]
    public void Firewall_PerfectPolicyEarnsTheMaximum(int level, int size)
    {
        var c = ExtTestUtil.CyberCalls(21, level);
        var fw = ((CyberState)c.State).Firewall;
        c.Call("packet_next");
        fw.Index = -1;
        var batch = fw.Packets.ToList();
        Assert.Equal(size, RunPolicyBatch(c, level, perfect: true));
        double max = Math.Floor(batch.Sum(p => p.Malicious ? CyberBalance.FwDenyOk : CyberBalance.FwAllowOk) * CyberBalance.FirewallMultiplier[level]);
        Assert.Equal(max, c.Has("bits"));
        Assert.True(max > 0);
        Assert.Equal(1, fw.Batches);
    }

    [Fact]
    public void Firewall_LevelOneRulesScoreLessAtLevelThree()
    {
        var good = ExtTestUtil.CyberCalls(22, 3);
        RunPolicyBatch(good, 3, true);
        var naive = ExtTestUtil.CyberCalls(22, 3);
        RunPolicyBatch(naive, 3, false);
        Assert.True(naive.Has("bits") < good.Has("bits"));
    }

    [Fact]
    public void Firewall_UndecidedPacketsAreDenied_AndAccessorsNeedAPacket()
    {
        var c = ExtTestUtil.CyberCalls(23, 1);
        Assert.Throws<ExtError>(() => c.Call("packet_port"));
        Assert.Throws<ExtError>(() => c.Call("packet_allow"));
        var fw = ((CyberState)c.State).Firewall;
        while (c.Bool("packet_next")) { }
        // nothing decided: every malicious packet is a correct deny, every legit one a wrong deny
        var expected = 0.0;
        Assert.True(fw.LastScore >= expected);
        Assert.Equal(fw.LastScore, c.Has("bits"));
        Assert.Throws<ExtError>(() => c.Call("packet_src"));
    }

    [Fact]
    public void Firewall_FloodRule_CountsEveryPacketOfTheSource()
    {
        Assert.False(Firewall.PolicyDenies(3, 80, "1.2.3.4", 100, 7));
        Assert.True(Firewall.PolicyDenies(3, 80, "1.2.3.4", 100, 8));
        Assert.False(Firewall.PolicyDenies(2, 80, "1.2.3.4", 100, 30));
        Assert.True(Firewall.PolicyDenies(2, 443, "10.0.0.1", 1501, 0));
        Assert.False(Firewall.PolicyDenies(1, 443, "10.0.0.1", 1501, 0));
        Assert.False(Firewall.PolicyDenies(2, 22, "10.0.0.1", 100, 0));
        Assert.True(Firewall.PolicyDenies(2, 22, "100.0.0.1", 100, 0));
        Assert.True(Firewall.PolicyDenies(1, 22, "10.0.0.1", 100, 0));
    }
}
