# Arithmetic: %/% and %% round down like Python's // and %.
a <- 17
b <- -5
quick_print(a %/% b, a %% b, a / 4, a ^ 2, -a %% 3)
quick_print(floor(2.7), ceiling(2.1), sqrt(16), abs(b), min(a, b), max(a, b, 3))
quick_print(trunc(-2.5), as.integer(7.9), as.numeric(3))
r <- random()
if (r < 0.5) do_a_flip()
