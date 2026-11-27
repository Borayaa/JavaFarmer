# switch() over text: arms, fall-through and default.
kind_of <- function(item) {
  switch(item,
    hay = ,
    wood = "plant",
    carrot = "food",
    "unknown"
  )
}
for (item in c("hay", "wood", "carrot", "gold")) print(kind_of(item))
score <- 0
for (word in c("a", "b", "c", "a")) {
  switch(word,
    a = {
      score <- score + 1
    },
    b = {
      score <- score + 10
    },
    {
      score <- score + 100
    }
  )
}
print(score)
label <- switch("z", a = "A")
print(is.null(label))
