# ERROR: seq(by = ...) needs a constant whole number step in JavaFarmer
# AT: k
k <- 2
for (i in seq(1, 10, by = k)) print(i)
