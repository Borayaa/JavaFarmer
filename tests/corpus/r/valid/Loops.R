# Every kind of loop R offers.
total <- 0
for (i in 10:1) total <- total + i
for (i in seq(2, 10, by = 2)) total <- total + i
for (i in seq_along(c(5, 6, 7))) total <- total - i
k <- 0
repeat {
  k <- k + 1
  if (k %% 2 == 0) next
  if (k > 7) break
  total <- total + k
}
while (k > 0) k <- k - 3
print(total)
