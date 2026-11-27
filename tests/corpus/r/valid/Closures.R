# Functions as values: closures, \(x) lambdas and spawn_drone(function() ...).
make_counter <- function() {
  count <- 0
  function() {
    harvest()
    count + 1
  }
}
twice <- \(f) {
  f()
  f()
}
counter <- make_counter()
print(counter())
twice(do_a_flip)
spawn_drone(function() {
  while (TRUE) {
    if (can_harvest()) harvest()
    move(North)
  }
})
