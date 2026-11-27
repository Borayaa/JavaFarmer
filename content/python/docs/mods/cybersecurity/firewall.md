# Firewall
Every message on a network is split into small packets. A firewall looks at each packet that wants to enter the farm network and decides: let it through or drop it.

The packets arrive in batches. `packet_next()` moves to the next packet and returns `True`, or `False` when the batch is over. Then the batch is scored, you get your Bits, and the next call starts a new batch.
For the current packet, `packet_port()` is the destination port (the service it wants to reach), `packet_src()` is the source address and `packet_size()` is its size in bytes. Decide with `packet_allow()` or `packet_deny()`.

## Default deny
Each service listens on a port: web servers on `80` and `443`, the name service (DNS) on `53`, remote login (SSH) on `22`. The farm runs web and DNS. Nobody outside should reach anything else.
A good firewall does not try to list everything that is bad. It lists what is allowed and drops everything else. This is called default deny. Here a packet you did not decide on counts as denied, just like on a real firewall.

The farm's policy at level `1`: allow ports `80`, `443` and `53`, deny everything else.

## Worked example
`# Example: a default-deny firewall for the farm network
allowed_ports = {80, 443, 53}
while True:
	while packet_next():
		if packet_port() in allowed_ports:
			packet_allow()
		else:
			packet_deny()
	quick_print("batch scored, Bits:", num_items(Items.Bits))`

## Scoring
For every packet of a batch: a correct allow is `3` points and a correct deny `2`. Letting a bad packet through costs `6` points, dropping a good one costs `3`. The batch pays its points (never less than `0`) times the level multiplier: `3` at level `1`, `5` at level `2` and `8` at level `3`.
`packet_next()`, `packet_allow()` and `packet_deny()` cost `10` ticks each. Since undecided packets are denied anyway, you can leave out `packet_deny()` and save `10` ticks per dropped packet.

## Levels
Every level adds rules. They are checked in this order, and the first one that applies decides:
- Level `3`, flood rule: if the source has already sent `8` packets in this batch (count every packet you have seen from it, allowed or not), deny.
- Level `2`, size rule: deny any packet bigger than `1500` bytes.
- Level `1`, ports: allow `80`, `443` and `53`.
- Level `2`, SSH: allow port `22`, but only from internal addresses, the ones that start with `"10."`. The test `src[0] == "1" and src[1] == "0" and src[2] == "."` checks that: you can read the characters of a string by index, like the items of a list.
- Anything else: deny.
Batches have `30`, `40` and `60` packets at levels `1`, `2` and `3`. The example above still earns at a higher level, but it breaks the new rules and loses points: update your program when you upgrade.

## In the real world
Firewalls really work like this, with ordered rule lists that end in "deny all". Exposing only the services you need shrinks the attack surface: a database or remote desktop that cannot be reached cannot be attacked from outside.
The level `3` rule makes the firewall stateful: the decision depends on what it has seen before, not only on the current packet. Real stateful firewalls track whole connections, and rate limits like the flood rule protect services from being overwhelmed.
No single wall is enough. Firewalls, detection, strong passwords, salted hashes and careful code each stop some attacks and miss others. Stacking them is called defense in depth.

## Exercises
1. Leave out `packet_deny()` and measure the Bits per `1000` ticks before and after.
2. Level `2`: add the size rule and the SSH rule. Write a function `allowed(port, src, size)` that returns `True` or `False`, so your loop stays short.
3. Level `3`: add the flood rule. You need to remember how many packets each source has sent in the current batch, and forget it when a new batch starts.
<spoiler=show hint for exercise 2>Check the size first: a big packet on port `80` must be denied. Then the ports, then SSH with `src[0] == "1" and src[1] == "0" and src[2] == "."`.</spoiler>
<spoiler=show hint for exercise 3>Create an empty dictionary `counts` right before the inner `while packet_next():` loop. For each packet, read how many packets its source has sent so far (`0` if it is not in `counts`), decide with that number, and only then add `1` to it.</spoiler>
