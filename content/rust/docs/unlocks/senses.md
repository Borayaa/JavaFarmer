# Senses
The drone can see now! 

The functions `get_pos_x()` and `get_pos_y()` return the current x and y position of the drone as an `i32`. At the start position they are both `0`. The x position increases by `1` every tile towards `East` and the y position increases by `1` every tile towards `North`.

`num_items(item)` returns how many of an item you have, as an `f64`.
For example `num_items(Item::Hay)` returns how much hay you have.

`get_entity_type()` and `get_ground_type()` return the type of entity or ground that is under the drone.

Do a flip if you are over a bush:
`if get_entity_type() == Some(Entity::Bush) {
	do_a_flip();
}`

`None` is also unlocked now! `None` is a value that represents that there is no value.
In Rust, a value that might be missing has the type `Option`: an `Option<Entity>` is either `Some(entity)` or `None`. That's why the code above compares with `Some(Entity::Bush)` and not with `Entity::Bush`.

`get_entity_type()` returns `None` if there is no entity under the drone.

`if get_entity_type().is_none() {
	plant(Entity::Bush);
}`

If you want to find out how many of a particular unlock you have, use the `num_unlocked(unlock)` function.

For example, `num_unlocked(Unlock::Speed)` will return the number of speed upgrades you have.

`num_unlocked(Unlock::Senses)` will return `1` if senses are unlocked and `0` if they are not.

You can also use `num_unlocked()` on an `Item`, `Entity` or `Ground`. This will return `1` if it's unlocked otherwise `0`.

Be careful `num_unlocked(Unlock::Carrots)` will return the number of times it was unlocked/upgraded.
`num_unlocked(Item::Carrot)` will only return `0` or `1`. (Same for other plants)
