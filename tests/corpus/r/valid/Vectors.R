# Vectors are 1-based and grow with v[[length(v) + 1]] <- x, c(v, x) and append().
v <- c(3, 1, 2)
v[[length(v) + 1]] <- 4
v[length(v) + 1] <- 5
v <- c(v, 6)
v <- append(v, 7)
v <- c(0, v)
v <- append(v, 99, after = 2)
v[[1]] <- 10
v <- v[-1]
v <- v[-length(v)]
v[[2]] <- NULL
for (i in seq_along(v)) {
  quick_print(i, v[[i]])
}
print(9 %in% v)
print(match(4, v))
