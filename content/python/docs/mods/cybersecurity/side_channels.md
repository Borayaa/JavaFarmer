# Side Channels
Brute force treats the vault as a black box: you put a PIN in and get back only `True` or `False`. Real machines are not black boxes. They take time, use power, make noise and get warm, and all of that can depend on the secret they are protecting. Information that leaks this way, beside the official answer, is called a side channel.

The farm's vault has one. To check a PIN it compares the digits one by one, from left to right, and stops at the first wrong digit. A wrong first digit is rejected very fast. A right first digit and a wrong second one takes a little longer. By timing the vault carefully you can find out whether one single digit is right.

`vault_check_digit(i, d)` does that measurement for you. It returns `True` if digit number `i` of the PIN is `d`. Digit `0` is the leftmost one. A real timing difference is tiny and noisy, so the measurement has to be repeated many times: one check takes `25` ticks.

The checks do not open the vault and do not pay anything. Once you know every digit, one `vault_try(pin)` opens it.

## From digits to a PIN
If the digits of a `3` digit PIN are `4`, `0` and `7`, the PIN is `4 * 100 + 0 * 10 + 7`, which is `407`.
You can build it from left to right: start with `0`, and for every digit multiply what you have by `10` and add the digit.

## Worked example
For every position, try the digits `0` to `8`. If none of them is right, the digit must be `9`, so you never need to check it.

`# Example: crack the vault by listening to the timing leak
while True:
	n = vault_pin_length()
	pin = 0
	for i in range(n):
		digit = 9
		for d in range(9):
			if vault_check_digit(i, d):
				digit = d
				break
		pin = pin * 10 + digit
	if vault_try(pin):
		quick_print("cracked", pin)`

## How much faster is it?
Each digit needs at most `9` checks and on average about `5`. So a PIN with `n` digits needs about `5 * n` checks instead of about `10 ** n / 2` tries.

- `2` digits: about `270` ticks of checks plus one `200` tick try, instead of about `10000` ticks
- `4` digits: about `540` ticks of checks plus one try, instead of about `1000000` ticks

The work now grows linearly with the length of the PIN instead of exponentially. That changes the deal for the upgrades of [Brute Force](docs/mods/cybersecurity/cybersecurity.md): a longer PIN pays `5 * n * n` Bits but costs only a few more checks. With all `3` levels (`4` digits, `80` Bits per crack) the example above earns about `2.5` times as much per tick as with `2` digits.

## In the real world
Timing attacks are real and well studied. The classic mistake is comparing a secret (a password, a key, a security token) with an ordinary comparison that returns as soon as one character differs. An attacker who can measure the answer time, even across a network and with lots of noise, can repeat each guess many times, average the timings and recover the secret one character at a time, exactly like here.

The defense is a constant-time comparison: always compare every character, collect the result, and answer only at the end, so the time does not depend on where the first difference is. Security libraries provide such functions, and good code uses them for every secret.
Other side channels include power use, electromagnetic radiation, sound and caches. The lesson is the same each time: a system can be correct and still leak its secrets through how it works.

## Exercises
1. Count the `vault_check_digit()` calls of each crack and print the average. Is it close to `5.4` per digit?
2. Measure the Bits per `1000` ticks with `get_tick_count()` and `num_items(Items.Bits)`. Compare it with the brute force program on the [Brute Force](docs/mods/cybersecurity/cybersecurity.md) page.
3. Write a function `safe_equal(a, b)` that compares two lists of digits in constant time: it must always look at every position, and only return at the end.
<spoiler=show hint for exercise 1>A digit `d` from `0` to `8` needs `d + 1` checks and a `9` needs `9` checks. The average over all ten digits is `54 / 10`.</spoiler>
<spoiler=show hint for exercise 3>Use a variable `same = True` and set it to `False` when two digits differ, but do not `break` and do not `return` inside the loop.</spoiler>
