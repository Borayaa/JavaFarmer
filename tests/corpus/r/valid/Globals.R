# Top-level variables: read anywhere, change with <<-, and <- makes a local.
level <- 1
harvested <- 0

record <- function() {
  harvested <<- harvested + 1
}

next_level <- function() {
  level <- level + 1   # a local copy: the global level stays the same
  level
}

for (i in 1:3) record()
quick_print(harvested, next_level(), level)
