# Continue
`continue` allows stopping the current iteration of a loop and jumping to the next iteration of the innermost loop.

`loop {
	if !can_harvest() {
		continue;
	}
	harvest();
}`

This code only calls `harvest()` when `can_harvest()` is `true`. 
It has the same effect as

`loop {
	if can_harvest() {
		harvest();
	}
}`

`continue` works the same way in `while` loops. Careful with a `while` loop that counts: if `continue` jumps over the line `i += 1;`, `i` never changes.

<unlock=for>It also works on `for` loops.

`for i in 0..10 {
	if i % 2 == 0 {
		continue;
	}
	println!("{}", i);
}`

This runs all `10` iterations of the loop, but the `println!` statement is skipped for every even `i`. So it prints `1, 3, 5, 7, 9`.
In a `for` loop, `continue` simply moves on to the next element, so you can't forget to count.

In nested loops `continue` always affects the innermost loop.

`for i in 0..10 {
	for j in 0..10 {
		println!("this is printed 100 times");
		if j >= 0 {
			continue;
		}
		println!("this is never printed");
	}
	println!("this is printed 10 times");
}`
</unlock>
