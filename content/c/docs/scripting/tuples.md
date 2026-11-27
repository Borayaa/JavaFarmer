# Position (Tuples)
In Python, tuples are used to combine several values into a single value, like an `(x, y)` position. C has no tuples.
Instead, `farm.h` gives you the type `Position`, which holds an x and a y coordinate. You create one with braces:

`Position p = {1, 2};`

You can read the coordinates with `.x` and `.y`:

`printf("%d\n", p.y);`
prints `2`

A `Position` can't be changed after it has been created. `p.x = 3;` gives an error. Create a new `Position` instead. Inside an expression you write `(Position){1, 2}`:

`p = (Position){3, 2};`

To compare two positions, compare their coordinates: `p.x == 3 && p.y == 2`.

<unlock=mazes>Some game functions give you a position. For example, `measure_pos()` in a maze returns the `Position` of the treasure:

`Position treasure = measure_pos();
int treasure_x = treasure.x;
int treasure_y = treasure.y;`

</unlock><unlock=polyculture>`get_companion()` returns a `Companion`, or `NULL` if there is none. It holds an entity and a position, which you read with `->`: `c->entity`, `c->position`, and the shortcuts `c->x` and `c->y`.

`Companion c = get_companion();
if (c != NULL) {
	Position where = c->position;
	printf("%s at %d, %d\n", str_of(c->entity), where.x, where.y);
}`

</unlock><unlock=dicts>Positions can be used as keys in a map or as elements of a set. Two positions are the same key if their coordinates are equal.

`Map(Position, Position) d = map_of(Position, Position, {(Position){1, 2}, (Position){4, 5}});
printf("%s\n", str_of(map_get(d, (Position){1, 2})));`
prints `{4, 5}`

</unlock>
## Returning multiple values
A function can only return one value. If you want to return two numbers, you can return a `Position`:

`Position current_position(void) {
	return (Position){get_pos_x(), get_pos_y()};
}`

`Position here = current_position();
int x = here.x;
int y = here.y;`

For anything else, let the function store its results in global variables (see [Scopes](docs/scripting/scopes.md)):

`Entity best_plant;
double best_value;

void find_best(void) {
	best_plant = Entities.Carrot;
	best_value = 10;
}`
