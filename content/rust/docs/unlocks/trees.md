# Trees
[Trees](objects/tree) are a better way to get wood than bushes. They give 5 wood each. Like bushes, they can be planted on grass or soil.

Trees like to have some space and planting them right next to each other will slow down their growth. The growing time is doubled for each tree that is on a tile directly to the north, east, west or south of it. So if you plant trees on every tile, they will take `2*2*2*2 = 16` times longer to grow.

<spoiler=show> The `%` operator can be useful here. Remember that the `%` operator returns the remainder of the division. Even numbers divided by `2` have a remainder of `0` and odd numbers divided by `2` have a remainder of `1`.
So you can check if a number is even like this:

`fn is_even(n: i32) -> bool {
	n % 2 == 0
}`

This returns `true` if n is even and `false` if it isn't. The last line has no `;`, so its value is what the function returns.
</spoiler>
