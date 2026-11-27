# Counting loops, while, repeat with break / next.
total <- 0
for (i in 1:10) total <- total + i
print(total)
line <- "down:"
for (i in 5:1) line <- paste(line, i)
print(line)
line <- "by 5:"
for (i in seq(0, 20, by = 5)) line <- paste(line, i)
print(line)
line <- "by -3:"
for (i in seq(10, 1, by = -3)) line <- paste(line, i)
print(line)
line <- "from 0:"
for (i in seq_len(4) - 1) line <- paste(line, i)
print(line)
n <- 0
for (i in seq_len(n)) print("never")
k <- 0
line <- "odd:"
repeat {
  k <- k + 1
  if (k %% 3 == 0) next
  if (k > 10) break
  line <- paste(line, k)
}
print(line)
x <- 100
steps <- 0
while (x > 1) {
  x <- x %/% 2
  steps <- steps + 1
}
cat("steps", steps, "\n")
