# Hashing
A website must check your password, but it should never store it. If its database is ever stolen, the attacker would read every password at once. Instead the site stores a hash of the password.

A hash function turns any text into a number, always the same number for the same text. It is easy to compute in one direction and there is no way to run it backwards. To check a login, the site hashes the password you typed and compares it with the stored hash.

This is the difference between hashing and encryption: an encrypted message can be decrypted with the key, a hash cannot be "decrypted" at all.

## The leak
A password database of the farm has leaked. Your job is to show how weak it is.
- `leaked_hash_count()` is the number of entries (`8`). Entry `i` has the stored hash `leaked_hash(i)` and a salt `leaked_salt(i)` (see below).
- `hash_text(s)` is the hash function the database uses. It takes `5` ticks.
- `wordlist_size()` and `wordlist_word(i)` give you a wordlist of common passwords, the most popular ones first. Every leaked password is one of these words.
- `crack_submit(i, password)` checks a password for entry `i`. It returns `True` and pays Bits if it is right. When all `8` entries are cracked, a new leak arrives at once.
You cannot invert the hash, but you can guess: hash every word of the wordlist and see which one gives the stored hash. This is a dictionary attack.

## Precomputed tables
The hash of `"dragon"` is the same number in every leak. So you can hash the whole wordlist once, store the results in a dictionary that maps each hash to its word, and from then on crack any unsalted hash with one lookup. Attackers really do this, with huge precomputed tables built from billions of leaked passwords.

## Salts
The defense is a salt: a random string, different for each user, stored next to the hash. The site stores `hash_text(salt + password)`. Now two users with the same password have different hashes, and a table computed in advance is useless: for every salt you must hash the whole wordlist again.
At level `1` the salts are empty (`leaked_salt(i)` is `""`). From level `2` every entry has a salt of `4` lowercase letters.

## Worked example
The program builds the table once, uses it for unsalted entries, and does a dictionary attack per entry for salted ones. It works at both levels.

`# Example: crack leaked hashes with a precomputed table and a dictionary attack
table = {}
for w in range(wordlist_size()):
	word = wordlist_word(w)
	table[str(hash_text(word))] = word

while True:
	for i in range(leaked_hash_count()):
		salt = leaked_salt(i)
		stored = leaked_hash(i)
		if salt == "":
			key = str(stored)
			if key in table:
				crack_submit(i, table[key])
		else:
			for w in range(wordlist_size()):
				word = wordlist_word(w)
				if hash_text(salt + word) == stored:
					crack_submit(i, word)
					break`

## Rewards and costs
- Level `1`: `40` Bits per cracked entry. Building the table costs about `1500` ticks once; after that almost all the time goes into the `200` ticks of each `crack_submit()`.
- Level `2`: `400` Bits per entry, but every entry needs its own search. Because the wordlist starts with the most popular passwords, the search usually stops early.
A wrong `crack_submit()` also costs `200` ticks, so only submit a password whose hash you have checked.

## In the real world
The hash here is a toy. Its formula is so simple that, with some algebra, you can construct texts with any hash you want. Real systems use cryptographic hash functions, where that is practically impossible.
Even a perfect hash does not save weak passwords: if your password is in a wordlist, a dictionary attack finds it. That is why real password storage uses three ideas together:
- a unique random salt per user, so precomputed tables do not work and every account must be attacked separately;
- a deliberately slow hash such as bcrypt, scrypt or Argon2, which can take thousands of times longer than `hash_text()`, so each guess is expensive;
- users with long, unique passwords that are in no wordlist, ideally from a password manager.
Also notice the duplicates: two entries of an unsalted leak with the same hash have the same password, which tells an attacker that it is a popular one.

## Exercises
1. Count how many words the salted search tries per entry on average. How much would it cost if the wordlist were shuffled instead of sorted by popularity?
2. At level `1`, the same password can appear in several entries. Does your program handle that? Does it waste any `crack_submit()` calls?
3. Imagine `hash_text()` took `5000` ticks instead of `5`, like a slow password hash. Estimate how many Bits per `1000` ticks each level would still earn.
<spoiler=show hint for exercise 1>Keep a counter that you increase inside the inner loop and divide it by the number of salted entries at the end. A shuffled list would need about half of `wordlist_size()` tries per entry.</spoiler>
<spoiler=show hint for exercise 3>At level `1` you hash the wordlist only once, so the slow hash hardly matters. At level `2` every tried word costs the full price. This is exactly why salts and slow hashes work so well together.</spoiler>
