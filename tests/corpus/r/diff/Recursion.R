# Recursion.
fact <- function(n) if (n <= 1) 1 else n * fact(n - 1)
fib <- function(n) {
  if (n < 2) {
    return(n)
  }
  fib(n - 1) + fib(n - 2)
}
gcd <- function(a, b) {
  if (b == 0) a else gcd(b, a %% b)
}
hanoi <- function(n) {
  if (n == 0) return(0)
  2 * hanoi(n - 1) + 1
}
print(fact(10))
line <- "fib:"
for (i in 0:10) line <- paste(line, fib(i))
print(line)
print(gcd(84, 36))
print(hanoi(10))
