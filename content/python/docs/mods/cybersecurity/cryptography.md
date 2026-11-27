# Cryptography
Someone on the farm network is sending secret messages. `intercept_message()` gives you the latest one, encrypted. It contains only the capital letters `"A"` to `"Z"` and single spaces between the words. If you work out the original text and submit it with `decrypt_submit(text)`, you earn `Items.Bits` and the next message arrives.

## Caesar and Vigenere
Number the letters: `"A"` is `0`, `"B"` is `1`, ... `"Z"` is `25`. The Caesar cipher shifts every letter forward by the same amount, the key, and wraps around after `"Z"`. With the key `3`, `"A"` becomes `"D"` and `"Y"` becomes `"B"`.
The Vigenere cipher uses several shifts in turn. With the key `[3, 1, 4]` the 1st letter is shifted by `3`, the 2nd by `1`, the 3rd by `4`, the 4th by `3` again, and so on. Spaces are kept as they are and do not count as letters.
`cipher_key_length()` tells you how many shifts the key has. A length of `1` means plain Caesar.

To decrypt a letter with the number `c` and the shift `k`, shift it back: the plain letter is `(c - k) % 26`.

## Working with characters
Game Python has no functions to turn letters into numbers, so this unlock gives you two:
- `char_code(s, i)` returns the character code of the character at index `i` of `s`. `"A"` is `65`, `"Z"` is `90` and a space is `32`. So `char_code(s, i) - 65` is the letter number.
- `from_char_code(n)` returns the one-character string with the code `n`. `from_char_code(65 + 3)` is `"D"`.
You build the decrypted text with `+`, one character at a time.

## Frequency analysis
You do not know the key. But you know the message is English, and English letters are not equally common: about `12.7` percent of the letters in English text are `"E"`, `9.1` percent are `"T"`, and only `0.07` percent are `"Z"`.
A Caesar cipher moves those frequencies, it does not hide them. If `"H"` is the most common letter of the message, the shift is probably `3`, because `"E"` shifted by `3` is `"H"`.

To use all the letters and not only the most common one, try every shift `s` from `0` to `25` and measure how well the shifted counts fit English with the chi-squared score: for every letter, take the difference between the count you see and the count you expect, square it and divide by the expected count, then add it all up. The smaller the score, the better the fit. The shift with the smallest score is the key.

For a Vigenere key of length `L`, letters `0`, `L`, `2 * L`, ... were all shifted by the first key letter, letters `1`, `L + 1`, ... by the second, and so on. Each of these columns is just a Caesar cipher, so you solve each column on its own.

## Worked example
`english` holds the frequencies (in percent) of `"A"` to `"Z"`. Every message of levels `2` and `3` can be solved with exactly this table and this score. At level `1` the message is short, and about one message in a hundred fools the statistics: then the program falls back to trying all `25` Caesar keys.

`# Example: break Caesar and Vigenere ciphers by frequency analysis
english = [8.2, 1.5, 2.8, 4.3, 12.7, 2.2, 2.0, 6.1, 7.0, 0.15, 0.8, 4.0, 2.4, 6.7, 7.5, 1.9, 0.1, 6.0, 6.3, 9.1, 2.8, 1.0, 2.4, 0.15, 2.0, 0.07]

def letter_numbers(text):
	letters = []
	for i in range(len(text)):
		c = char_code(text, i)
		if c != 32:
			letters.append(c - 65)
	return letters

def best_shift(letters, key_length, column):
	counts = []
	for l in range(26):
		counts.append(0)
	n = 0
	j = column
	while j < len(letters):
		c = letters[j]
		counts[c] = counts[c] + 1
		n += 1
		j += key_length
	expected = []
	for l in range(26):
		expected.append(english[l] * n / 100)
	best = 0
	best_chi = 1000000000
	for s in range(26):
		chi = 0
		for l in range(26):
			diff = counts[(l + s) % 26] - expected[l]
			chi += diff * diff / expected[l]
		if chi < best_chi:
			best_chi = chi
			best = s
	return best

def decrypt(text, key):
	plain = ""
	j = 0
	for i in range(len(text)):
		c = char_code(text, i)
		if c == 32:
			plain = plain + " "
		else:
			k = key[j]
			plain = plain + from_char_code((c - 65 - k + 26) % 26 + 65)
			j += 1
			if j == len(key):
				j = 0
	return plain

while True:
	text = intercept_message()
	key_length = cipher_key_length()
	letters = letter_numbers(text)
	key = []
	key_word = ""
	for column in range(key_length):
		s = best_shift(letters, key_length, column)
		key.append(s)
		key_word = key_word + from_char_code(65 + s)
	if decrypt_submit(decrypt(text, key)):
		quick_print("solved, key", key_word)
	else:
		for s in range(1, 26):
			if decrypt_submit(decrypt(text, [s])):
				quick_print("solved by brute force, shift", s)
				break`

## Levels and rewards
- Level `1`: Caesar, about `40` letters, `1000` Bits per message.
- Level `2`: a key of `2` or `3` letters, about `90` letters, `3000` Bits.
- Level `3`: a key of `4` to `6` letters, about `230` letters, `15000` Bits.
A wrong `decrypt_submit()` costs `200` ticks and the message stays, so it pays to be sure before you submit. The program above works at every level without changes; it only gets slower per message as the messages get longer, and the reward grows much faster than the work.

## In the real world
Caesar and Vigenere are historical ciphers. Frequency analysis was described by the Arab scholar al-Kindi in the 9th century, and Vigenere, once called "the indecipherable cipher", was broken in the 19th century by Babbage and Kasiski. Kasiski also showed how to find the key length when nobody tells you: repeated groups of letters in the ciphertext tend to be a multiple of the key length apart.
Notice how much this cipher leaks: word lengths (the spaces), letter frequencies, repeats. Modern ciphers such as AES are designed so that the ciphertext looks like random noise and no statistics survive. Their security rests on the key alone, and the key space is so large (for example `2 ** 128` keys) that trying them all is impossible. Never invent your own cipher for real data: use a well-tested, standard one.

## Exercises
1. Measure how many ticks one message takes at your level. Which part of the program is the most expensive?
2. Strings are copied by `+`, so building a long text one character at a time gets slow: each `+` costs about as many ticks as the length of the result. Build each word first and add whole words to the text instead. How many ticks does that save at level `3`?
3. Level `1` only: instead of chi-squared, guess that the most common letter is `"E"`. How often is that right? Is it cheaper overall, counting the failed submits?
<spoiler=show hint for exercise 1>Put `t = get_tick_count()` before each step and print `get_tick_count() - t` after it. The `26 * 26` loop of `best_shift()` runs once per key letter.</spoiler>
<spoiler=show hint for exercise 2>Keep a variable `word = ""` for the current word. Add letters to `word`; when you meet a space, add `word + " "` to the text and start a new word. Do not forget the last word.</spoiler>
