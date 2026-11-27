# Intrusion Detection
Now you switch sides. Attackers are trying to log in to the farm's accounts, and your job is to spot them in the login log and block them.

The log has `log_count()` entries, in the order the logins happened. Entry `i` tells you the source address `log_ip(i)` (a text such as `"10.0.3.17"`), the account name `log_user(i)` and whether the login worked, `log_success(i)`.
`block_ip(ip)` blocks an address. If it belonged to an attacker it returns `True` and pays Bits. If it belonged to a legit user, you have locked out a farmer: it returns `False` and you lose Bits. Blocking an address that is not in the log, or one you already blocked, does nothing.
The log does not change by itself. When you are done with it, `log_next()` archives it and loads the next one.

## Telling attackers from users
Real users make typos too. Here a user who mistypes the password tries again, at most a few times, and then gets in. An attacker who guesses passwords fails again and again.
So the simplest intrusion detection system (IDS) counts the failed logins of each address and raises an alarm above a threshold. Choosing the threshold is the whole art:
- too low, and you block farmers who mistyped: false positives;
- too high, and attackers slip through: false negatives.
In these logs a user never fails more than `3` times, and every attacker fails at least `6` times, so a threshold of `5` sits safely in the gap.

## Worked example
The dictionary `failures` maps each address to its number of failed logins.

`# Example: find the attackers in each login log and block them
while True:
	failures = {}
	for i in range(log_count()):
		if not log_success(i):
			ip = log_ip(i)
			if ip in failures:
				failures[ip] += 1
			else:
				failures[ip] = 1
	for ip in failures:
		if failures[ip] >= 5:
			if block_ip(ip):
				quick_print("blocked", ip, failures[ip])
			else:
				quick_print("false alarm", ip)
	log_next()`

## Levels and rewards
- Level `1`: one brute-force attacker per log, with `12` to `20` failures against one or two accounts. `150` Bits per attacker, `100` Bits penalty per false alarm.
- Level `2`: `2` or `3` attackers per log, `200` Bits each, `150` Bits penalty. Besides a brute-forcer there are password sprayers, who try one password on many different accounts to stay under per-account lockouts, and slow brute-forcers, who fail only `6` to `8` times. There is also an office: several users share one address, `"10.1.0.1"`, so it has many logins but almost no failures. A detector that counts all logins instead of failures blocks the office.
The example works at both levels. Reading the log costs only a few ticks per entry, so most of the time goes into `block_ip()` and `log_next()`.

## In the real world
Tools like fail2ban read server logs and block addresses after a number of failed logins, and big companies feed all their logs into a SIEM, a system that applies many such rules at once. The trade-off between false positives and false negatives is real: an IDS that cries wolf all day gets ignored, one that is too quiet misses the attack.
Real attackers are also harder to catch than here. Sprayers spread their attempts over many addresses and many hours, and many legit users share one address behind a router (that is the office here). So defenders count per account as well as per address, look for successful logins after many failures (an account that may be compromised), and do not rely on detection alone: strong passwords and a second factor make most guessing pointless.

## Exercises
1. Count your false alarms and missed attackers. `block_ip()` tells you which blocks were right; how many attackers did you miss? Try thresholds `3`, `4`, `5` and `8` at your level.
2. For each address, also count the number of different accounts it failed on. Print the addresses that look like password sprayers.
3. Some attackers succeed in the end. Print every account that had a successful login right after a series of failures from the same address: in real life, that password must be changed at once.
<spoiler=show hint for exercise 2>Use a dictionary that maps each address to another dictionary (or a set) of account names. The number of different accounts is its `len()`.</spoiler>
<spoiler=show hint for exercise 3>Keep a dictionary with the number of failures in a row of each address. Set it back to `0` on a success, and print the account if it was at least `5` just before the success.</spoiler>
