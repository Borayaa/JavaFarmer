# Operators
arithmetic operators: `+, -, *, /, %` and the builtins `@divTrunc, @divFloor, @rem, @mod`
comparison operators: `==, !=, <=, >=, <, >`
boolean operators: `!, and, or`

Note: Zig has different kinds of numbers. `i32` and `usize` hold whole numbers and `f64` holds numbers with decimal places. This matters for division: `/` on whole numbers gives a whole number, and on signed whole numbers like `i32` you have to say how to round.
(The game itself uses floating point numbers for everything. JavaFarmer makes your Zig code behave the way Zig says.)

For assignment operators like `+=` you need to unlock the "Variables" unlock.

## Introduction
Operators allow you to compare, modify and combine values. 
The arithmetic operators `+, -, *, /, %` are used to perform common mathematical operations on numbers. 
The comparison operators `==, !=, <=, >=, <, >` are used to compare values. The result is always either `true` or `false`.
The logic operators (also called boolean operators) `!, and, or` are used to combine truth values.

## Arithmetic Operators
`+` and `-` are used for addition and subtraction.

`2 + 3` evaluates to `5`
`3 - 2` evaluates to `1`

`*` and `/` are used for multiplication and division.

`2 * 3` evaluates to `6`
`5.0 / 2.0` evaluates to `2.5`

If both numbers are `usize` (or plain positive numbers like `5` and `2`), `/` is an integer division. The result is a whole number and everything after the decimal point is cut off.

`5 / 2` evaluates to `2`

If both numbers are `f64`, `/` is a normal division:

`7.0 / 2.0` evaluates to `3.5`

Zig never mixes an `f64` and a whole number variable in one operation. Convert one of them first, for example `hay / @as(f64, @floatFromInt(n))`.

## Division of signed numbers
For signed whole numbers like `i32` (numbers that can be negative), `/` and `%` are errors: `division with 'i32' and 'comptime_int': signed integers must use @divTrunc, @divFloor, or @divExact`.
That's because there are two ways to round a negative result, and Zig wants you to choose one:

`@divTrunc(a, b)` divides and cuts off the decimal places (rounds towards zero).
`@divFloor(a, b)` divides and floors the result (rounds down to the next whole number).

`@divTrunc(7, 2)` evaluates to `3`
`@divFloor(7, 2)` evaluates to `3`
`@divTrunc(-7, 2)` evaluates to `-3`
`@divFloor(-7, 2)` evaluates to `-4`

For the remainder there are also two builtins.
`@mod(a, b)` is the modulo operation. It essentially divides the two numbers and then returns the remainder. You can also think of it as repeatedly subtracting the right number from the left number until the remainder is less than the right number. The result is never negative if `b` is positive.

`@mod(4, 2)` evaluates to `0`
`@mod(5, 2)` evaluates to `1`
`@mod(6, 2)` evaluates to `0`
`@mod(2, 6)` evaluates to `2`
`@mod(-1, 4)` evaluates to `3`
`@mod(1.5, 1.0)` evaluates to `0.5`

`@rem(a, b)` is the remainder of `@divTrunc`. It has the same sign as the left number:

`@rem(-1, 4)` evaluates to `-1`

`@mod` and `@rem` also work on `f64` numbers (`%` doesn't). For `usize` numbers, which can't be negative, you can simply use `/` and `%`:

`const half = farm.getWorldSize() / 2;
const even = farm.getPosX() % 2 == 0;`

A typical use for `i32`:

`const x: i32 = @intCast(farm.getPosX());
const steps = @divTrunc(x - 5, 2);
const side = @mod(x - 5, 2);`

Zig has no power operator. Use `std.math.pow(T, base, exponent)` instead, where `T` is the type of the numbers:

`std.math.pow(i32, 2, 2)` evaluates to `4`
`std.math.pow(i32, -5, 3)` evaluates to `-125`
`std.math.pow(f64, 2.0, 0.5)` evaluates to `1.41...`

## Comparison Operators
`==` and `!=` are used to check if two values are "equal"(`==`) or "not equal"(`!=`).

`2 == 2` evaluates to `true`
`farm.Entity.Bush != farm.Entity.Bush` evaluates to `false`
`3 != 3 + 1` evaluates to `true`

To compare two pieces of text (`[]const u8`), use `std.mem.eql(u8, a, b)` instead of `a == b`. That's how it's done in Zig.

`<=, >=, <, >` can only be used on numbers. They check if the left number is "smaller or equal"(`<=`), "bigger or equal"(`>=`), "smaller" (`<`) or "bigger" (`>`) than the right number.

`1 <= 1` evaluates to `true`
`2 >= 3` evaluates to `false`
`-2 < -1` evaluates to `true`
`6 > 6` evaluates to `false`

Unlike `+`, comparisons work between different kinds of whole numbers, so `farm.getPosX() < x` with an `i32` `x` is fine.
You can't chain comparisons in Zig (`comparison operators cannot be chained`). Instead of `1 < x < 5` write `1 < x and x < 5`.

## Logic Operators
`!` (not) simply inverts the value:

`!false` evaluates to `true`
`!true` evaluates to `false`

`and` evaluates to `true` only if both values are `true`

`true and true` evaluates to `true`
`true and false` evaluates to `false`
`false and false` evaluates to `false`

`or` evaluates to `true` if at least one of the values is `true`

`true or true` evaluates to `true`
`true or false` evaluates to `true`
`false or false` evaluates to `false`

`and` and `or` only look at the right side if they need to. In `false and farm.canHarvest()`, `farm.canHarvest()` is never called.
