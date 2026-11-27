# Vectors and lists: 1-based indexing, growing, removing, copies.
v <- c(5, 3, 8)
v[[length(v) + 1]] <- 1
v <- c(v, 9)
v <- c(0, v)
v <- append(v, 7, after = 2)
print(v)
print(v[[1]])
print(v[length(v)])
v <- v[-1]
v <- v[-2]
print(v)
print(length(v))
print(8 %in% v)
print(42 %in% v)
print(match(9, v))
w <- v
w[[1]] <- 100
print(v[[1]])
print(w[[1]])
double_first <- function(x) {
  x[[1]] <- x[[1]] * 2
  x
}
d <- double_first(v)
print(v[[1]])
print(d[[1]])
grid <- list(c(1, 2), c(3, 4))
grid[[2]][[1]] <- 30
print(grid)
total <- 0
for (row in grid) for (x in row) total <- total + x
print(total)
names_seen <- list("a", "b")
names_seen[[length(names_seen) + 1]] <- "c"
print(names_seen)
