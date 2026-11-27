# Position (Tuples)
In Python, tuples are used to combine several values into a single value, like an `(x, y)` position. C++ has no tuples in JavaFarmer.
Instead, the game gives you two small types that each hold a fixed set of values: `Position` and `Companion`.

A `Position` holds an x and a y coordinate. You create one with braces:

`Position pos = Position{1, 2};`

`pos.x` is the x coordinate and `pos.y` is the y coordinate.

You can also unpack a position into two variables. This is called a structured binding. In the code below, the position `(1, 2)` is unpacked into two variables `a` and `b`.

`auto [a, b] = pos;`

Like tuples, positions cannot be changed after creation.

`std::cout << pos.y << std::endl;`
prints `2`

Writing `pos.x = 3;` is an error. Create a new position instead: `pos = Position{3, pos.y};`

<unlock=mazes>For example, `measure()` in a maze returns the `Position` of the treasure. `measure()` can return different kinds of values, so you tell C++ that you want a `Position` by assigning it to one:

`Position treasure = measure();
int treasure_x = treasure.x;
int treasure_y = treasure.y;`

</unlock>A `Companion` is what `get_companion()` returns. It holds an entity and a position: `c->entity` is the type of plant and `c->position` is the `Position`.
`get_companion()` returns a `std::optional<Companion>` because there might be no companion. Check it with `if (c)` before you use it.

<unlock=polyculture>`auto c = get_companion();
if (c) {
	auto [wanted, where] = *c;
	std::println("{} at {}, {}", wanted, where.x, where.y);
}`

</unlock><unlock=dicts>Positions can be used as keys in a `std::map` or as elements of a `std::set`. Two positions are equal if their coordinates are equal.

`std::map<Position, Position> next;
next[Position{1, 2}] = Position{4, 5};
std::println("{}", next[Position{1, 2}]);`
prints `(4, 5)`
</unlock>

## Returning multiple values
A function can only return one value. If you want to return two numbers, you can return a `Position`:

`Position current_position() {
	return Position{get_pos_x(), get_pos_y()};
}

int main() {
	auto [x, y] = current_position();
}`

For other combinations of values, the function can store its results in global variables (see [Scopes](docs/scripting/scopes.md)), or change a vector that was passed to it as a reference `std::vector<int>& results`.
