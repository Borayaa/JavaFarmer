# Tuples
Tuples are a great way to combine multiple values into a single value.
To create a tuple, write the values in parentheses, separated by commas:

`let tuple = (1, 2);`

You can also unpack them into several variables again. In the code below, the tuple `(1, 2)` is unpacked into two variables `a` and `b`.

`let (a, b) = (1, 2);`

The values of a tuple can have different types, like `(Entity::Bush, 3)`. You read them with a dot and their position, starting at `0`. Tuples can't be changed after creation.

`let tuple = (1, 2);
println!("{}", tuple.1);`
prints `2`

`tuple.0 = 3;` throws an error. Create a new tuple instead: `let tuple = (3, tuple.1);`

To print a whole tuple, use `{:?}`: `println!("{:?}", tuple);` prints `(1, 2)`.

The game uses tuples for positions: a `Position` is an `(i32, i32)` tuple of an x and a y coordinate.
<unlock=mazes>For example, `measure()` in a maze returns the `Position` of the treasure. `measure()` can return different kinds of values, so tell Rust which one you want with the type of the variable:

`let (treasure_x, treasure_y): Position = measure();`

</unlock><unlock=polyculture>`get_companion()` returns an `Option<Companion>`. A `Companion` is an `(Entity, Position)` tuple: the type of plant and where it wants to be.

`if let Some((plant_type, (x, y))) = get_companion() {
	println!("{} at {}, {}", plant_type, x, y);
}`

</unlock><unlock=dicts>
Unlike lists tuples can be used as keys in a `HashMap`.

`let d = HashMap::from([((1, 2), (4, 5))]);

println!("{:?}", d[&(1, 2)]);`
prints `(4, 5)`</unlock>

They can also be useful for returning multiple values in a function.

`fn f() -> (i32, i32) {
	(1, 2)
}

let (a, b) = f();`
