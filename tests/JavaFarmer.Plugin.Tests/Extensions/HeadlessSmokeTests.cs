using JavaFarmer.Plugin.Extensions;
using JavaFarmer.Plugin.Extensions.Cyber;

namespace JavaFarmer.Plugin.Tests.Extensions;

[Collection("Game")]
public class HeadlessSmokeTests
{
    [Fact]
    public void BruteForceProgram_EarnsBits()
    {
        var levels = ExtTestUtil.AllLevels(ExtTestUtil.Cyber);
        var r = Headless.Run(@"
n = vault_pin_length()
found = 0
for pin in range(10 ** n):
    if vault_try(pin):
        found += 1
        print('cracked', pin)
        break
quick_print(num_items(Items.Bits))
", levels);
        Assert.True(r.Error == null, r.ToString());
        Assert.True(r.Finished, r.ToString());
        Assert.True(r.Item(CyberExtension.Currency) > 0, r.ToString());
    }
}
