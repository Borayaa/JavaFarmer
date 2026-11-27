# Operators
arithmetic operators: `+, -, *, /, %`
comparison operators: `==, !=, <=, >=, <, >`
boolean operators: `!, &&, ||`

Note: Rust has two kinds of numbers here. `i32` holds whole numbers and `f64` holds numbers with decimal places. Rust never mixes them: both sides of an operator must have the same type. This matters for division: `/` on two `i32`s gives an `i32`.

For assignment operators like `+=` you need to unlock the "Variables" unlock.

## Introduction
Operators allow you to compare, modify and combine values. 
The arithmetic operators `+, -, *, /, %` are used to perform common mathematical operations on numbers. 
The comparison operators `==, !=, <=, >=, <, >` are used to compare values. The result is always either `true` or `false`.
The logic operators (also called boolean operators) `!, &&, ||` are used to combine truth values.

## Arithmetic Operators
`+` and `-` are used for addition and subtraction.

`2 + 3` evaluates to `5`
`3 - 2` evaluates to `1`

`*` and `/` are used for multiplication and division.

`2 * 3` evaluates to `6`
`5.0 / 2.0` evaluates to `2.5`

If both numbers are `i32`s, `/` is an integer division. The result is an `i32` and everything after the decimal point is cut off.

`5 / 2` evaluates to `2`
`-5 / 2` evaluates to `-2`

Mixing the two kinds is an error: `5 / 2.0` gives `cannot divide {integer} by {float}`. Convert one side with `as` instead: `5 as f64 / 2.0` evaluates to `2.5` and `2.7 as i32` evaluates to `2`.
The game's functions use both kinds: `num_items()`, `get_water()` and `random()` give an `f64`, positions and `get_world_size()` give an `i32`.

`%` is the remainder operator (also called modulo). It essentially divides the two numbers and then returns the remainder. You can also think of it as repeatedly subtracting the right number from the left number until the remainder is less than the right number.

`4 % 2` evaluates to `0`
`5 % 2` evaluates to `1`
`6 % 2` evaluates to `0`
`2 % 6` evaluates to `2`
`1.5 % 1.0` evaluates to `0.5`

Careful with negative numbers: in Rust the remainder has the same sign as the left number.

`-1 % 4` evaluates to `-1` (not `3`)

If you want the result that is never negative (like Python's `%`), use `rem_euclid`: `(-1_i32).rem_euclid(4)` evaluates to `3`. In the same way `(-5_i32).div_euclid(2)` evaluates to `-3` (rounded down, like Python's `//`).

Rust has no power operator. Use the `pow` method instead:

`2_i32.pow(3)` evaluates to `8`
`2.0_f64.powi(3)` evaluates to `8` (`powf` takes an `f64` exponent)

The `_i32` and `_f64` after a number tell Rust its type, which it needs to find the method.

`+` also joins texts. The left side must be a `String` and the right side a `&str`:

`"tile ".to_string() + "3"` evaluates to `"tile 3"`

To build a text from values, `format!` is easier: `format!("tile {}", 3)` also evaluates to `"tile 3"`.

## Comparison Operators
`==` and `!=` are used to check if two values are "equal"(`==`) or "not equal"(`!=`). Both sides must have the same type.

`2 == 2` evaluates to `true`
`Entity::Bush != Entity::Bush` evaluates to `false`
`3 != 3 + 1` evaluates to `true`

A value that might be missing is an `Option`. Compare it with `Some(..)`: `get_entity_type() == Some(Entity::Bush)`.

`<=, >=, <, >` can only be used on numbers. They check if the left number is "smaller or equal"(`<=`), "bigger or equal"(`>=`), "smaller" (`<`) or "bigger" (`>`) than the right number.

`1 <= 1` evaluates to `true`
`2 >= 3` evaluates to `false`
`-2 < -1` evaluates to `true`
`6 > 6` evaluates to `false`

Remember to compare an `f64` with an `f64`: write `num_items(Item::Hay) > 10.0`, not `> 10`.

You can't chain comparisons in Rust. Instead of `1 < x < 5` write `1 < x && x < 5`.

## Logic Operators
`!` (not) simply inverts the value:

`!false` evaluates to `true`
`!true` evaluates to `false`

`&&` (and) evaluates to `true` only if both values are `true`

`true && true` evaluates to `true`
`true && false` evaluates to `false`
`false && false` evaluates to `false`

`||` (or) evaluates to `true` if at least one of the values is `true`

`true || true` evaluates to `true`
`true || false` evaluates to `true`
`false || false` evaluates to `false`

`&&` and `||` only look at the right side if they need to. In `false && can_harvest()`, `can_harvest()` is never called.

## if as a value
Rust has no `? :` operator. Instead, an `if` with an `else` can be used as a value:

`let flips = if can_harvest() { 1 } else { 2 };`

Both blocks must give a value of the same type, written without a `;`.
