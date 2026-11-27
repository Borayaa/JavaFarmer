# Closures and functions as values.
make_adder <- function(k) {
  function(x) x + k
}
add3 <- make_adder(3)
add10 <- make_adder(10)
print(add3(4))
print(add10(4))
apply_twice <- function(f, x) f(f(x))
print(apply_twice(add3, 1))
print(apply_twice(\(x) x * x, 3))
square <- function(x) x * x
print(apply_twice(square, 2))
compose <- function(f, g) function(x) g(f(x))
inc_then_square <- compose(add3, square)
print(inc_then_square(1))
tasks <- list(make_adder(1), make_adder(2), make_adder(3))
line <- "tasks:"
for (t in tasks) line <- paste(line, t(100))
print(line)
