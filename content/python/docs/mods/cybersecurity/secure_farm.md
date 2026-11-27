# Secure Farm
You have cracked vaults, broken ciphers, audited a password leak, caught intruders and built a firewall. The farm is now as well protected as your code, and a farm that no longer loses its crops to thieves simply keeps more of them.

## The bonus
Every level of Secure Farm makes every harvest yield `5` percent more, up to `5` levels (`25` percent). The extra items are whole items: the fractions are saved and paid out when they add up to a full item, so even a harvest of `1` counts. The unlock's tooltip shows the current bonus.
The bonus works on everything you harvest, as long as the Cybersecurity extension is enabled. It needs no new code: your old farming programs simply earn more.

## Paying for it
The first level costs `1000000` Bits and `5000` gold, the second `1500000` Bits and `7500` gold, and every further level `1.5` times the one before. That is a lot of Bits, so this is the moment to look back at your programs and make them fast:
- Measure. `get_tick_count()` and `num_items(Items.Bits)` before and after a few thousand ticks tell you the Bits per `1000` ticks of a program.
- Upgrade where it pays most. Compare the reward per tick of every unlock at its current level, not only the reward per success.
- Cut what you do not need: `packet_deny()` on a default-deny firewall, a second search for a password you already know, a wrong submit.

## Final challenge
Write one program that keeps earning Bits on its own, choosing what to work on.
1. Write each attack or defense from the previous pages as a function that does one round and returns the Bits it earned and the ticks it took, for example one message, one leak, one log or one firewall batch.
2. Call each function a few times, measure its Bits per `1000` ticks, and then spend most of the time on the best one, checking the others now and then in case you have upgraded something.
3. If you have drones, let each one run a different puzzle at the same time. Do not let two drones work on the same puzzle: they would fight over the same vault, message or batch.

A skeleton for step 2:

`def measure(round_function):
	t = get_tick_count()
	bits = num_items(Items.Bits)
	round_function()
	return (num_items(Items.Bits) - bits) * 1000 / (get_tick_count() - t)`

<spoiler=show hint>Keep the measured rates in a list. Use the best one most of the time, but every tenth round try a random other one with `random()`: this way your program notices when an upgrade has changed which puzzle is best.</spoiler>
