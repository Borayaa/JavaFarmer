# Check what we can afford before unlocking.
can_afford <- function(u) {
  cost <- get_cost(u)
  if (is.null(cost)) {
    return(FALSE)
  }
  for (item in names(cost)) {
    if (num_items(item) < cost[[item]]) {
      return(FALSE)
    }
  }
  TRUE
}

wanted <- c(Unlocks$Speed, Unlocks$Expand, Unlocks$Plant)
for (u in wanted) {
  if (can_afford(u) && num_unlocked(u) < 5) {
    unlock(u)
  }
}
comp <- get_companion()
if (!is.null(comp)) {
  quick_print(comp[[1]], comp[[2]][[1]], comp[[2]][[2]])
}
change_hat(Hats$Straw_Hat)
set_execution_speed(2)
t <- get_time()
quick_print("ticks", get_tick_count(), "time", t)
