# Text with paste / paste0 / cat.
name <- "carrot"
n <- 12
print(paste(name, n))
print(paste0(name, "s: ", n))
print(paste("a", "b", "c", sep = "-"))
print(paste("half", 2.5))
print(nchar(name))
print(as.character(7))
print(paste(TRUE, FALSE))
cat("count", n, "\n")
cat("one line\n")
cat(name, n, "\n", sep = "")
greeting <- "hi"
for (i in 1:3) greeting <- paste0(greeting, "!")
print(greeting)
print(name == "carrot")
print("apple" < "banana")
