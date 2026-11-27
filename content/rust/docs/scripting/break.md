# Break
`break` allows stopping a loop early. When the `break;` statement is reached it will immediately exit the innermost loop and start running the code after the loop.

`loop {
	if can_harvest() {
		break;
	}
}
harvest();`

This code runs the `loop` until `can_harvest()` is `true` and then harvests. 
It has the same effect as

`while !can_harvest() {
}
harvest();`

`break` works the same way in `while` loops.

<unlock=for>It also works on `for` loops.

`for i in 0..10 {
	if i == 3 {
		break;
	}
	println!("{}", i);
}`
This prints `0`, `1` and `2`. When `i` reaches `3` the `break` ends the loop.

In nested loops `break` always exits the innermost loop.

`for i in 0..10 {
	for j in 0..10 {
		if j == 0 {
			break;
		}
		println!("this is never printed");
	}
	println!("this is printed 10 times");
}`

</unlock>Code directly after a `break;` in the same block could never run, so Rust warns about it (`unreachable statement`). That's why the examples put `break;` inside an `if`.
