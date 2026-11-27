# Cybersecurity: Brute Force
The farm has a vault. It is locked with a PIN, and every PIN you crack pays out `Items.Bits`.
You do not know the PIN. All you can do is try one: `vault_try(pin)` returns `True` if the PIN was right and `False` otherwise.

`vault_pin_length()` tells you how many digits the PIN has. A PIN with `2` digits is a number from `0` to `99`. A PIN written as 07 is simply the number `7`.

When you crack the vault, it is locked again at once with a new random PIN, so you can keep going.

## Brute force
The simplest attack is to try every possible PIN, one after the other, until one works. This is called a brute force attack.
It needs no cleverness at all, only time. And time is exactly what the vault takes from you: every `vault_try()` takes `200` ticks, as long as a full drone action.
Real systems do the same on purpose. A login page that answers slowly, or locks you out after a few wrong passwords, is rate limiting the attacker.

## Worked example
Try every PIN from `0` to the largest one. The largest PIN with `n` digits is `10 ** n - 1`, so `range(10 ** n)` covers them all.

`# Example: crack the vault again and again by brute force
while True:
	n = vault_pin_length()
	for pin in range(10 ** n):
		if vault_try(pin):
			print("cracked", pin)
			break`

The `break` leaves the `for` loop as soon as the vault opens. The `while` loop then starts on the new PIN.

## How long does it take?
With `n` digits there are `10 ** n` possible PINs. On average you find the right one after trying half of them.

- `2` digits: `100` PINs, about `50` tries, `10000` ticks per crack
- `3` digits: `1000` PINs, about `500` tries, `100000` ticks per crack
- `4` digits: `10000` PINs, about `5000` tries, `1000000` ticks per crack

Every extra digit makes the attack ten times slower. That is why the vault pays more for longer PINs: a PIN of length `n` is worth `5 * n * n` Bits.
Upgrading this unlock adds a digit to the PIN (up to `4` digits). With brute force alone, that is a bad deal: the reward grows by less than the work. You need a smarter attack first. Look at [Side Channels](docs/mods/cybersecurity/side_channels.md).

## In the real world
Passwords and PINs are attacked the same way. Each extra character multiplies the number of possibilities, so long passwords are much safer than short ones.
Defenders also make every guess expensive: lockouts after a few wrong tries, delays, and second factors such as a code on your phone.

## Exercises
1. Change the example so that it counts how many `vault_try()` calls it needed for each PIN and prints the count. Is it close to half of `10 ** n`?
2. Use `get_tick_count()` to measure how many ticks one crack takes.
3. Try the PINs from the largest to the smallest instead. Does that make the attack faster on average?
<spoiler=show hint for exercise 1>Keep a variable `tries = 0` before the `for` loop and add `1` to it before every `vault_try()`.</spoiler>
<spoiler=show hint for exercise 3>`range(10 ** n - 1, -1, -1)` counts down. Each PIN is equally likely, so the average does not change.</spoiler>
