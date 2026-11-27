# Text: paste, paste0, as.character and cat.
name <- "drone"
n <- 3
label <- paste(name, n)
label2 <- paste0(name, "-", n)
label3 <- paste("a", "b", "c", sep = "/")
cat(label, label2, label3, "\n")
cat("count:", n, "\n")
cat("done\n")
cat(name, n, sep = "")
print(as.character(n * 2))
print(nchar(name))
