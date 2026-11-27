# ERROR: assignment inside a condition is not supported in JavaFarmer
# AT: <-
x <- 0
while (x <- x + 1 < 5) print(x)
