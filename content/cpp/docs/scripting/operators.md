# Operators
arithmetic operators: `+, -, *, /, %`
comparison operators: `==, !=, <=, >=, <, >`
boolean operators: `!, &&, ||`

Note: C++ has two kinds of numbers. `int` holds whole numbers and `double` holds numbers with decimal places. This matters for division: `/` on two `int`s gives an `int`.

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
`5.0 / 2` evaluates to `2.5`

If both numbers are `int`s, `/` is an integer division. The result is an `int` and everything after the decimal point is cut off.

`5 / 2` evaluates to `2`
`-5 / 2` evaluates to `-2`

If at least one of the numbers is a `double`, the result is a `double`:

`5 / 2.0` evaluates to `2.5`

`%` is the remainder operator (also called modulo). It essentially divides the two numbers and then returns the remainder. You can also think of it as repeatedly subtracting the right number from the left number until the remainder is less than the right number.

`4 % 2` evaluates to `0`
`5 % 2` evaluates to `1`
`6 % 2` evaluates to `0`
`2 % 6` evaluates to `2`

`%` only works on `int`s. For `double`s use `std::fmod` from `<cmath>`: `std::fmod(1.5, 1)` evaluates to `0.5`.

Careful with negative numbers: in C++ the remainder has the same sign as the left number.

`-1 % 4` evaluates to `-1` (not `3`)

C++ has no power operator. Use `std::pow(base, exponent)` from `<cmath>` instead:

`std::pow(2, 3)` evaluates to `8`

`+` also joins text. One side has to be a `std::string`, and numbers have to be turned into text with `std::to_string` first:

`std::string("tile ") + std::to_string(3)` evaluates to `"tile 3"`

## Comparison Operators
`==` and `!=` are used to check if two values are "equal"(`==`) or "not equal"(`!=`).

`2 == 2` evaluates to `true`
`Entities::Bush != Entities::Bush` evaluates to `false`
`3 != 3 + 1` evaluates to `true`

`<=, >=, <, >` can only be used on numbers. They check if the left number is "smaller or equal"(`<=`), "bigger or equal"(`>=`), "smaller" (`<`) or "bigger" (`>`) than the right number.

`1 <= 1` evaluates to `true`
`2 >= 3` evaluates to `false`
`-2 < -1` evaluates to `true`
`6 > 6` evaluates to `false`

You can't chain comparisons in C++. Instead of `1 < x < 5` write `1 < x && x < 5`.

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

## The conditional operator
`condition ? a : b` evaluates to `a` if `condition` is `true` and to `b` otherwise.

`int flips = can_harvest() ? 1 : 2;`

In JavaFarmer it can only be used as a whole value, for example as the whole right side of an assignment or a `return`.
