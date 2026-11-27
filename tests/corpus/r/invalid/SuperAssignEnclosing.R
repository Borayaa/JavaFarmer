# ERROR: assigning a variable of an enclosing function with <<- is not supported in JavaFarmer
# AT: <<-
outer <- function() {
  n <- 0
  inner <- function() {
    n <<- n + 1
  }
  inner()
  n
}
