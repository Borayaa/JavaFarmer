# Integer division and modulo round down (like Python // and %), also for negatives.
for (a in c(7, -7, 17, -17)) {
  for (b in c(2, -2, 5, -5)) {
    cat(a, b, a %/% b, a %% b, "\n")
  }
}
print(2 ^ 10)
print(-3 ^ 2)
print((-3) ^ 2)
print(7 / 2)
print(abs(-4) + min(3, 9) + max(1, 2, 8))
print(floor(-2.5))
print(ceiling(-2.5))
print(trunc(-2.5))
print(as.integer(9.99))
print(10 - 2 - 3)
print(2 * 3 + 4 * 5)
print(5.5 %% 2)
