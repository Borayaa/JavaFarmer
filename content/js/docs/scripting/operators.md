# Operators
arithmetic operators: `+, -, *, /, %, **`
comparison operators: `===, !==, <=, >=, <, >`
boolean operators: `!, &&, ||`

Note: All numbers in the game are floating point numbers, just like in JavaScript. So all arithmetic operators are floating point operators.
To divide and round down, use `Math.floor(a / b)`.

For assignment operators you need to unlock the "Variables" unlock.

## Introduction
Operators allow you to compare, modify and combine values. 
The arithmetic operators `+, -, *, /, %, **` are used to perform common mathematical operations on numbers. 
The comparison operators `===, !==, <=, >=, <, >` are used to compare values. The result is always either `true` or `false`.
The logic operators (also called boolean operators) `!, &&, ||` are used to combine truth values.

## Arithmetic Operators
`+` and `-` are used for addition and subtraction.

`2 + 3` evaluates to `5`
`3 - 2` evaluates to `1`

`+` also joins texts: `"hay: " + 5` evaluates to `"hay: 5"`.

`*` and `/` are used for multiplication and division.

`2 * 3` evaluates to `6`
`5 / 2` evaluates to `2.5`

`Math.floor()` rounds a number down to the next integer, so `Math.floor(a / b)` divides and rounds down.

`Math.floor(5 / 2)` evaluates to `2`

`%` is the remainder operator. It essentially divides the two numbers and then returns the remainder. You can also think of it as repeatedly subtracting the right number from the left number until the remainder is less than the right number.

`4 % 2` evaluates to `0`
`5 % 2` evaluates to `1`
`6 % 2` evaluates to `0`
`2 % 6` evaluates to `2`
`1.5 % 1` evaluates to `0.5`

As in all of JavaScript, the result of `%` has the sign of the left number: `-7 % 3` evaluates to `-1`. To get a result between `0` and `n - 1` also for negative numbers, write `((a % n) + n) % n`.

`**` is the power operator.

`2 ** 2` evaluates to `4`
`(-5) ** 3` evaluates to `-125`

## Comparison Operators
`===` and `!==` are used to check if two values are "equal"(`===`) or "not equal"(`!==`). They can be used on all types of values.

`2 === 2` evaluates to `true`
`Entities.Bush !== Entities.Bush` evaluates to `false`
`3 !== 3 + 1` evaluates to `true`

`==` and `!=` also work. In real JavaScript they convert values before comparing (`1 == "1"` is `true`), in JavaFarmer they mean exactly the same as `===` and `!==`. Get used to the triple form.

`<=, >=, <, >` can only be used on numbers. They check if the left number is "smaller or equal"(`<=`), "bigger or equal"(`>=`), "smaller" (`<`) or "bigger" (`>`) than the right number.

`1 <= 1` evaluates to `true`
`2 >= 3` evaluates to `false`
`-2 < -1` evaluates to `true`
`6 > 6` evaluates to `false`

## Logic Operators
`!` simply inverts the value:

`!false` evaluates to `true`
`!true` evaluates to `false`

`&&` evaluates to `true` only if both values are `true`

`true && true` evaluates to `true`
`true && false` evaluates to `false`
`false && false` evaluates to `false`

`||` evaluates to `true` if at least one of the values is `true`

`true || true` evaluates to `true`
`true || false` evaluates to `true`
`false || false` evaluates to `false`