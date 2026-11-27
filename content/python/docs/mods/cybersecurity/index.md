# Cybersecurity
This extension turns your programming skills into security skills. You attack the farm's own systems to find their weaknesses, and then you defend them. Every success earns `Items.Bits`, the currency of this extension, which pays for the next unlocks. All of it is real security knowledge, shrunk down to fit the farm.

## The tech tree
Cybersecurity unlocks after [Lists](docs/scripting/lists.md). From there, two branches:
- The vault branch: Brute Force leads to Side Channels, then Intrusion Detection, then Firewall, and finally Secure Farm.
- The cipher branch: Brute Force also leads to Cryptography, which leads to Hashing.

## The unlocks
- <unlock=cybersecurity>[Brute Force](docs/mods/cybersecurity/cybersecurity.md)</unlock>: guess the vault PIN by trying every possibility.
- <unlock=side_channels>[Side Channels](docs/mods/cybersecurity/side_channels.md)</unlock>: listen to a timing leak and find the PIN digit by digit.
- <unlock=cryptography>[Cryptography](docs/mods/cybersecurity/cryptography.md)</unlock>: break Caesar and Vigenere ciphers with frequency analysis.
- <unlock=hashing>[Hashing](docs/mods/cybersecurity/hashing.md)</unlock>: audit a leaked password database with a dictionary attack, and see why salts matter.
- <unlock=intrusion_detection>[Intrusion Detection](docs/mods/cybersecurity/intrusion_detection.md)</unlock>: find the attackers in a login log and block them.
- <unlock=firewall>[Firewall](docs/mods/cybersecurity/firewall.md)</unlock>: filter network packets with default deny and ordered rules.
- <unlock=secure_farm>[Secure Farm](docs/mods/cybersecurity/secure_farm.md)</unlock>: a safer farm keeps more of its harvest.

## General tips
- Time is the real cost. Every attempt that talks to a system, such as `vault_try()`, `decrypt_submit()` or `block_ip()`, takes `200` ticks, as long as a drone action. Reading information (a log entry, a packet, a character) usually takes `1` tick. So think first, then act once.
- Wrong answers are not free: they cost the full `200` ticks, and a wrong `block_ip()` even costs Bits.
- Every puzzle starts over by itself: a new PIN, message, leak, log or batch arrives when you finish the last one. So your programs can run forever in a `while True:` loop.
- Measure your programs. `get_tick_count()` and `num_items(Items.Bits)` tell you how many Bits a program earns per tick, and that is what decides which unlock to upgrade next.
- Upgrades usually make a puzzle harder and pay much more. After an upgrade, check that your program still follows the new rules.
- Each puzzle keeps its own state. If you have drones, give each drone a different puzzle.

## Glossary
- Brute force: trying every possible key or password until one works.
- Side channel: a leak of secret information through time, power or other effects, not through the official answer.
- Constant time: code whose running time does not depend on a secret.
- Cipher, key: a method to encrypt messages, and the secret that controls it.
- Frequency analysis: breaking a cipher by counting how often each letter appears.
- Hash: a one-way fingerprint of a text. It cannot be turned back into the text.
- Salt: a random value added to each password before hashing, so equal passwords get different hashes.
- Dictionary attack: trying the words of a list of common passwords.
- IDS: an intrusion detection system, which watches activity and raises alarms.
- False positive, false negative: an alarm without an attack, and an attack without an alarm.
- Port: the number of the service a packet wants to reach.
- Default deny: allow only what is explicitly permitted, drop everything else.
- Defense in depth: several independent layers of protection.
