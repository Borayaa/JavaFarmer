# Senses
The drone can see now! 

The methods `GetPosX()` and `GetPosY()` return the current x and y position of the drone as an `int`. At the start position they are both `0`. The x position increases by `1` every tile towards `East` and the y position increases by `1` every tile towards `North`.

`NumItems(item)` returns how many of an item you have, as a `double`.
For example `NumItems(Items.Hay)` returns how much hay you have.

`GetEntityType()` and `GetGroundType()` return the type of entity or ground that is under the drone.

Do a flip if you are over a bush:
`if (GetEntityType() == Entities.Bush)
{
	DoAFlip();
}`

The `null` keyword is also unlocked now! `null` is a value that represents that there is no value.

`GetEntityType()` returns `null` if there is no entity under the drone. That's why its return type is `Entity?`: the `?` means "or `null`".

`if (GetEntityType() == null)
{
	Plant(Entities.Bush);
}`

If you want to find out how many of a particular unlock you have, use the `NumUnlocked(unlock)` method.

For example, `NumUnlocked(Unlocks.Speed)` will return the number of speed upgrades you have.

`NumUnlocked(Unlocks.Senses)` will return `1` if senses are unlocked and `0` if they are not.

You can also use `NumUnlocked()` on Items, Entities or Grounds. This will return `1` if it's unlocked otherwise `0`.

Be careful `NumUnlocked(Unlocks.Carrots)` will return the number of times it was unlocked/upgraded.
`NumUnlocked(Items.Carrot)` will only return `0` or `1`. (Same for other plants)
