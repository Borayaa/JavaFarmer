# Named lists are maps: m[["key"]], m$key, names(m), %in% and removal with NULL.
stock <- list(hay = 0, wood = 0)
stock$hay <- num_items(Items$Hay)
stock[["wood"]] <- num_items(Items$Wood)
stock[["carrot"]] <- num_items(Items$Carrot)
if ("wood" %in% names(stock)) {
  stock[["wood"]] <- NULL
}
for (name in names(stock)) {
  quick_print(name, stock[[name]])
}
counts <- list()
counts[[Entities$Tree]] <- 1
counts[[Entities$Bush]] <- 2
print(length(counts))
print(is.null(stock$missing))
