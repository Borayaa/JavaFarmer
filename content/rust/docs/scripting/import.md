# Import
Putting all your code in a single file quickly becomes unmanageable. 
In Rust every file is a module, and the name of the module is the file name without its extension (the code window `utils.rs` is the module `utils`). To use another file, write `mod utils;` at the top of your file. Then you can call its functions by writing the module name, `::` and the function name.
This also works for files in other languages: `helpers.py` or `Farm.java` are used from Rust as `mod helpers;` and `helpers::some_function()`, with the names written in that file. Values from Python, JavaScript and R files have no Rust type, so the editor can't check how you use them.

For example, create a new code window, name it `utils.rs` and write this into it (the first line is only a comment with the file name):

`// utils.rs
use farm::*;

pub fn plant_row(plant_type: Entity) {
	for _ in 0..get_world_size() {
		plant(plant_type);
		move_to(East);
	}
}`

The function is marked `pub` (public). In Rust everything in a file is private by default, so other files can only use the items marked `pub`. Without it you get the error `function plant_row is private`.

Now you can use it from any other file:

`// main.rs
use farm::*;

mod utils;

fn main() {
	utils::plant_row(Entity::Bush);
}`

`utils::plant_row(...)` calls the function `plant_row` in the file `utils`.
Constants work the same way: if `utils` declares `pub const ROWS: i32 = 3;`, other files can read it as `utils::ROWS`.

A file without a `main` function like `utils` is a library. It can't be run on its own, it just provides functions for other files.

### No need to read further

## use statements
You can also bring a function from another module into your file with `use`, so you can call it without the module name:

`// main.rs
use farm::*;

mod utils;
use utils::plant_row;

fn main() {
	plant_row(Entity::Bush);
}`
Brings only `plant_row` from `utils` into the file.

or

`// main.rs
use farm::*;

mod utils;
use utils::*;

fn main() {
	plant_row(Entity::Bush);
}`
Brings all `pub` items of `utils` into the file.

That's exactly what `use farm::*;` does: it brings all the drone functions of the module `farm` into your file. In the same way `use std::collections::HashMap;` brings in `HashMap` from Rust's standard library.

If two modules have an item with the same name and you bring both in with `*`, Rust reports an error when you use that name. Writing `utils::plant_row()` always works and shows where a function comes from, so prefer it if you are not sure.

# How it really works

## Loading files
A Rust module can only contain items: functions (`fn`), constants (`const`) and `static` variables. There are no statements outside of functions, and the starting values of `const` and `static` must be fixed values. So using a module never runs any code by itself, there are no side effects like harvesting while a file is loaded. 
When a file is used by another file, its `main` function does not run. It only runs when you press the play button of that file.

The game loads a file the first time something from it is used. The file is only loaded once, so all files that use `utils` share the same `utils` (within one drone, see the Megafarm unlock).
A `static mut` variable of another file can't be used in JavaFarmer. Give that file a `pub fn` that reads or changes the value instead.

## Using each other
Two files can use each other's functions and constants. Inside a library, refer to another file of your farm with `crate::` in front of its name, the files are declared once with `mod` in `main.rs`.

file `a`:
`// a.rs
use farm::*;

pub const X: i32 = 0;

pub fn print_b() {
	println!("{}", crate::b::Y);
}`

file `b`:
`// b.rs
use farm::*;

pub const Y: i32 = 1;

pub fn print_a() {
	println!("{}", crate::a::X);
}`

`// main.rs
use farm::*;

mod a;
mod b;

fn main() {
	a::print_b();
	b::print_a();
}`

This always works, because functions only run when they are called and constants are fixed values.
