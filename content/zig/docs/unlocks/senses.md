# Senses
The drone can see now! 

The functions `farm.getPosX()` and `farm.getPosY()` return the current x and y position of the drone as a `usize` (a number that is never negative). At the start position they are both `0`. The x position increases by `1` every tile towards `.East` and the y position increases by `1` every tile towards `.North`.

`farm.numItems(item)` returns how many of an item you have, as an `f64`.
For example `farm.numItems(.Hay)` returns how much hay you have.

`farm.getEntityType()` and `farm.getGroundType()` return the type of entity or ground that is under the drone.

The `null` keyword is also unlocked now! `null` is a value that represents that there is no value.
In Zig only optional types can be `null`. An optional type is written with a `?` in front: `farm.getEntityType()` returns a `?farm.Entity`, because it returns `null` if there is no entity under the drone.

You can check for `null` directly:
`if (farm.getEntityType() == null) {
    _ = farm.plant(.Bush);
}`

To look at the entity itself, unwrap the optional first. `if (optional) |entity|` only runs its block if the value is not `null`, and gives you the value inside as `entity`.

Do a flip if you are over a bush:
`if (farm.getEntityType()) |entity| {
    if (entity == .Bush) {
        farm.doAFlip();
    }
}`

If you want to find out how many of a particular unlock you have, use the `farm.numUnlocked()` function.

For example, `farm.numUnlocked(farm.Unlock.Speed)` will return the number of speed upgrades you have.

`farm.numUnlocked(farm.Unlock.Senses)` will return `1` if senses are unlocked and `0` if they are not.

You can also use `farm.numUnlocked()` on Items, Entities or Grounds. This will return `1` if it's unlocked otherwise `0`.
`farm.numUnlocked()` accepts all of these types, so write the full name of the constant (`farm.Unlock.Speed`, not just `.Speed`).

Be careful `farm.numUnlocked(farm.Unlock.Carrots)` will return the number of times it was unlocked/upgraded.
`farm.numUnlocked(farm.Item.Carrot)` will only return `0` or `1`. (Same for other plants)