# Named lists as maps.
stock <- list(hay = 3, wood = 5)
stock$carrot <- 2
stock[["hay"]] <- stock[["hay"]] + 10
print(stock)
print(stock$wood)
print(length(stock))
print("wood" %in% names(stock))
print(is.null(stock$gold))
stock[["wood"]] <- NULL
for (k in names(stock)) cat(k, stock[[k]], "\n")
line <- "values:"
for (v in stock) line <- paste(line, v)
print(line)
counts <- list()
for (w in c("x", "y", "x", "z", "x")) {
  if (w %in% names(counts)) {
    counts[[w]] <- counts[[w]] + 1
  } else {
    counts[[w]] <- 1
  }
}
print(counts)
backup <- counts
backup$x <- 0
print(counts$x)
