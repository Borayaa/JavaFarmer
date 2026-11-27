# Operators
arithmetic operators: `+, -, *, /, %/%, %%, ^`
comparison operators: `==, !=, <=, >=, <, >`
boolean operators: `!, &&, ||`

Note: All numbers in the game are floating point numbers. So all arithmetic operators are floating point operators.
`%/%` is defined to just floor the number after the division.

For the assignment operator `<-` you need to unlock the "Variables" unlock.

## Introduction
Operators allow you to compare, modify and combine values. 
The arithmetic operators `+, -, *, /, %/%, %%, ^` are used to perform common mathematical operations on numbers. 
The comparison operators `==, !=, <=, >=, <, >` are used to compare values. The result is always either `TRUE` or `FALSE`.
The logic operators (also called boolean operators) `!, &&, ||` are used to combine truth values.

## Arithmetic Operators
`+` and `-` are used for addition and subtraction.

`2 + 3` evaluates to `5`
`3 - 2` evaluates to `1`

`*`, `/` and `%/%` are used for multiplication and division.

`2 * 3` evaluates to `6`
`5 / 2` evaluates to `2.5`

`%/%` does the same thing as `/` but the result is floored (rounded down to the next integer).

`5 %/% 2` evaluates to `2`

`%%` is the modulo operator, also known as the remainder operator. It essentially divides the two numbers and then returns the remainder. You can also think of it as repeatedly subtracting the right number from the left number until the remainder is less than the right number.

`4 %% 2` evaluates to `0`
`5 %% 2` evaluates to `1`
`6 %% 2` evaluates to `0`
`2 %% 6` evaluates to `2`
`1.5 %% 1` evaluates to `0.5`

`^` is the power operator.

`2 ^ 2` evaluates to `4`
`(-5) ^ 3` evaluates to `-125`

`+` only works on numbers. To join texts, use `paste()`: `paste("a", 1)` evaluates to `"a 1"` and `paste0("a", 1)` (without a space) to `"a1"`.

## Comparison Operators
`==` and `!=` are used to check if two values are "equal"(`==`) or "not equal"(`!=`). They can be used on all types of values except `NULL`. Use `is.null(x)` to check if `x` is `NULL`.

`2 == 2` evaluates to `TRUE`
`Entities$Bush != Entities$Bush` evaluates to `FALSE`
`3 != 3 + 1` evaluates to `TRUE`

`<=, >=, <, >` can only be used on numbers. They check if the left number is "smaller or equal"(`<=`), "bigger or equal"(`>=`), "smaller" (`<`) or "bigger" (`>`) than the right number.

`1 <= 1` evaluates to `TRUE`
`2 >= 3` evaluates to `FALSE`
`-2 < -1` evaluates to `TRUE`
`6 > 6` evaluates to `FALSE`

## Logic Operators
`!` simply inverts the value:

`!FALSE` evaluates to `TRUE`
`!TRUE` evaluates to `FALSE`

`&&` evaluates to `TRUE` only if both values are `TRUE`

`TRUE && TRUE` evaluates to `TRUE`
`TRUE && FALSE` evaluates to `FALSE`
`FALSE && FALSE` evaluates to `FALSE`

`||` evaluates to `TRUE` if at least one of the values is `TRUE`

`TRUE || TRUE` evaluates to `TRUE`
`TRUE || FALSE` evaluates to `TRUE`
`FALSE || FALSE` evaluates to `FALSE`

Use the double forms `&&` and `||`. The single `&` and `|` of R work on whole vectors and are not supported in JavaFarmer.

## Other Operators
`x %in% v` is `TRUE` if the value `x` is an element of the list `v`.
