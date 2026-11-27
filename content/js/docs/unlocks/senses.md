# Senses
The drone can see now! 

The functions `getPosX()` and `getPosY()` return the current x and y position of the drone. At the start position they are both `0`. The x position increases by `1` every tile towards `East` and the y position increases by `1` every tile towards `North`.

`numItems(item)` returns how many of an item you have.
For example `numItems(Items.Hay)` returns how much hay you have.

`getEntityType()` and `getGroundType()` return the type of entity or ground that is under the drone.

Do a flip if you are over a bush:
`if (getEntityType() === Entities.Bush) {
	doAFlip();
}`

The `null` keyword is also unlocked now! `null` is a value that represents that there is no value. (JavaScript also has `undefined`, which means the same thing in JavaFarmer. This documentation always uses `null`.)
For example, a function that has no `return` statement will actually return `null`.

`getEntityType()` returns `null` if there is no entity under the drone. Check for it with `=== null`:
`if (getEntityType() === null) {
	plant(Entities.Bush);
}`


If you want to find out how many of a particular unlock you have, use the `numUnlocked(unlock)` function.

For example, `numUnlocked(Unlocks.Speed)` will return the number of speed upgrades you have.

`numUnlocked(Unlocks.Senses)` will return `1` if senses are unlocked and `0` if they are not.

You can also use `numUnlocked()` on Items, Entities or Grounds. This will return `1` if it's unlocked otherwise `0`.

Be careful `numUnlocked(Unlocks.Carrots)` will return the number of times it was unlocked/upgraded.
`numUnlocked(Items.Carrot)` will only return `0` or `1`. (Same for other plants)