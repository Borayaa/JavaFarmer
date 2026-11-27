# Recursive functions with implicit returns.
fib <- function(n) {
  if (n < 2) {
    n
  } else {
    fib(n - 1) + fib(n - 2)
  }
}

visit <- function(depth) {
  if (depth == 0) return()
  move(North)
  visit(depth - 1)
  move(South)
}

print(fib(10))
visit(3)
