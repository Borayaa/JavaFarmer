# std::vector (Lists)
A `std::vector` is an easy way to store multiple values in a single variable. (In Python this is called a list.) It needs `#include <vector>`.
You can create a new vector like this:

`std::vector<int> numbers = {2, 6, 12};`

The vector now contains the values `2`, `6` and `12`. The type `std::vector<int>` means "vector of `int`". All elements of a vector have the same type, so a vector of `int` can't hold an `Item`. Game types are written as they are, like `std::vector<Entity>`.
A vector can also be empty:

`std::vector<Entity> empty_vector;`

You can access an element of a vector by its index. The index is `0` for the first element, `1` for the second element, `2` for the third...

plants carrots
`std::vector<Entity> plants = {Entities::Tree, Entities::Carrot, Entities::Pumpkin};
plant(plants[1]);`

You can also change an element:
`numbers[0] = 5;`

You can iterate over a vector using a range-for loop. The following example sums all elements in the vector.

`std::vector<int> values = {4, 7, 2, 5};
int sum = 0;
for (int value : values) {
	sum += value;
}`
`sum` is now `18`

If you also need the index, use a classic `for` loop:

`for (int i = 0; i < values.size(); i++) {
	values[i] += 1;
}`

The following functions of a vector allow you to add and remove elements:

`v.push_back(elem)` adds an element to the end of the vector:

`std::vector<int> v = {2, 6, 12};
v.push_back(7);`
`v` is now `[2, 6, 12, 7]`

`v.erase(std::find(v.begin(), v.end(), elem))` removes the first occurrence of an element from a vector:

`std::vector<int> w = {1, 2, 4, 2};
w.erase(std::find(w.begin(), w.end(), 2));`
`w` is now `[1, 4, 2]`

`v.insert(v.begin() + index, elem)` inserts an element at the given index:

`std::vector<Item> items = {Items::Hay, Items::Carrot};
items.insert(items.begin() + 1, Items::Wood);`
`items` is now `[Items::Hay, Items::Wood, Items::Carrot]`

`v.pop_back()` removes the last element and `v.erase(v.begin() + index)` removes the element at the specified index.

`std::vector<int> u = {3, 5, 8, 25};
u.pop_back();`
`u` is now `[3, 5, 8]`
`u.erase(u.begin() + 1);`
`u` is now `[3, 8]`

`v.size()` returns the number of elements in the vector.
`std::vector<int> t = {3, 2, 1};
int x = t.size();`
`x` is now `3`

`v.empty()` returns `true` if the vector has no elements, `v.front()` and `v.back()` return the first and the last element, and `std::ranges::contains(v, elem)` returns `true` if the element is in the vector.

`std::println("{}", v);` prints a whole vector like `[2, 6, 12, 7]`.

Vectors can hold other vectors. This is handy to store something for every tile of the farm:

`int size = get_world_size();
std::vector<std::vector<bool>> watered(size, std::vector<bool>(size, false));
watered[get_pos_x()][get_pos_y()] = true;`

`std::vector<bool>(size, false)` creates a vector with `size` elements that are all `false`.

## Copies and references
Unlike lists in Python, vectors in C++ are values. Assigning a vector to another variable makes a copy, and changes to the copy are not seen by the original.

`std::vector<int> a = {1, 2};
std::vector<int> b = a;
b.pop_back();`
`a` is still `[1, 2]` and `b` is now `[1]`

The same happens when you pass a vector to a function: the function gets its own copy. If the function should change the caller's vector, add `&` to the parameter type. This makes the parameter a reference to the same vector:

`void add_hay(std::vector<Item>& stock) {
	stock.push_back(Items::Hay);
}`

Copying a big vector takes time, so use a reference when you only want to pass it on.
