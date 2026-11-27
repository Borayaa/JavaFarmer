# Functions: defaults, named arguments, implicit and early returns, globals.
area <- function(w, h = 2) w * h
describe <- function(n, unit = "tiles", prefix = "") {
  if (n == 0) return(paste(prefix, "nothing"))
  paste(prefix, n, unit)
}
calls <- 0
count_call <- function() {
  calls <<- calls + 1
  invisible(NULL)
}
level <- 5
bump <- function() {
  level <- level + 1
  level
}
print(area(3))
print(area(3, 4))
print(area(h = 2, w = 5))
print(describe(0))
print(describe(4, prefix = "got"))
print(describe(2, "rows"))
count_call()
count_call()
print(calls)
print(bump())
print(level)
sign_of <- function(x) {
  if (x > 0) {
    "positive"
  } else if (x < 0) {
    "negative"
  } else {
    "zero"
  }
}
for (v in c(3, -1, 0)) print(sign_of(v))
